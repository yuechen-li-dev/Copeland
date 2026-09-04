using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Core.Tests;

public sealed class UiStackAuthoringM17bTests
{
    [Fact]
    public void UIStack_CreatesVerticalStack()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("Title")),
            ]));

        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("stack")).Arrange);
        Assert.Equal(StackAxis.Vertical, arrange.Axis);
    }

    [Fact]
    public void UIStack_CreatesHorizontalStack()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Horizontal,
            children:
            [
                UI.StackItem.Fixed(main: 80, child: UI.Text("Title")),
            ]));

        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("stack")).Arrange);
        Assert.Equal(StackAxis.Horizontal, arrange.Axis);
    }

    [Fact]
    public void UIStack_SupportsFixedItems()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("Title", id: "title")),
            ]));

        var wrapper = Assert.Single(result.Rows, row => row.Id == new NodeId("stack.item-0"));
        var frame = Assert.IsType<FixedFrame>(wrapper.Frame);
        Assert.Equal(24, frame.Height);

        var title = Assert.Single(result.Rows, row => row.Id == new NodeId("title"));
        Assert.Equal(wrapper.Id, title.Parent);
    }

    [Fact]
    public void UIStack_SupportsFillItems()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fill(weight: 2, child: UI.Rect(id: "body")),
            ]));

        var wrapper = Assert.Single(result.Rows, row => row.Id == new NodeId("stack.item-0"));
        var frame = Assert.IsType<FillFrame>(wrapper.Frame);
        Assert.Equal(2, frame.Weight);
        Assert.True(frame.CrossFill);
    }

    [Fact]
    public void UIStack_SupportsGap()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            gap: 6,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("A")),
            ]));

        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("stack")).Arrange);
        Assert.Equal(6, arrange.Gap);
    }

    [Fact]
    public void UIStack_SupportsPadding()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            padding: UiPadding.All(12),
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("A")),
            ]));

        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("stack")).Arrange);
        Assert.Equal(new EdgeInsets(12, 12, 12, 12), arrange.Padding);
    }

    [Fact]
    public void UIStack_ExposesJsJustifyAndAlignVocabulary()
    {
        var result = UiLowerer.Lower(UI.VStack(
            id: "stack",
            justify: StackJustify.SpaceBetween,
            align: StackAlign.Center,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("A")),
                UI.StackItem.Fixed(main: 24, child: UI.Text("B")),
            ]));

        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("stack")).Arrange);
        Assert.Equal(StackJustify.SpaceBetween, arrange.Justify);
        Assert.Equal(StackAlign.Center, arrange.Align);
    }

    [Fact]
    public void UIStack_OffersConciseFixedFillAndSpaceAuthoring()
    {
        var result = UiLowerer.Lower(UI.VStack(
            id: "stack",
            children:
            [
                UI.Fixed(24, UI.Text("Title", id: "title")),
                UI.Fill(UI.Rect(id: "body"), weight: 2),
                UI.Space(),
            ]));

        var fixedFrame = Assert.IsType<FixedFrame>(
            Assert.Single(result.Rows, row => row.Id == new NodeId("stack.item-0")).Frame);
        var fillFrame = Assert.IsType<FillFrame>(
            Assert.Single(result.Rows, row => row.Id == new NodeId("stack.item-1")).Frame);
        var spaceFrame = Assert.IsType<FillFrame>(
            Assert.Single(result.Rows, row => row.Id == new NodeId("stack.item-2")).Frame);

        Assert.Equal(24, fixedFrame.Height);
        Assert.Equal(2, fillFrame.Weight);
        Assert.Equal(1, spaceFrame.Weight);
    }

    [Fact]
    public void UIStack_DerivesWrapperIdsDeterministically()
    {
        var first = Snapshot(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("A", id: "title")),
                UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "body")),
            ]));

        var second = Snapshot(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("A", id: "title")),
                UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "body")),
            ]));

        Assert.Equal(first, second);
        Assert.Contains("stack.item-0", first);
        Assert.Contains("stack.item-1", first);
    }

    [Fact]
    public void UIStack_PreservesChildOrder()
    {
        var resolved = Resolve(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 20, child: UI.Text("First")),
                UI.StackItem.Fill(weight: 1, child: UI.Rect()),
                UI.StackItem.Fixed(main: 10, child: UI.Text("Third")),
            ]));

        Assert.Equal(
            new NodeId[] { "stack.item-0", "stack.item-1", "stack.item-2" },
            resolved.Children[new NodeId("stack")].ToArray());
    }

    [Fact]
    public void UIStack_DoesNotRequireSlotIdsFromAuthor()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("Title", id: "title")),
            ]));

        Assert.DoesNotContain(result.Rows, row => row.Id.Value.EndsWith(".slot", StringComparison.Ordinal));
        Assert.Contains(result.Rows, row => row.Id == new NodeId("stack.item-0"));
        Assert.Contains(result.Rows, row => row.Id == new NodeId("title"));
    }

    [Fact]
    public void UIStack_VerticalFixedItemsResolveExpectedRects()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Vertical,
                children:
                [
                    UI.StackItem.Fixed(main: 24, child: UI.Rect(width: 80, id: "a")),
                    UI.StackItem.Fixed(main: 18, child: UI.Rect(width: 60, id: "b")),
                ]),
            new Rect(0, 0, 200, 100));

        AssertRect(resolved.Nodes[new NodeId("stack.item-0")].Rect, 0, 0, 80, 24);
        AssertRect(resolved.Nodes[new NodeId("stack.item-1")].Rect, 0, 24, 60, 18);
    }

    [Fact]
    public void UIStack_VerticalFillItemGetsRemainingHeight()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Vertical,
                gap: 5,
                children:
                [
                    UI.StackItem.Fixed(main: 20, child: UI.Rect(width: 80)),
                    UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "fill")),
                    UI.StackItem.Fixed(main: 10, child: UI.Rect(width: 60)),
                ]),
            new Rect(0, 0, 200, 100));

        AssertRect(resolved.Nodes[new NodeId("stack.item-1")].Rect, 0, 25, 200, 60);
    }

    [Fact]
    public void UIStack_HorizontalFixedItemsResolveExpectedRects()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Horizontal,
                children:
                [
                    UI.StackItem.Fixed(main: 40, child: UI.Rect(height: 20)),
                    UI.StackItem.Fixed(main: 30, child: UI.Rect(height: 10)),
                ]),
            new Rect(0, 0, 120, 50));

        AssertRect(resolved.Nodes[new NodeId("stack.item-0")].Rect, 0, 0, 40, 20);
        AssertRect(resolved.Nodes[new NodeId("stack.item-1")].Rect, 40, 0, 30, 10);
    }

    [Fact]
    public void UIStack_HorizontalFillItemGetsRemainingWidth()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Horizontal,
                gap: 5,
                children:
                [
                    UI.StackItem.Fixed(main: 20, child: UI.Rect(height: 20)),
                    UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "fill")),
                    UI.StackItem.Fixed(main: 10, child: UI.Rect(height: 10)),
                ]),
            new Rect(0, 0, 100, 40));

        AssertRect(resolved.Nodes[new NodeId("stack.item-1")].Rect, 25, 0, 60, 40);
    }

    [Fact]
    public void UIStack_GapAffectsChildPositions()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Vertical,
                gap: 7,
                children:
                [
                    UI.StackItem.Fixed(main: 20, child: UI.Rect(width: 20)),
                    UI.StackItem.Fixed(main: 10, child: UI.Rect(width: 20)),
                ]),
            new Rect(0, 0, 80, 80));

        Assert.Equal(27, resolved.Nodes[new NodeId("stack.item-1")].Rect.Y);
    }

    [Fact]
    public void UIStack_PaddingAffectsChildPositions()
    {
        var resolved = Resolve(
            UI.Stack(
                id: "stack",
                axis: StackAxis.Vertical,
                padding: UiPadding.All(12),
                children:
                [
                    UI.StackItem.Fixed(main: 20, child: UI.Rect(width: 20)),
                ]),
            new Rect(0, 0, 80, 80));

        AssertRect(resolved.Nodes[new NodeId("stack.item-0")].Rect, 12, 12, 20, 20);
    }

    [Fact]
    public void UIRow_ExistingBehaviorStillWorks()
    {
        var result = UiLowerer.Lower(UI.Row(
            id: "row",
            children:
            [
                UI.Text("A", id: "a"),
                UI.Text("B", id: "b"),
            ]));

        Assert.DoesNotContain(result.Rows, row => row.Id.Value.Contains(".item-", StringComparison.Ordinal));
        Assert.Equal(new NodeId("row"), result.Rows.Single(row => row.Id == new NodeId("a")).Parent);
        Assert.Equal(new NodeId("row"), result.Rows.Single(row => row.Id == new NodeId("b")).Parent);
    }

    [Fact]
    public void UIColumn_ExistingBehaviorStillWorks()
    {
        var result = UiLowerer.Lower(UI.Column(
            id: "column",
            children:
            [
                UI.Text("A", id: "a"),
                UI.Text("B", id: "b"),
            ]));

        Assert.DoesNotContain(result.Rows, row => row.Id.Value.Contains(".item-", StringComparison.Ordinal));
        var arrange = Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("column")).Arrange);
        Assert.Equal(StackAxis.Vertical, arrange.Axis);
    }

    [Fact]
    public void UIStack_DoesNotChangeAnchorBehavior()
    {
        var result = UiLowerer.Lower(UI.Surface(
            id: "surface",
            children:
            [
                UI.Anchor(
                    id: "panel-anchor",
                    left: 72,
                    top: 24,
                    width: 500,
                    height: 292,
                    child: UI.Rect(id: "panel")),
            ]));

        var anchor = Assert.Single(result.Rows, row => row.Id == new NodeId("panel-anchor"));
        var frame = Assert.IsType<AnchorFrame>(anchor.Frame);
        Assert.Equal(UiLength.Px(72), frame.Left);
        Assert.Equal(UiLength.Px(24), frame.Top);
        Assert.Equal(UiLength.Px(500), frame.Width);
        Assert.Equal(UiLength.Px(292), frame.Height);
    }

    private static ResolvedLayoutDocument Resolve(
        Machina.Core.Nodes.UiNode root,
        Rect? rootRect = null)
    {
        var result = UiLowerer.Lower(root);
        var document = LayoutCompiler.CompileLayoutRows(result.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, rootRect ?? new Rect(0, 0, 200, 100));
    }

    private static string Snapshot(Machina.Core.Nodes.UiNode root)
    {
        return string.Join(
            "\n",
            UiLowerer.Lower(root)
                .Rows
                .Select(row => $"{row.Id.Value}:{row.Parent?.Value ?? "<root>"}:{row.Order}:{row.DebugLabel}"));
    }

    private static void AssertRect(Rect actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }
}
