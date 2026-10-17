using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfDesk.App.Services;
using PdfDesk.App.ViewModels;

namespace PdfDesk.App.Views;

/// <summary>
/// View plumbing the view model cannot do: lazy page rendering based on scroll position, thumbnail
/// drag &amp; drop, text-box focus/move handling and keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    private const string PagesFormat = "PdfDesk.Pages";
    private const double ThumbnailBoxHeight = 130;

    private readonly DispatcherTimer _renderTimer;
    private (int Index, double Fraction) _anchor;
    private bool _restoringSelection;
    private bool _closeConfirmed;

    // Thumbnail drag state.
    private Point _dragStart;
    private PageViewModel? _pressedPage;
    private bool _deferSingleSelect;
    private InsertionAdorner? _insertion;

    // Text-box move state.
    private Point _gripStart;
    private (double X, double Y) _gripOrigin;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(new DialogService(this));
        DataContext = ViewModel;
        ViewModel.FlushEdits = FlushEdits;
        ViewModel.ScrollToPageRequested += (_, index) => ScrollToPage(index);
        ViewModel.PagesReset += (_, _) => RestoreSelection();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => UpdateRendering(), Dispatcher);
        _renderTimer.Stop();

        RestorePlacement();
        Loaded += (_, _) => ViewModel.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Preview: the viewer's ScrollViewer would otherwise consume Home/End/PageUp/PageDown.
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    public MainViewModel ViewModel { get; }

    // ------------------------------------------------------------ window lifecycle

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ViewModel.PixelsPerDip = newDpi.PixelsPerDip;
        ScheduleRendering();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !ViewModel.IsDirty)
        {
            SavePlacement();
            return;
        }

        e.Cancel = true;
        await Dispatcher.Yield();
        if (await ViewModel.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderTimer.Stop();
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    private void RestorePlacement()
    {
        var s = App.Settings;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(s.WindowWidth, work.Width);
        Height = Math.Min(s.WindowHeight, work.Height);
        if (s.WindowLeft is { } left && s.WindowTop is { } top
            && left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            // Cascade extra windows so they don't sit exactly on top of each other.
            var offset = (Application.Current.Windows.OfType<MainWindow>().Count() - 1) * 24;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Min(left + offset, Math.Max(work.Left, work.Right - Width));
            Top = Math.Min(top + offset, Math.Max(work.Top, work.Bottom - Height));
        }

        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        var s = App.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        s.WindowMaximized = WindowState == WindowState.Maximized;
        App.SaveSettings();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Zoom):
                // Keep the same spot of the same page at the top after the layout changes.
                var anchor = _anchor;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (PageTop(anchor.Index) is { } top && PageHeight(anchor.Index) is { } height)
                    {
                        Viewer.ScrollToVerticalOffset(top + (anchor.Fraction * height));
                    }

                    ScheduleRendering();
                });
                break;
            case nameof(MainViewModel.CurrentPageIndex):
            case nameof(MainViewModel.PageInfo):
                if (!PageBox.IsKeyboardFocused)
                {
                    PageBox.Text = ViewModel.HasDocument ? (ViewModel.CurrentPageIndex + 1).ToString() : "";
                }

                // Keep the current page visible in the thumbnail strip (not while dragging there).
                if (_pressedPage is null && ViewModel.Pages.ElementAtOrDefault(ViewModel.CurrentPageIndex) is { } current)
                {
                    Thumbs.ScrollIntoView(current);
                }

                break;
            case nameof(MainViewModel.Tool):
                Viewer.Cursor = ViewModel.Tool == ToolMode.None ? null : Cursors.IBeam;
                break;
        }
    }

    // ------------------------------------------------------------ viewer: scrolling and lazy rendering

    private FrameworkElement? PageContainer(int index) =>
        index >= 0 && index < ViewModel.Pages.Count ? PagesHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement : null;

    private double? PageTop(int index) =>
        PageContainer(index) is { IsLoaded: true } c ? c.TransformToAncestor(PagesHost).Transform(default).Y : null;

    private double? PageHeight(int index) => PageContainer(index)?.ActualHeight;

    /// <summary>Index of the page at a vertical offset (binary search over the page containers).</summary>
    private int PageAt(double offset)
    {
        int low = 0, high = ViewModel.Pages.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if ((PageTop(mid) ?? double.MaxValue) <= offset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private void ScrollToPage(int index) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (PageTop(index) is { } top)
            {
                Viewer.ScrollToVerticalOffset(top + 4);
            }

            ScheduleRendering();
        });

    private void Viewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ViewModel.Pages.Count == 0)
        {
            return;
        }

        var offset = Viewer.VerticalOffset;
        var index = PageAt(offset);
        if (PageTop(index) is { } top && PageHeight(index) is { } height and > 0)
        {
            _anchor = (index, (offset - top) / height);
        }

        ViewModel.SetCurrentPageFromScroll(PageAt(offset + (Viewer.ViewportHeight * 0.3)));
        ScheduleRendering();
    }

    private void Viewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.SetViewport(e.NewSize);
        ScheduleRendering();
    }

    private void Viewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            (e.Delta > 0 ? ViewModel.ZoomInCommand : ViewModel.ZoomOutCommand).Execute(null);
            e.Handled = true;
        }
    }

    private void ScheduleRendering()
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    /// <summary>Renders pages in or next to the viewport; frees bitmaps of pages far away.</summary>
    private void UpdateRendering()
    {
        _renderTimer.Stop();
        var pages = ViewModel.Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var first = PageAt(Viewer.VerticalOffset);
        var last = PageAt(Viewer.VerticalOffset + Viewer.ViewportHeight);
        var pixelsPerPoint = ViewModel.Zoom * MainViewModel.PointsToDips * ViewModel.PixelsPerDip;
        for (var i = 0; i < pages.Count; i++)
        {
            if (i >= first - 1 && i <= last + 1)
            {
                pages[i].EnsureImage(pixelsPerPoint);
            }
            else if (i < first - 4 || i > last + 4)
            {
                pages[i].ReleaseImage();
            }
        }
    }

    private void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.GoToPageCommand.Execute(PageBox.Text);
            Viewer.Focus();
            PageBox.Text = (ViewModel.CurrentPageIndex + 1).ToString();
            e.Handled = true;
        }
    }

    private void PageBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => PageBox.SelectAll();

    // A click would place the caret and undo SelectAll: take focus ourselves on the first click.
    private void PageBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!PageBox.IsKeyboardFocusWithin)
        {
            PageBox.Focus();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ text boxes and form fields

    /// <summary>Commits whatever is being typed by moving focus out of the page.</summary>
    private void FlushEdits()
    {
        if (Keyboard.FocusedElement is TextBox box && box.IsDescendantOf(Viewer))
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Viewer.Focus();
        }
    }

    private void Page_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PageViewModel page } root)
        {
            return;
        }

        ViewModel.SetCurrentPageFromScroll(ViewModel.Pages.IndexOf(page));
        if (ViewModel.Tool != ToolMode.None)
        {
            var position = e.GetPosition(root);
            FlushEdits();
            ViewModel.CreateOverlayAt(page, position.X, position.Y);
        }
        else
        {
            FlushEdits();
            ViewModel.SelectOverlay(null);
            Viewer.Focus();
        }

        e.Handled = true;
    }

    private void Overlay_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: OverlayViewModel { FocusOnLoad: true } overlay } box)
        {
            overlay.FocusOnLoad = false;
            box.Focus();
            Keyboard.Focus(box);
        }
    }

    private void Overlay_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayViewModel overlay })
        {
            ViewModel.SelectOverlay(overlay);
        }
    }

    private void Overlay_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayViewModel overlay })
        {
            ViewModel.CommitOverlay(overlay);
        }
    }

    private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Viewer.Focus(); // commits
            e.Handled = true;
        }
    }

    private static FrameworkElement? FindPageRoot(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name: "PageRoot" } root)
            {
                return root;
            }
        }

        return null;
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayViewModel overlay } grip && FindPageRoot(grip) is { } root)
        {
            _gripStart = e.GetPosition(root);
            _gripOrigin = (overlay.X, overlay.Y);
            grip.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { IsMouseCaptured: true, DataContext: OverlayViewModel overlay } grip && FindPageRoot(grip) is FrameworkElement root)
        {
            var delta = e.GetPosition(root) - _gripStart;
            overlay.X = Math.Clamp(_gripOrigin.X + delta.X, 0, root.ActualWidth - 4);
            overlay.Y = Math.Clamp(_gripOrigin.Y + delta.Y, 0, root.ActualHeight - 4);
        }
    }

    private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { IsMouseCaptured: true, DataContext: OverlayViewModel overlay } grip)
        {
            grip.ReleaseMouseCapture();
            ViewModel.CommitOverlay(overlay);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ thumbnails: selection, navigation, drag & drop

    private void Thumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageViewModel page })
        {
            page.EnsureThumbnail(ThumbnailBoxHeight * ViewModel.PixelsPerDip);
        }
    }

    private void Thumbs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringSelection || ViewModel.Pages.IsResetting)
        {
            return;
        }

        ViewModel.SelectedIds.Clear();
        foreach (PageViewModel page in Thumbs.SelectedItems)
        {
            ViewModel.SelectedIds.Add(page.Id);
        }
    }

    private void RestoreSelection()
    {
        _restoringSelection = true;
        Thumbs.SelectedItems.Clear();
        foreach (var page in ViewModel.Pages.Where(p => ViewModel.SelectedIds.Contains(p.Id)))
        {
            Thumbs.SelectedItems.Add(page);
        }

        _restoringSelection = false;
        ScheduleRendering();
    }

    private PageViewModel? ThumbAt(object originalSource) =>
        originalSource is DependencyObject source && ItemsControl.ContainerFromElement(Thumbs, source) is ListBoxItem item
            ? item.DataContext as PageViewModel
            : null;

    private void Thumbs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Thumbs);
        _pressedPage = ThumbAt(e.OriginalSource);
        _deferSingleSelect = false;

        // Clicking an already selected page of a multi-selection keeps the selection so it can be dragged.
        if (_pressedPage is not null && Keyboard.Modifiers == ModifierKeys.None
            && Thumbs.SelectedItems.Count > 1 && Thumbs.SelectedItems.Contains(_pressedPage))
        {
            _deferSingleSelect = true;
            e.Handled = true;
        }
    }

    private void Thumbs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedPage is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(Thumbs) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        var ids = Thumbs.SelectedItems.Contains(_pressedPage)
            ? Thumbs.SelectedItems.Cast<PageViewModel>().Select(p => p.Id).ToHashSet()
            : [_pressedPage.Id];
        _pressedPage = null;
        _deferSingleSelect = false;
        try
        {
            DragDrop.DoDragDrop(Thumbs, new DataObject(PagesFormat, ids), DragDropEffects.Move);
        }
        finally
        {
            HideInsertion();
        }
    }

    private void Thumbs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedPage is not { } page)
        {
            return;
        }

        if (_deferSingleSelect)
        {
            Thumbs.SelectedItems.Clear();
            Thumbs.SelectedItem = page;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel.ScrollToPage(ViewModel.Pages.IndexOf(page));
        }

        _pressedPage = null;
        _deferSingleSelect = false;
    }

    /// <summary>Insertion index for a drop at the mouse position, plus the y of the insertion line.</summary>
    private (int Index, double Y) DropTarget(DragEventArgs e)
    {
        var y = e.GetPosition(Thumbs).Y;
        var lastBottom = 0.0;
        var lastIndex = -1;
        for (var i = 0; i < ViewModel.Pages.Count; i++)
        {
            if (Thumbs.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem { IsVisible: true } item)
            {
                continue;
            }

            var top = item.TransformToAncestor(Thumbs).Transform(default).Y;
            if (y < top + (item.ActualHeight / 2))
            {
                return (i, top);
            }

            lastBottom = top + item.ActualHeight;
            lastIndex = i;
        }

        return (lastIndex + 1, lastBottom);
    }

    private static IReadOnlyList<string> PdfFiles(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files
            ? [.. files.Where(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))]
            : [];

    private void Thumbs_DragOver(object sender, DragEventArgs e)
    {
        var pages = e.Data.GetDataPresent(PagesFormat);
        if (!pages && PdfFiles(e.Data).Count == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = pages ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowInsertion(DropTarget(e).Y);

        // Auto-scroll near the edges.
        if (FindScrollViewer(Thumbs) is { } scroller)
        {
            var y = e.GetPosition(Thumbs).Y;
            if (y < 30)
            {
                scroller.LineUp();
            }
            else if (y > Thumbs.ActualHeight - 30)
            {
                scroller.LineDown();
            }
        }

        e.Handled = true;
    }

    private void Thumbs_DragLeave(object sender, DragEventArgs e) => HideInsertion();

    private async void Thumbs_Drop(object sender, DragEventArgs e)
    {
        var (index, _) = DropTarget(e);
        HideInsertion();
        e.Handled = true;
        if (e.Data.GetData(PagesFormat) is HashSet<Guid> ids)
        {
            ViewModel.SelectedIds.Clear();
            ViewModel.SelectedIds.UnionWith(ids);
            ViewModel.MovePages(ids, index);
            RestoreSelection();
        }
        else if (PdfFiles(e.Data) is { Count: > 0 } files)
        {
            await ViewModel.InsertFilesAsync(files, index);
        }
    }

    private void ShowInsertion(double y)
    {
        if (_insertion is null)
        {
            _insertion = new InsertionAdorner(Thumbs);
            AdornerLayer.GetAdornerLayer(Thumbs)?.Add(_insertion);
        }

        _insertion.Y = y;
    }

    private void HideInsertion()
    {
        if (_insertion is not null)
        {
            AdornerLayer.GetAdornerLayer(Thumbs)?.Remove(_insertion);
            _insertion = null;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ------------------------------------------------------------ window: file drop, recent files, keyboard

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = PdfFiles(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (PdfFiles(e.Data) is not { Count: > 0 } files)
        {
            return;
        }

        e.Handled = true;
        Activate();
        if (ViewModel.HasDocument)
        {
            await ViewModel.InsertFilesAsync(files); // append (PRD S6)
        }
        else
        {
            await ViewModel.LoadAsync(files, combine: files.Count > 1);
        }
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { DataContext: string path })
        {
            ViewModel.OpenRecentCommand.Execute(path);
        }
    }

    private void RecentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string path })
        {
            ViewModel.OpenRecentCommand.Execute(path);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var typing = Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;

        // F1 works everywhere, also while typing. Keep ShortcutsWindow.All in sync with this method.
        if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            vm.ShowShortcutsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // While typing, Ctrl+Z/Y and Ctrl+A belong to the text box.
        ICommand? command = (ctrl, shift, key) switch
        {
            (true, false, Key.O) => vm.OpenCommand,
            (true, false, Key.S) => vm.SaveCommand,
            (true, true, Key.S) => vm.SaveAsCommand,
            (true, false, Key.Z) when !typing => vm.UndoCommand,
            (true, false, Key.Y) or (true, true, Key.Z) when !typing => vm.RedoCommand,
            (true, _, Key.OemPlus or Key.Add) => vm.ZoomInCommand,
            (true, _, Key.OemMinus or Key.Subtract) => vm.ZoomOutCommand,
            (true, _, Key.D0 or Key.NumPad0) => vm.ZoomActualSizeCommand,
            _ => null,
        };

        if (command is null && ctrl && !typing && key == Key.A && Thumbs.IsKeyboardFocusWithin)
        {
            return; // let the thumbnail list select all pages
        }

        if (command is null && !typing && !ctrl)
        {
            var alt = modifiers.HasFlag(ModifierKeys.Alt);
            command = (alt, shift, key) switch
            {
                (false, false, Key.Delete) => vm.HasSelectedOverlay && Viewer.IsKeyboardFocused ? vm.DeleteOverlayCommand : vm.DeletePagesCommand,
                (true, false, Key.Up) => vm.MoveUpCommand,
                (true, false, Key.Down) => vm.MoveDownCommand,
                (false, _, Key.Escape) => vm.CancelToolCommand,
                (false, false, Key.PageDown) => vm.NextPageCommand,
                (false, false, Key.PageUp) => vm.PreviousPageCommand,
                (false, false, Key.Home) => vm.FirstPageCommand,
                (false, false, Key.End) => vm.LastPageCommand,
                _ => null,
            };

            if (command is null && !alt && key == Key.T && vm.HasDocument)
            {
                if (shift)
                {
                    vm.IsEditableTextTool = !vm.IsEditableTextTool;
                }
                else
                {
                    vm.IsTextTool = !vm.IsTextTool;
                }

                e.Handled = true;
                return;
            }
        }

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}

/// <summary>Horizontal line showing where dragged pages will be inserted.</summary>
internal sealed class InsertionAdorner(UIElement adorned) : Adorner(adorned)
{
    private static readonly Pen Pen = CreatePen();

    public double Y
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ((FrameworkElement)AdornedElement).ActualWidth;
        drawingContext.DrawLine(Pen, new Point(8, Y), new Point(width - 8, Y));
    }

    private static Pen CreatePen()
    {
        var pen = new Pen((Brush)Application.Current.FindResource("AccentBrush"), 3);
        pen.Freeze();
        return pen;
    }
}
