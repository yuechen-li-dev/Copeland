using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Text;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class StandardPositionedAuthoringTests
{
    [Theory]
    [InlineData(100, 80)]
    [InlineData(350, 240)]
    public void MovingNestedFrameMovesPaintAndHitTargetsWithoutChangingIds(double x, double y)
    {
        UiNode screen = UI.Surface(children:
        [
            StandardUI.Frame(x, y, 240, 160, id: "frame", children:
            [
                StandardUI.Panel(10, 12, 220, 136, id: "panel", children:
                [
                    StandardUI.Label("Title", 8, 6, 200, id: "title"),
                    StandardUI.Button("Go", 8, 50, 180, 42, id: "go", action: UiAction.Named("go")),
                ]),
            ]),
        ]);
        var prepared = new MachinaPresentationPipeline().Prepare(screen, 800, 600);
        var button = prepared.Resolved.Nodes[new NodeId("go")].Rect;

        Assert.Equal(new Rect(x + 18, y + 62, 180, 42), button);
        var text = Assert.Single(prepared.PresentationFrame.Operations.OfType<PositionedTextOperation>(),
            operation => operation.SourceId == "title");
        Assert.Equal(x + 18, text.Rect.X);
        Assert.Equal(y + 18, text.Rect.Y);
        Assert.Equal("go", prepared.HitTest.HitTest(new PointerPoint(button.X + 90, button.Y + 21))?.Action.Name);
        Assert.Null(prepared.HitTest.HitTest(new PointerPoint(x - 1, y - 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositionedButtonRetainsSmallLabelStyleAndDisabledBehavior(bool disabled)
    {
        var screen = UI.Surface(children:
        [
            StandardUI.Button("Go", 30, 40, 200, 60, id: "go", action: UiAction.Named("go"),
                size: ButtonSize.Small, disabled: disabled),
        ]);
        var prepared = new MachinaPresentationPipeline().Prepare(screen, 300, 200);
        Assert.Equal(new Rect(30, 40, 200, 60), prepared.Resolved.Nodes[new NodeId("go")].Rect);
        Assert.Equal(TextSize.Sm, prepared.Lowering.TextStyles[new NodeId("go.label")].Size);
        Assert.Equal(disabled, prepared.Lowering.Semantics[new NodeId("go")].Disabled);
        var hit = prepared.HitTest.HitTest(new PointerPoint(130, 70));
        if (disabled)
        {
            Assert.Null(hit);
        }
        else
        {
            Assert.Equal("go", hit?.Action.Name);
        }
    }

    [Fact]
    public void MeasuredParagraphWrapsAtItsWidthAndFitsInAStack()
    {
        const string text = "Measured text wraps into more rows when its available width shrinks.";
        RectNode narrow = StandardUI.Paragraph(text, 140, id: "narrow");
        RectNode wide = StandardUI.Paragraph(text, 400, id: "wide");
        Assert.True(narrow.Height > wide.Height);

        var screen = UI.Column([narrow, wide], gap: 8);
        var prepared = new MachinaPresentationPipeline().Prepare(screen, 500, 800);
        Rect narrowRect = prepared.Resolved.Nodes[new NodeId("narrow")].Rect;
        Rect wideRect = prepared.Resolved.Nodes[new NodeId("wide")].Rect;
        Assert.Equal(narrow.Height!.Value + 8, wideRect.Y);
        var operations = prepared.PresentationFrame.Operations.OfType<PositionedTextOperation>()
            .Where(operation => operation.SourceId.StartsWith("narrow.content.", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(operations);
        foreach (var operation in operations)
        {
            Assert.True(operation.Rect.X >= narrowRect.X);
            Assert.True(operation.Rect.X + operation.Rect.Width <= narrowRect.X + narrowRect.Width + 0.001);
            Assert.True(operation.Rect.Y + operation.Rect.Height <= narrowRect.Y + narrowRect.Height + 0.001);
        }
    }

    [Fact]
    public void PlacingMeasuredParagraphKeepsItsDeclaredHeight()
    {
        RectNode paragraph = StandardUI.Paragraph("First line\nSecond line", 200, id: "body");
        var screen = UI.Surface(children: [StandardUI.Place(paragraph, 20, 35)]);
        var prepared = new MachinaPresentationPipeline().Prepare(screen, 400, 300);
        Assert.Equal(new Rect(20, 35, 200, paragraph.Height!.Value),
            prepared.Resolved.Nodes[new NodeId("body")].Rect);
    }
}
