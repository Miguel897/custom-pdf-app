using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfDesk.App.Services;
using PdfDesk.Core;
using PdfDesk.Core.Model;
using PdfDesk.Core.Pdfium;
using PdfDesk.Core.Saving;

namespace PdfDesk.App.ViewModels;

public enum ToolMode
{
    None,
    Text,
    EditableText,
}

public enum ZoomMode
{
    Custom,
    FitWidth,
    FitPage,
}

/// <summary>
/// Owns one <see cref="WorkingDocument"/> and everything the main window does with it. Page edits go
/// through <see cref="WorkingDocument.Apply"/> so they are undoable; the page list is then rebuilt.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double PointsToDips = 96.0 / 72;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 4;

    private readonly IDialogService _dialogs;
    private WorkingDocument _document = new();
    private Size _viewport;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _document.Changed += OnDocumentChanged;
        RecentFiles = [.. App.Settings.RecentFiles];
    }

    /// <summary>Raised after the page list was replaced, so the view can restore the selection.</summary>
    public event EventHandler? PagesReset;

    /// <summary>Asks the view to scroll the viewer so the given page index is at the top.</summary>
    public event EventHandler<int>? ScrollToPageRequested;

    /// <summary>Set by the view: moves focus out of text boxes so pending edits are committed.</summary>
    public Action? FlushEdits { get; set; }

    public PageCollection Pages { get; } = [];

    public HashSet<Guid> SelectedIds { get; } = [];

    public ObservableCollection<string> RecentFiles { get; }

    public IReadOnlyList<double> FontSizes { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36];

    public IReadOnlyList<KeyValuePair<string, string>> Colors { get; } =
    [
        new("Black", "#000000"),
        new("Blue", "#0033CC"),
        new("Red", "#CC0000"),
    ];

    public bool HasDocument => Pages.Count > 0;

    public bool IsDirty => _document.IsDirty;

    public bool CanUndo => _document.CanUndo;

    public bool CanRedo => _document.CanRedo;

    public string DocumentName => _document.FilePath is { } path ? Path.GetFileName(path) : HasDocument ? "Combined (unsaved)" : "";

    public string Title => HasDocument ? $"{DocumentName}{(IsDirty ? " •" : "")} — PdfDesk" : "PdfDesk";

    public string PageInfo => HasDocument ? $"Page {CurrentPageIndex + 1} / {Pages.Count}" : "";

    public string SourceInfo => HasDocument
        ? $"{_document.State.Pages.Select(p => p.SourceId).Distinct().Count()} source(s) · {Pages.Count} pages"
        : "";

    public string ZoomText => $"{Zoom * 100:0}%";

    public bool IsOpenWithRegistered => OpenWithRegistration.IsRegistered();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    public partial double Zoom { get; private set; } = 1;

    [ObservableProperty]
    public partial ZoomMode ZoomMode { get; private set; } = ZoomMode.FitWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextTool), nameof(IsEditableTextTool))]
    public partial ToolMode Tool { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedOverlay), nameof(CurrentFontSize), nameof(CurrentColor))]
    public partial OverlayViewModel? SelectedOverlay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    public partial int CurrentPageIndex { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a PDF, drop files here, or combine a folder.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsTextTool
    {
        get => Tool == ToolMode.Text;
        set => Tool = value ? ToolMode.Text : ToolMode.None;
    }

    public bool IsEditableTextTool
    {
        get => Tool == ToolMode.EditableText;
        set => Tool = value ? ToolMode.EditableText : ToolMode.None;
    }

    public bool HasSelectedOverlay => SelectedOverlay is not null;

    /// <summary>Font size of the selected text box, or the default for new ones.</summary>
    public double CurrentFontSize
    {
        get => SelectedOverlay?.FontSize ?? App.Settings.DefaultFontSize;
        set
        {
            App.Settings.DefaultFontSize = value;
            if (SelectedOverlay is { } overlay && overlay.FontSize != value)
            {
                overlay.FontSize = value;
                CommitOverlay(overlay);
            }

            OnPropertyChanged();
        }
    }

    public string CurrentColor
    {
        get => SelectedOverlay?.Color ?? App.Settings.DefaultColor;
        set
        {
            App.Settings.DefaultColor = value;
            if (SelectedOverlay is { } overlay && overlay.Color != value)
            {
                overlay.Color = value;
                CommitOverlay(overlay);
            }

            OnPropertyChanged();
        }
    }

    public double PixelsPerDip { get; set; } = 1;

    // ---------------------------------------------------------------- loading

    /// <summary>Loads files into this window: one file is opened, several (or <paramref name="combine"/>) are combined.</summary>
    public async Task LoadAsync(IReadOnlyList<string> paths, bool combine = false)
    {
        var document = new WorkingDocument();
        var pages = await OpenSourcesAsync(document, paths);
        if (pages.Count == 0)
        {
            document.Dispose();
            return;
        }

        var single = paths.Count == 1 && !combine;
        document.Initialize(pages, single ? paths[0] : null);
        ReplaceDocument(document);
        if (single)
        {
            AddRecent(paths[0]);
        }

        Status = single ? $"Opened {Path.GetFileName(paths[0])}" : $"Combined {document.Sources.Count} files — not saved yet";
        ScrollToPage(0);
    }

    /// <summary>Inserts the pages of other PDFs at <paramref name="index"/> (end when null).</summary>
    public async Task InsertFilesAsync(IReadOnlyList<string> paths, int? index = null)
    {
        if (!HasDocument)
        {
            await LoadAsync(paths);
            return;
        }

        FlushEdits?.Invoke();
        var pages = await OpenSourcesAsync(_document, paths);
        if (pages.Count > 0)
        {
            var at = index ?? Pages.Count;
            _document.Apply(s => PageOperations.Insert(s, at, pages));
            Status = $"Inserted {pages.Count} page(s)";
        }
    }

    private async Task<List<PageItem>> OpenSourcesAsync(WorkingDocument document, IReadOnlyList<string> paths)
    {
        var pages = new List<PageItem>();
        var problems = new List<string>();
        IsBusy = true;
        try
        {
            foreach (var path in paths)
            {
                string? password = null;
                while (true)
                {
                    try
                    {
                        pages.AddRange(await document.AddSourceAsync(path, password));
                        if (document.Sources[^1].SaveProblem is { } problem)
                        {
                            problems.Add($"{Path.GetFileName(path)}: {problem}");
                        }

                        break;
                    }
                    catch (PdfOpenException ex) when (ex.Error == PdfOpenError.PasswordRequired)
                    {
                        password = _dialogs.AskPassword(Path.GetFileName(path), retry: password is not null);
                        if (password is null)
                        {
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is PdfOpenException or IOException or UnauthorizedAccessException)
                    {
                        Log.Error($"Opening {path} failed", ex);
                        problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
                        break;
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (problems.Count > 0)
        {
            _dialogs.ShowError(string.Join("\n\n", problems));
        }

        return pages;
    }

    private void ReplaceDocument(WorkingDocument document)
    {
        var old = _document;
        old.Changed -= OnDocumentChanged;
        _document = document;
        _document.Changed += OnDocumentChanged;
        SelectedIds.Clear();
        SelectedOverlay = null;
        CurrentPageIndex = 0;
        Rebuild();
        ApplyZoomMode();
        old.Dispose();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var state = _document.State;
        var existing = Pages.ToDictionary(p => p.Id);
        var list = new List<PageViewModel>(state.Pages.Count);
        for (var i = 0; i < state.Pages.Count; i++)
        {
            var item = state.Pages[i];
            if (!existing.Remove(item.Id, out var page))
            {
                page = new PageViewModel(this, _document.GetSource(item.SourceId), item) { Scale = Zoom * PointsToDips };
            }

            page.Update(item, state);
            page.Number = i + 1;
            list.Add(page);
        }

        foreach (var removed in existing.Values)
        {
            removed.ReleaseImage();
        }

        if (!list.SequenceEqual(Pages))
        {
            Pages.ReplaceAll(list);
            SelectedIds.IntersectWith(list.Select(p => p.Id));
            if (SelectedOverlay is { } overlay && !list.Contains(overlay.Page))
            {
                SelectedOverlay = null;
            }

            CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, list.Count - 1));
            PagesReset?.Invoke(this, EventArgs.Empty);
        }

        UpdateCurrentFlags();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PageInfo));
        OnPropertyChanged(nameof(SourceInfo));
    }

    // ---------------------------------------------------------------- file commands

    [RelayCommand]
    private async Task OpenAsync()
    {
        var paths = _dialogs.AskOpenPaths(App.Settings.LastFolder, multiple: false);
        if (paths.Count == 0 || !await ConfirmCloseAsync())
        {
            return;
        }

        App.Settings.LastFolder = Path.GetDirectoryName(paths[0]);
        await LoadAsync(paths);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            _dialogs.ShowError($"File not found:\n{path}");
            RecentFiles.Remove(path);
            App.Settings.RecentFiles.Remove(path);
            return;
        }

        if (await ConfirmCloseAsync())
        {
            await LoadAsync([path]);
        }
    }

    [RelayCommand]
    private async Task InsertPagesAsync()
    {
        var paths = _dialogs.AskOpenPaths(App.Settings.LastFolder, multiple: true);
        if (paths.Count > 0)
        {
            var at = SelectedIndices().DefaultIfEmpty(Pages.Count - 1).Max() + 1;
            await InsertFilesAsync(paths, at);
        }
    }

    [RelayCommand]
    private async Task CombineAsync()
    {
        if (_dialogs.AskCombine() is not { Count: > 0 } paths)
        {
            return;
        }

        if (HasDocument)
        {
            App.OpenWindow(paths, combine: true); // keep this document, open the result next to it
        }
        else
        {
            await LoadAsync(paths, combine: true);
        }
    }

    [RelayCommand]
    private Task<bool> SaveAsync() =>
        _document.FilePath is { } path ? SaveToAsync(path) : SaveAsAsync();

    [RelayCommand]
    private Task<bool> SaveAsAsync()
    {
        if (!HasDocument)
        {
            return Task.FromResult(false);
        }

        FlushEdits?.Invoke();
        var folder = _document.FilePath is { } current ? Path.GetDirectoryName(current)
            : Path.GetDirectoryName(_document.Sources.FirstOrDefault()?.Path) ?? App.Settings.LastFolder;
        var name = _document.FilePath is { } file ? Path.GetFileName(file) : $"Combined_{DateTime.Now:yyyy-MM-dd_HHmmss}.pdf";
        var target = _dialogs.AskSavePath(folder, name);
        return target is null ? Task.FromResult(false) : SaveToAsync(target);
    }

    private async Task<bool> SaveToAsync(string path)
    {
        if (!HasDocument)
        {
            return false;
        }

        FlushEdits?.Invoke();
        var sources = _document.Sources.ToList();
        var state = _document.State;
        IsBusy = true;
        try
        {
            await Task.Run(() => PdfExporter.Save(sources, state, path));
            _document.MarkSaved(path);
            AddRecent(path);
            App.Settings.LastFolder = Path.GetDirectoryName(path);
            Status = $"Saved {Path.GetFileName(path)} at {DateTime.Now:HH:mm}";
            return true;
        }
        catch (PdfSaveException ex)
        {
            _dialogs.ShowError(ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Returns true when the current document may be closed (saved, discarded or unchanged).</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        FlushEdits?.Invoke();
        if (!IsDirty)
        {
            return true;
        }

        return _dialogs.AskSaveChanges(DocumentName) switch
        {
            SaveChoice.Save => await SaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    [RelayCommand]
    private void OpenInDefaultApp()
    {
        if (_document.FilePath is not { } path)
        {
            _dialogs.ShowInfo("Save the document first.");
            return;
        }

        if (IsDirty)
        {
            _dialogs.ShowInfo("The default app shows the last saved version; your unsaved changes are not included.");
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_document.FilePath is { } path)
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }

    [RelayCommand]
    private void ToggleOpenWith()
    {
        try
        {
            if (OpenWithRegistration.IsRegistered())
            {
                OpenWithRegistration.Unregister();
                _dialogs.ShowInfo("PdfDesk was removed from \"Open with\".");
            }
            else
            {
                OpenWithRegistration.Register();
                _dialogs.ShowInfo("PdfDesk now appears in Explorer under right-click › Open with.\n\n" +
                    "To make it the default, choose Open with › Choose another app › PdfDesk › Always.\n\n" +
                    "If you move PdfDesk.exe, use this button again.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Open with registration failed", ex);
            _dialogs.ShowError("The registration failed: " + ex.Message);
        }

        OnPropertyChanged(nameof(IsOpenWithRegistered));
    }

    [RelayCommand]
    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start("explorer.exe", Log.Folder);
    }

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    private void AddRecent(string path)
    {
        App.Settings.AddRecent(path);
        RecentFiles.Clear();
        foreach (var file in App.Settings.RecentFiles)
        {
            RecentFiles.Add(file);
        }
    }

    // ---------------------------------------------------------------- page commands

    /// <summary>Selected pages, or the current page when nothing is selected.</summary>
    private HashSet<Guid> Targets() =>
        SelectedIds.Count > 0 ? [.. SelectedIds] : Pages.Count > CurrentPageIndex ? [Pages[CurrentPageIndex].Id] : [];

    private IEnumerable<int> SelectedIndices() =>
        Pages.Select((p, i) => (p, i)).Where(x => SelectedIds.Contains(x.p.Id)).Select(x => x.i);

    private void Apply(Func<DocumentState, DocumentState> operation)
    {
        FlushEdits?.Invoke();
        _document.Apply(operation);
    }

    [RelayCommand]
    private void Undo()
    {
        FlushEdits?.Invoke();
        _document.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        FlushEdits?.Invoke();
        _document.Redo();
    }

    [RelayCommand]
    private void RotateLeft() => Apply(s => PageOperations.Rotate(s, Targets(), -1));

    [RelayCommand]
    private void RotateRight() => Apply(s => PageOperations.Rotate(s, Targets(), 1));

    [RelayCommand]
    private void DeletePages()
    {
        var targets = Targets();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == Pages.Count)
        {
            _dialogs.ShowInfo("A document needs at least one page.");
            return;
        }

        Apply(s => PageOperations.Delete(s, targets));
        Status = $"Deleted {targets.Count} page(s) — Ctrl+Z to undo";
    }

    [RelayCommand]
    private void MoveUp() => MoveAndFollow(s => PageOperations.Shift(s, Targets(), -1));

    [RelayCommand]
    private void MoveDown() => MoveAndFollow(s => PageOperations.Shift(s, Targets(), +1));

    [RelayCommand]
    private void MoveToStart() => MoveAndFollow(s => PageOperations.Move(s, Targets(), 0));

    [RelayCommand]
    private void MoveToEnd() => MoveAndFollow(s => PageOperations.Move(s, Targets(), s.Pages.Count));

    /// <summary>Called by drag &amp; drop in the thumbnail strip.</summary>
    public void MovePages(IReadOnlySet<Guid> ids, int insertIndex) =>
        MoveAndFollow(s => PageOperations.Move(s, ids, insertIndex));

    private void MoveAndFollow(Func<DocumentState, DocumentState> operation)
    {
        var current = Pages.ElementAtOrDefault(CurrentPageIndex)?.Id;
        Apply(operation);
        var first = Pages.Select((p, i) => (p, i)).FirstOrDefault(x => SelectedIds.Contains(x.p.Id) || x.p.Id == current);
        if (first.p is not null)
        {
            ScrollToPage(first.i);
        }
    }

    public void SetFormValue(FieldKey key, string value, string original) =>
        _document.Apply(s => PageOperations.SetFormValue(s, key, value, original));

    // ---------------------------------------------------------------- navigation & zoom

    public void ScrollToPage(int index)
    {
        if (Pages.Count == 0)
        {
            return;
        }

        CurrentPageIndex = Math.Clamp(index, 0, Pages.Count - 1);
        UpdateCurrentFlags();
        ScrollToPageRequested?.Invoke(this, CurrentPageIndex);
    }

    /// <summary>Called by the view while scrolling.</summary>
    public void SetCurrentPageFromScroll(int index)
    {
        if (index != CurrentPageIndex && index >= 0 && index < Pages.Count)
        {
            CurrentPageIndex = index;
            UpdateCurrentFlags();
        }
    }

    [RelayCommand]
    private void GoToPage(string? text)
    {
        if (int.TryParse(text, out var number))
        {
            ScrollToPage(number - 1);
        }
    }

    [RelayCommand]
    private void NextPage() => ScrollToPage(CurrentPageIndex + 1);

    [RelayCommand]
    private void PreviousPage() => ScrollToPage(CurrentPageIndex - 1);

    [RelayCommand]
    private void FirstPage() => ScrollToPage(0);

    [RelayCommand]
    private void LastPage() => ScrollToPage(Pages.Count - 1);

    private void UpdateCurrentFlags()
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].IsCurrent = i == CurrentPageIndex;
        }
    }

    [RelayCommand]
    private void ZoomIn() => SetZoom(Zoom * 1.25, ZoomMode.Custom);

    [RelayCommand]
    private void ZoomOut() => SetZoom(Zoom / 1.25, ZoomMode.Custom);

    [RelayCommand]
    private void ZoomActualSize() => SetZoom(1, ZoomMode.Custom);

    [RelayCommand]
    private void FitWidth()
    {
        ZoomMode = ZoomMode.FitWidth;
        ApplyZoomMode();
    }

    [RelayCommand]
    private void FitPage()
    {
        ZoomMode = ZoomMode.FitPage;
        ApplyZoomMode();
    }

    /// <summary>Called by the view when the viewer area changes size.</summary>
    public void SetViewport(Size size)
    {
        _viewport = size;
        ApplyZoomMode();
    }

    private void ApplyZoomMode()
    {
        if (Pages.Count == 0 || _viewport.Width <= 0 || ZoomMode == ZoomMode.Custom)
        {
            return;
        }

        const double margin = 72; // items padding, page border, vertical scrollbar
        var widest = Pages.Max(p => p.RotatedWidth) * PointsToDips;
        var zoom = (_viewport.Width - margin) / widest;
        if (ZoomMode == ZoomMode.FitPage)
        {
            var page = Pages[Math.Min(CurrentPageIndex, Pages.Count - 1)];
            zoom = Math.Min(zoom, (_viewport.Height - 24) / (page.RotatedHeight * PointsToDips));
        }

        SetZoom(zoom, ZoomMode);
    }

    private void SetZoom(double zoom, ZoomMode mode)
    {
        ZoomMode = mode;
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        foreach (var page in Pages)
        {
            page.Scale = Zoom * PointsToDips;
        }
    }

    // ---------------------------------------------------------------- text tools

    /// <summary>
    /// Called by the view when a page is clicked with a text tool active. <paramref name="x"/>/<paramref name="y"/>
    /// are base-frame points. Returns the new (pending) box, focused once it is shown.
    /// </summary>
    public OverlayViewModel? CreateOverlayAt(PageViewModel page, double x, double y)
    {
        if (Tool == ToolMode.None)
        {
            return null;
        }

        // Keep the text upright on screen: undo the user's page rotation.
        var rotation = (4 - page.Item.Rotation) % 4;
        var size = App.Settings.DefaultFontSize;
        var half = size * PdfExporter.LineSpacing / 2;
        var angle = rotation * Math.PI / 2;
        var overlay = new OverlayViewModel(this, page, Guid.NewGuid())
        {
            // Centre the first line vertically on the click point.
            X = x + (half * Math.Round(Math.Sin(angle))),
            Y = y - (half * Math.Round(Math.Cos(angle))),
            Rotation = rotation,
            FontSize = size,
            Color = App.Settings.DefaultColor,
            Kind = Tool == ToolMode.EditableText ? TextKind.Annotation : TextKind.Flattened,
            FocusOnLoad = true,
        };
        page.AddPendingOverlay(overlay);
        SelectOverlay(overlay);
        return overlay;
    }

    public void SelectOverlay(OverlayViewModel? overlay)
    {
        if (SelectedOverlay is { } previous && previous != overlay)
        {
            previous.IsSelected = false;
        }

        if (overlay is not null)
        {
            overlay.IsSelected = true;
        }

        SelectedOverlay = overlay;
    }

    /// <summary>Writes a text box into the document state (adds, updates or removes it).</summary>
    public void CommitOverlay(OverlayViewModel overlay)
    {
        var page = overlay.Page;
        var empty = string.IsNullOrWhiteSpace(overlay.Text);
        if (overlay.IsPending)
        {
            if (empty)
            {
                page.RemovePendingOverlay(overlay);
                if (SelectedOverlay == overlay)
                {
                    SelectOverlay(null);
                }

                return;
            }

            var model = overlay.ToModel();
            overlay.Load(model); // no longer pending; the rebuild then finds it by id
            _document.Apply(s => PageOperations.AddOverlay(s, page.Id, model));
        }
        else if (empty)
        {
            _document.Apply(s => PageOperations.RemoveOverlay(s, page.Id, overlay.Id));
        }
        else
        {
            var model = overlay.ToModel();
            _document.Apply(s => PageOperations.UpdateOverlay(s, page.Id, model));
        }
    }

    [RelayCommand]
    private void DeleteOverlay()
    {
        if (SelectedOverlay is not { } overlay)
        {
            return;
        }

        SelectOverlay(null);
        if (overlay.IsPending)
        {
            overlay.Page.RemovePendingOverlay(overlay);
        }
        else
        {
            _document.Apply(s => PageOperations.RemoveOverlay(s, overlay.Page.Id, overlay.Id));
        }
    }

    [RelayCommand]
    private void ConvertOverlay()
    {
        if (SelectedOverlay is { } overlay)
        {
            overlay.Kind = overlay.Kind == TextKind.Flattened ? TextKind.Annotation : TextKind.Flattened;
            CommitOverlay(overlay);
            Status = overlay.Kind == TextKind.Annotation
                ? "Text will be saved as an editable annotation"
                : "Text will be drawn into the page";
        }
    }

    [RelayCommand]
    private void InsertDate()
    {
        if (SelectedOverlay is { } overlay)
        {
            overlay.Text += DateTime.Now.ToString(App.Settings.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
            CommitOverlay(overlay);
        }
    }

    [RelayCommand]
    private void CancelTool()
    {
        Tool = ToolMode.None;
        FlushEdits?.Invoke();
        SelectOverlay(null);
    }

    public void Dispose() => _document.Dispose();
}

/// <summary>ObservableCollection that can swap all items with a single Reset notification.</summary>
public sealed class PageCollection : ObservableCollection<PageViewModel>
{
    /// <summary>True while the Reset notification is raised (list controls then clear their selection).</summary>
    public bool IsResetting { get; private set; }

    public void ReplaceAll(IEnumerable<PageViewModel> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        IsResetting = true;
        try
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        finally
        {
            IsResetting = false;
        }
    }
}
