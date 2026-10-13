namespace PdfDesk.Core.Model;

/// <summary>
/// The document being edited: source files plus an immutable <see cref="DocumentState"/> with
/// snapshot-based undo/redo. Nothing touches disk until <see cref="Saving.PdfExporter"/> runs.
/// </summary>
public sealed class WorkingDocument : IDisposable
{
    private const int MaxUndo = 200;

    private readonly List<SourceFile> _sources = [];
    private readonly LinkedList<DocumentState> _undo = new();
    private readonly Stack<DocumentState> _redo = new();
    private DocumentState _saved = DocumentState.Empty;
    private int _nextSourceId = 1;

    public event EventHandler? Changed;

    public IReadOnlyList<SourceFile> Sources => _sources;

    public DocumentState State { get; private set; } = DocumentState.Empty;

    /// <summary>The file this document saves to; null for a new (e.g. combined) document.</summary>
    public string? FilePath { get; private set; }

    public bool IsDirty => !ReferenceEquals(State, _saved);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public SourceFile GetSource(int id) => _sources.First(s => s.Id == id);

    /// <summary>Opens a file as a source and returns its pages, not yet inserted into the document.</summary>
    public async Task<IReadOnlyList<PageItem>> AddSourceAsync(string path, string? password = null)
    {
        var source = await SourceFile.OpenAsync(_nextSourceId++, path, password);
        _sources.Add(source);
        Log.Info($"Opened {path}: {source.PageCount} pages, {source.FormFields.Count} form widgets");
        return [.. Enumerable.Range(0, source.PageCount).Select(i => PageItem.Create(source.Id, i))];
    }

    /// <summary>
    /// Loads <paramref name="pages"/> as the initial, saved content (no undo entry).
    /// <paramref name="filePath"/> is null for new documents, which are dirty until saved.
    /// </summary>
    public void Initialize(IReadOnlyList<PageItem> pages, string? filePath)
    {
        State = DocumentState.Empty with { Pages = [.. pages] };
        _saved = filePath is null ? DocumentState.Empty : State;
        FilePath = filePath;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(Func<DocumentState, DocumentState> operation)
    {
        var next = operation(State);
        if (ReferenceEquals(next, State))
        {
            return;
        }

        _undo.AddLast(State);
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        State = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Last is not { } last)
        {
            return;
        }

        _undo.RemoveLast();
        _redo.Push(State);
        State = last.Value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next))
        {
            return;
        }

        _undo.AddLast(State);
        State = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        _saved = State;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.Dispose();
        }

        _sources.Clear();
    }
}
