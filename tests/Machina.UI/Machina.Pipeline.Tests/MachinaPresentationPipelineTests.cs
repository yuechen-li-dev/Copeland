using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class MachinaPresentationPipelineTests
{
    [Fact]
    public void Prepare_TextUi_ProducesPresentationWithoutPixels()
    {
        var pipeline = new MachinaPresentationPipeline();

        MachinaPreparedPresentation prepared = pipeline.Prepare(
            UI.Text("Hello", id: "hello", color: ColorToken.White),
            width: 80,
            height: 40);

        Assert.Equal(80, prepared.PresentationFrame.Viewport.Width);
        Assert.Equal(40, prepared.PresentationFrame.Viewport.Height);
        Assert.Contains(prepared.PresentationFrame.Operations, operation =>
            operation is PositionedTextOperation text && text.SourceId == "hello");
    }

    [Fact]
    public void Prepare_StandardButton_ProducesHitTestAction()
    {
        UiNode ui = StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"));
        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(ui, 200, 100);
        var rect = prepared.Resolved.Nodes[new NodeId("increment")].Rect;

        UiHitTestResult? hit = prepared.HitTest.HitTest(
            new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0))));

        Assert.NotNull(hit);
        Assert.Equal("increment", hit!.Action.Name);
    }

    [Fact]
    public void Prepare_StandardCard_PreservesOrderedPresentationSemantics()
    {
        UiNode ui = StandardUI.Card(
            id: "card",
            width: 120,
            height: 80,
            children:
            [
                UI.Text("Card", id: "title", color: ColorToken.White),
                StandardUI.Button("Go", id: "go", action: UiAction.Named("go")),
            ]);

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(ui, 160, 120);

        Assert.NotEmpty(prepared.PresentationFrame.Operations);
        Assert.Contains(prepared.PresentationFrame.Operations, operation => operation is FillRectangleOperation);
        Assert.Contains(prepared.PresentationFrame.Operations, operation => operation is PositionedTextOperation);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void Prepare_InvalidDimensions_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MachinaPresentationPipeline().Prepare(UI.Text("X"), width, height));
    }

    [Fact]
    public void Prepare_IsDeterministic()
    {
        UiNode ui = StandardUI.Button("Deterministic", id: "det", action: UiAction.Named("det"));
        var pipeline = new MachinaPresentationPipeline();

        MachinaPreparedPresentation first = pipeline.Prepare(ui, 200, 100);
        MachinaPreparedPresentation second = pipeline.Prepare(ui, 200, 100);

        Assert.Equal(first.PresentationFrame.Viewport, second.PresentationFrame.Viewport);
        Assert.Equal(first.PresentationFrame.Operations, second.PresentationFrame.Operations);
    }

    [Fact]
    public void Prepare_FlatDocument_PreservesLoweringAndHitTesting()
    {
        UiDocument document = UiDocument.Create(
        [
            Row.Root("root", View.Rect(background: ColorToken.Hex(0x202020FF))),
            Row.Anchor(
                "button",
                "root",
                left: 20,
                top: 20,
                width: 120,
                height: 32,
                view: StandardView.Button("Go", UiAction.Named("go")))
        ]);

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(document, 220, 120);
        var rect = prepared.Resolved.Nodes[new NodeId("button")].Rect;
        UiHitTestResult? hit = prepared.HitTest.HitTest(
            new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0))));

        Assert.Equal(document.Rows.Count, prepared.Lowering.Rows.Count);
        Assert.NotNull(hit);
        Assert.Equal("go", hit!.Action.Name);
    }
}
