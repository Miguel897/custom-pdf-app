using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfDesk.Core.Combine;

namespace PdfDesk.App.ViewModels;

public sealed partial class CandidateViewModel(CombineCandidate candidate, Action changed) : ObservableObject
{
    public CombineCandidate Candidate { get; } = candidate;

    public string FileName => Candidate.FileName;

    public string RelativeFolder { get; init; } = "";

    public string Pages => Candidate.Problem is null ? $"{Candidate.PageCount} p" : "—";

    public string Size => Candidate.Size switch
    {
        < 1024 * 1024 => $"{Math.Max(1, Candidate.Size / 1024)} KB",
        _ => $"{Candidate.Size / (1024.0 * 1024):0.#} MB",
    };

    public string Modified => Candidate.Modified.ToString("yyyy-MM-dd HH:mm");

    public string? Problem => Candidate.Problem is { } p ? $"{p}, skipped" : null;

    public bool IsUsable => Candidate.Problem is null;

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = candidate.Problem is null;

    partial void OnIsIncludedChanged(bool value) => changed();
}

/// <summary>Combine dialog (PRD §5.3): pick a folder, choose and order its PDFs.</summary>
public sealed partial class CombineViewModel : ObservableObject
{
    private CancellationTokenSource? _scan;

    public ObservableCollection<CandidateViewModel> Items { get; } = [];

    /// <summary>The included files in order, set when the user clicks Combine.</summary>
    public IReadOnlyList<string>? Result { get; private set; }

    [ObservableProperty]
    public partial string Folder { get; set; } = App.Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    [ObservableProperty]
    public partial CombineSort Sort { get; set; } = CombineSort.Name;

    [ObservableProperty]
    public partial CandidateViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    public int TotalPages => Items.Where(i => i.IsIncluded).Sum(i => i.Candidate.PageCount);

    public int IncludedCount => Items.Count(i => i.IsIncluded);

    public string Summary => $"{IncludedCount} file(s) · {TotalPages} pages";

    public bool SortByName
    {
        get => Sort == CombineSort.Name;
        set
        {
            if (value)
            {
                Sort = CombineSort.Name;
            }
        }
    }

    public bool SortByDate
    {
        get => Sort == CombineSort.Date;
        set
        {
            if (value)
            {
                Sort = CombineSort.Date;
            }
        }
    }

    public bool SortManual
    {
        get => Sort == CombineSort.Manual;
        set
        {
            if (value)
            {
                Sort = CombineSort.Manual;
            }
        }
    }

    public async Task ScanAsync()
    {
        _scan?.Cancel();
        var cancel = new CancellationTokenSource();
        _scan = cancel;
        Items.Clear();
        OnSelectionChanged();
        if (!Directory.Exists(Folder))
        {
            Message = "Folder not found.";
            return;
        }

        IsScanning = true;
        Message = "Reading files…";
        try
        {
            var found = await FolderScanner.ScanAsync(Folder, IncludeSubfolders, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            foreach (var candidate in FolderScanner.Sort(found, Sort == CombineSort.Manual ? CombineSort.Name : Sort))
            {
                var folder = Path.GetRelativePath(Folder, Path.GetDirectoryName(candidate.Path)!);
                Items.Add(new CandidateViewModel(candidate, OnSelectionChanged) { RelativeFolder = folder == "." ? "" : folder });
            }

            Message = Items.Count == 0 ? "No PDF files in this folder." : "";
            App.Settings.LastFolder = Folder;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
        finally
        {
            if (_scan == cancel)
            {
                IsScanning = false;
            }

            OnSelectionChanged();
        }
    }

    partial void OnFolderChanged(string value) => _ = ScanAsync();

    partial void OnIncludeSubfoldersChanged(bool value) => _ = ScanAsync();

    partial void OnSortChanged(CombineSort value)
    {
        OnPropertyChanged(nameof(SortByName));
        OnPropertyChanged(nameof(SortByDate));
        OnPropertyChanged(nameof(SortManual));
        if (value == CombineSort.Manual)
        {
            return;
        }

        var selected = SelectedItem;
        var sorted = FolderScanner.Sort(Items.Select(i => i.Candidate), value)
            .Select(c => Items.First(i => i.Candidate == c)).ToList();
        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }

        SelectedItem = selected;
    }

    [RelayCommand]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveDown() => MoveSelected(+1);

    private void MoveSelected(int direction)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var index = Items.IndexOf(item);
        var target = index + direction;
        if (target < 0 || target >= Items.Count)
        {
            return;
        }

        Sort = CombineSort.Manual;
        Items.Move(index, target);
        SelectedItem = item;
    }

    /// <summary>Called by drag &amp; drop in the list.</summary>
    public void MoveItem(CandidateViewModel item, int targetIndex)
    {
        var index = Items.IndexOf(item);
        targetIndex = Math.Clamp(targetIndex > index ? targetIndex - 1 : targetIndex, 0, Items.Count - 1);
        if (index >= 0 && index != targetIndex)
        {
            Sort = CombineSort.Manual;
            Items.Move(index, targetIndex);
        }
    }

    public bool Accept()
    {
        var included = Items.Where(i => i.IsIncluded && i.IsUsable).Select(i => i.Candidate.Path).ToList();
        if (included.Count == 0)
        {
            Message = "Select at least one file.";
            return false;
        }

        Result = included;
        return true;
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(Summary));
    }
}
