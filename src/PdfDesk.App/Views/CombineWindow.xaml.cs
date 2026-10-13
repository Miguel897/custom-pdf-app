using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PdfDesk.App.ViewModels;

namespace PdfDesk.App.Views;

public partial class CombineWindow : Window
{
    private Point _dragStart;
    private CandidateViewModel? _dragItem;

    public CombineWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += (_, _) => _ = ViewModel.ScanAsync();
    }

    public CombineViewModel ViewModel { get; } = new();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = ViewModel.Folder };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.Folder = dialog.FolderName;
        }
    }

    private void Folder_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }

    private void Combine_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accept())
        {
            DialogResult = true;
        }
    }

    // Drag & drop reordering inside the list (switches sorting to Manual).
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(List);
        _dragItem = (e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(List, source) is ListViewItem item
            && e.OriginalSource is not CheckBox
            ? item.DataContext as CandidateViewModel
            : null;
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(List) - _dragStart;
        if (Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var item = _dragItem;
            _dragItem = null;
            DragDrop.DoDragDrop(List, new DataObject(typeof(CandidateViewModel), item), DragDropEffects.Move);
        }
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(CandidateViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CandidateViewModel)) is not CandidateViewModel item)
        {
            return;
        }

        var position = e.GetPosition(List);
        var target = ViewModel.Items.Count;
        for (var i = 0; i < ViewModel.Items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem container)
            {
                var top = container.TransformToAncestor(List).Transform(new Point(0, 0)).Y;
                if (position.Y < top + (container.ActualHeight / 2))
                {
                    target = i;
                    break;
                }
            }
        }

        ViewModel.MoveItem(item, target);
        List.SelectedItem = item;
    }
}
