using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Core.Semantics;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class MachinaPresentationCacheTests
{
    [Fact]
    public void EqualReconstructedDeclarationsReusePreparationIncludingParagraphs()
    {
        static UiNode Build() => UI.Surface(children:
        [
            StandardUI.Place(StandardUI.Paragraph("Measured text", 200, id: "body"), 10, 10),
            StandardUI.Button("Go", 10, 80, 150, id: "go", action: UiAction.Named("go")),
        ]);
        var cache = new MachinaPresentationCache();
        var first = cache.Prepare(Build(), 400, 300);
        Assert.Same(first, cache.Prepare(Build(), 400, 300));
        Assert.Equal(1, cache.Preparations);
        Assert.NotSame(first, cache.Prepare(Build(), 500, 300));
    }

    [Fact]
    public void TextStyleSemanticsActionsAndMutableChildListsInvalidateWithoutAChangeKey()
    {
        var children = new List<UiNode> { StandardUI.Button("Go", 10, 20, 150, id: "go", action: UiAction.Named("first")) };
        UiNode screen = UI.Surface(children: children);
        var cache = new MachinaPresentationCache();
        var first = cache.Prepare(screen, 400, 300);
        children[0] = StandardUI.Button("New", 10, 20, 150, id: "go", action: UiAction.Named("second"));
        var second = cache.Prepare(screen, 400, 300);
        Assert.NotSame(first, second);
        Assert.Equal("second", second.HitTest.HitTest(new PointerPoint(50, 30))?.Action.Name);
        children[0] = StandardUI.Button("New", 10, 20, 150, id: "go", disabled: true, action: UiAction.Named("second"));
        var disabled = cache.Prepare(screen, 400, 300);
        Assert.NotSame(second, disabled);
        Assert.Null(disabled.HitTest.HitTest(new PointerPoint(50, 30)));
        children.Add(UI.Text("Added", color: ColorToken.White));
        Assert.NotSame(disabled, cache.Prepare(screen, 400, 300));
    }

    [Fact]
    public void GridTrackContentsAndDeclaredMetadataParticipateInEquality()
    {
        var columns = new List<GridTrack> { new FixedGridTrack(100) };
        UiNode grid = new GridNode(columns, [new FixedGridTrack(60)],
            [new UiGridCell(0, 0, UI.Text("Cell"))]) { Id = "grid" };
        UiNode screen = UI.Surface(children: [grid]);
        var cache = new MachinaPresentationCache();
        var first = cache.Prepare(screen, 400, 300);
        columns[0] = new FixedGridTrack(180);
        var wider = cache.Prepare(screen, 400, 300);
        Assert.NotSame(first, wider);
        Assert.Equal(180, wider.Resolved.Nodes[new NodeId("grid.cell-0-0")].Rect.Width);

        var control = StandardUI.Button("Same", 0, 0, 120, id: "button", action: UiAction.Named("same"));
        var changed = control with { Semantics = new UiSemantics(UiRole.Container, "Changed metadata") };
        first = cache.Prepare(UI.Surface(children: [control]), 400, 300);
        Assert.NotSame(first, cache.Prepare(UI.Surface(children: [changed]), 400, 300));
    }

    [Fact]
    public void PaintChangesAndOpaquePayloadsNeverReturnStalePreparation()
    {
        var cache = new MachinaPresentationCache();
        var first = cache.Prepare(UI.Rect(color: ColorToken.White), 200, 100);
        Assert.NotSame(first, cache.Prepare(UI.Rect(color: ColorToken.Hex(0x112233FF)), 200, 100));
        var opaque = new RichTextNode(new object());
        first = cache.Prepare(opaque, 200, 100);
        Assert.NotSame(first, cache.Prepare(opaque, 200, 100));
    }

    [Fact]
    public void FailedPreparationDoesNotPublishAnInvalidCacheEntry()
    {
        var cache = new MachinaPresentationCache();
        UiNode screen = UI.Text("Valid");
        var first = cache.Prepare(screen, 200, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Prepare(screen, 0, 100));
        Assert.Same(first, cache.Prepare(screen, 200, 100));
        Assert.Equal(1, cache.Preparations);
    }
}
