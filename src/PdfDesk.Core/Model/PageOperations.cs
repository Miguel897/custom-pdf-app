using System.Collections.Immutable;

namespace PdfDesk.Core.Model;

/// <summary>Pure edits on <see cref="DocumentState"/>. Each returns the same instance when nothing changes.</summary>
public static class PageOperations
{
    /// <summary>
    /// Moves the given pages (keeping their relative order) so they end up before the page that is
    /// currently at <paramref name="insertIndex"/> (0..Count, Count = end).
    /// </summary>
    public static DocumentState Move(DocumentState state, IReadOnlySet<Guid> ids, int insertIndex)
    {
        var pages = state.Pages;
        insertIndex = Math.Clamp(insertIndex, 0, pages.Count);
        var moving = pages.Where(p => ids.Contains(p.Id)).ToList();
        if (moving.Count == 0)
        {
            return state;
        }

        var before = pages.Take(insertIndex).Count(p => ids.Contains(p.Id));
        var rest = pages.Where(p => !ids.Contains(p.Id)).ToList();
        rest.InsertRange(insertIndex - before, moving);
        return rest.SequenceEqual(pages) ? state : state with { Pages = [.. rest] };
    }

    /// <summary>Moves the selection one position up (-1) or down (+1).</summary>
    public static DocumentState Shift(DocumentState state, IReadOnlySet<Guid> ids, int direction)
    {
        var indices = state.Pages.Select((p, i) => (p, i)).Where(x => ids.Contains(x.p.Id)).Select(x => x.i).ToList();
        if (indices.Count == 0)
        {
            return state;
        }

        return direction < 0
            ? Move(state, ids, indices[0] - 1)
            : Move(state, ids, indices[^1] + 2);
    }

    public static DocumentState Delete(DocumentState state, IReadOnlySet<Guid> ids)
    {
        var remaining = state.Pages.RemoveAll(p => ids.Contains(p.Id));
        return remaining.Count == state.Pages.Count ? state : state with { Pages = remaining };
    }

    /// <summary>Rotates pages by <paramref name="quarterTurns"/> clockwise (negative = counter-clockwise).</summary>
    public static DocumentState Rotate(DocumentState state, IReadOnlySet<Guid> ids, int quarterTurns)
    {
        if (quarterTurns % 4 == 0 || !state.Pages.Any(p => ids.Contains(p.Id)))
        {
            return state;
        }

        return state with
        {
            Pages = [.. state.Pages.Select(p => ids.Contains(p.Id)
                ? p with { Rotation = (((p.Rotation + quarterTurns) % 4) + 4) % 4 }
                : p)],
        };
    }

    public static DocumentState Insert(DocumentState state, int index, IEnumerable<PageItem> pages)
    {
        var list = pages.ToList();
        return list.Count == 0
            ? state
            : state with { Pages = state.Pages.InsertRange(Math.Clamp(index, 0, state.Pages.Count), list) };
    }

    public static DocumentState AddOverlay(DocumentState state, Guid pageId, TextOverlay overlay) =>
        UpdatePage(state, pageId, p => p with { Overlays = p.Overlays.Add(overlay) });

    public static DocumentState UpdateOverlay(DocumentState state, Guid pageId, TextOverlay overlay) =>
        UpdatePage(state, pageId, p =>
        {
            var index = p.Overlays.FindIndex(o => o.Id == overlay.Id);
            return index < 0 || p.Overlays[index] == overlay ? p : p with { Overlays = p.Overlays.SetItem(index, overlay) };
        });

    public static DocumentState RemoveOverlay(DocumentState state, Guid pageId, Guid overlayId) =>
        UpdatePage(state, pageId, p =>
        {
            var index = p.Overlays.FindIndex(o => o.Id == overlayId);
            return index < 0 ? p : p with { Overlays = p.Overlays.RemoveAt(index) };
        });

    public static DocumentState SetFormValue(DocumentState state, FieldKey key, string value, string originalValue)
    {
        var current = state.FormValues.GetValueOrDefault(key, originalValue);
        if (current == value)
        {
            return state;
        }

        return state with
        {
            FormValues = value == originalValue ? state.FormValues.Remove(key) : state.FormValues.SetItem(key, value),
        };
    }

    private static DocumentState UpdatePage(DocumentState state, Guid pageId, Func<PageItem, PageItem> update)
    {
        var index = state.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0)
        {
            return state;
        }

        var page = update(state.Pages[index]);
        return ReferenceEquals(page, state.Pages[index]) ? state : state with { Pages = state.Pages.SetItem(index, page) };
    }
}
