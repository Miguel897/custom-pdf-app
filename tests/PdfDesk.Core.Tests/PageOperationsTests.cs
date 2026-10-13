using PdfDesk.Core.Model;

namespace PdfDesk.Core.Tests;

public class PageOperationsTests
{
    private static DocumentState State(int count) =>
        DocumentState.Empty with { Pages = [.. Enumerable.Range(0, count).Select(i => PageItem.Create(1, i))] };

    private static int[] Order(DocumentState state) => [.. state.Pages.Select(p => p.PageIndex)];

    private static HashSet<Guid> Ids(DocumentState state, params int[] indices) => [.. indices.Select(i => state.Pages[i].Id)];

    [Theory]
    [InlineData(new[] { 0 }, 3, new[] { 1, 2, 0, 3, 4 })]
    [InlineData(new[] { 4 }, 0, new[] { 4, 0, 1, 2, 3 })]
    [InlineData(new[] { 1, 3 }, 5, new[] { 0, 2, 4, 1, 3 })]
    [InlineData(new[] { 0, 4 }, 2, new[] { 1, 0, 4, 2, 3 })]
    public void Move_PlacesSelectionBeforeTarget(int[] selected, int insertIndex, int[] expected)
    {
        var state = State(5);
        Assert.Equal(expected, Order(PageOperations.Move(state, Ids(state, selected), insertIndex)));
    }

    [Fact]
    public void Move_ToSamePlace_ReturnsSameInstance()
    {
        var state = State(3);
        Assert.Same(state, PageOperations.Move(state, Ids(state, 1), 1));
        Assert.Same(state, PageOperations.Move(state, Ids(state, 1), 2));
    }

    [Fact]
    public void Shift_MovesUpAndDown_AndStopsAtEdges()
    {
        var state = State(4);
        Assert.Equal([0, 2, 1, 3], Order(PageOperations.Shift(state, Ids(state, 2), -1)));
        Assert.Same(state, PageOperations.Shift(state, Ids(state, 0), -1));
        Assert.Same(state, PageOperations.Shift(state, Ids(state, 3), +1));
    }

    [Fact]
    public void Shift_Down_MovesBlockOnePosition()
    {
        var state = State(4);
        Assert.Equal([0, 3, 1, 2], Order(PageOperations.Shift(state, Ids(state, 1, 2), +1)));
    }

    [Fact]
    public void Delete_RemovesSelectedPages()
    {
        var state = State(4);
        Assert.Equal([0, 3], Order(PageOperations.Delete(state, Ids(state, 1, 2))));
    }

    [Fact]
    public void Rotate_WrapsAroundBothWays()
    {
        var state = State(2);
        var ids = Ids(state, 0);
        var left = PageOperations.Rotate(state, ids, -1);
        Assert.Equal(3, left.Pages[0].Rotation);
        Assert.Equal(0, left.Pages[1].Rotation);
        Assert.Equal(0, PageOperations.Rotate(left, ids, 1).Pages[0].Rotation);
    }

    [Fact]
    public void SetFormValue_BackToOriginal_RemovesEntry()
    {
        var key = new FieldKey(1, "name");
        var changed = PageOperations.SetFormValue(DocumentState.Empty, key, "Ana", "");
        Assert.Equal("Ana", changed.FormValues[key]);
        Assert.Same(changed, PageOperations.SetFormValue(changed, key, "Ana", ""));
        Assert.Empty(PageOperations.SetFormValue(changed, key, "", "").FormValues);
    }

    [Fact]
    public void Overlays_AddUpdateRemove()
    {
        var state = State(1);
        var pageId = state.Pages[0].Id;
        var overlay = new TextOverlay(Guid.NewGuid(), TextKind.Flattened, 10, 20, 0, "Hi", 11, "#000000");
        var added = PageOperations.AddOverlay(state, pageId, overlay);
        var updated = PageOperations.UpdateOverlay(added, pageId, overlay with { Text = "Hello" });
        Assert.Equal("Hello", updated.Pages[0].Overlays.Single().Text);
        Assert.Same(updated, PageOperations.UpdateOverlay(updated, pageId, updated.Pages[0].Overlays[0]));
        Assert.Empty(PageOperations.RemoveOverlay(updated, pageId, overlay.Id).Pages[0].Overlays);
    }
}
