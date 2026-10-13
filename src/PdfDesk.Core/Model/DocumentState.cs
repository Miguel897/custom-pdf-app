using System.Collections.Immutable;

namespace PdfDesk.Core.Model;

/// <summary>How a text box ends up in the saved PDF.</summary>
public enum TextKind
{
    /// <summary>Drawn into the page content: looks the same everywhere, cannot be changed later.</summary>
    Flattened,

    /// <summary>Saved as a FreeText annotation: other viewers can still move and edit it.</summary>
    Annotation,
}

/// <summary>
/// Text typed onto a page. Coordinates are in points in the page's <b>base frame</b>: the page as
/// PDFium shows it without extra rotation (its own <c>/Rotate</c> included), origin top-left, y down.
/// <paramref name="Rotation"/> is the text direction in clockwise quarter turns around (X, Y).
/// </summary>
public sealed record TextOverlay(
    Guid Id,
    TextKind Kind,
    double X,
    double Y,
    int Rotation,
    string Text,
    double FontSize,
    string Color);

/// <summary>One page of the working document: a reference to a source page plus edits.</summary>
public sealed record PageItem(
    Guid Id,
    int SourceId,
    int PageIndex,
    int Rotation,
    ImmutableList<TextOverlay> Overlays)
{
    public static PageItem Create(int sourceId, int pageIndex) =>
        new(Guid.NewGuid(), sourceId, pageIndex, 0, []);
}

/// <summary>Identifies an AcroForm field of a source file by its fully qualified name.</summary>
public readonly record struct FieldKey(int SourceId, string Name);

/// <summary>
/// Immutable snapshot of everything the user can change. Undo/redo keeps a stack of snapshots.
/// Checkbox values are stored as "true"/"false".
/// </summary>
public sealed record DocumentState(
    ImmutableList<PageItem> Pages,
    ImmutableDictionary<FieldKey, string> FormValues)
{
    public static DocumentState Empty { get; } = new([], ImmutableDictionary<FieldKey, string>.Empty);
}
