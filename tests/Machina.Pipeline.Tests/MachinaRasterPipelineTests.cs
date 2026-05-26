using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class MachinaRasterPipelineTests
{
    [Fact]
    public void Render_TextUi_ProducesFrame()
    {
        var pipeline = new MachinaRasterPipeline();

        var frame = pipeline.Render(
            UI.Text("Hello", id: "hello", color: ColorToken.White),
            width: 80,
            height: 40);

        Assert.NotNull(frame.Lowering);
        Assert.NotNull(frame.Document);
        Assert.NotNull(frame.Resolved);
        Assert.NotNull(frame.HitTest);
        Assert.Equal(80, frame.RasterFrame.Width);
        Assert.Equal(40, frame.RasterFrame.Height);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);
    }

    [Fact]
    public void Render_StandardButton_ProducesHitTestAction()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button(
            "Increment",
            id: "increment",
            action: UiAction.Named("increment"));

        var frame = pipeline.Render(ui, width: 200, height: 100);
        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("increment")].Rect;
        var point = new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0)));

        var hit = frame.HitTest.HitTest(point);

        Assert.NotNull(hit);
        Assert.Equal("increment", hit!.Action.Name);
    }

    [Fact]
    public void Render_StandardCard_ProducesCommandsPixelsAndPpm()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Card(
            id: "card",
            width: 120,
            height: 80,
            children:
            [
                UI.Text("Card", id: "title", color: ColorToken.White),
                StandardUI.Button("Go", id: "go", action: UiAction.Named("go")),
            ]);

        var frame = pipeline.Render(ui, width: 160, height: 120);

        Assert.IsType<BeginFrameCommand>(frame.RenderCommands[0]);
        Assert.IsType<EndFrameCommand>(frame.RenderCommands[^1]);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);

        var ppm = frame.RasterFrame.ToPpm();
        Assert.NotNull(ppm);
        Assert.NotEmpty(ppm);
    }

    [Fact]
    public void Render_InvalidDimensions_Throws()
    {
        var pipeline = new MachinaRasterPipeline();

        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Render(UI.Text("X"), width: 0, height: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Render(UI.Text("X"), width: 10, height: 0));
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button("Deterministic", id: "det", action: UiAction.Named("det"));

        var first = pipeline.Render(ui, width: 200, height: 100);
        var second = pipeline.Render(ui, width: 200, height: 100);

        Assert.Equal(first.RasterFrame.ToPpm(), second.RasterFrame.ToPpm());

        Assert.Equal(first.RenderCommands.Count, second.RenderCommands.Count);
        for (var i = 0; i < first.RenderCommands.Count; i++)
        {
            Assert.Equal(first.RenderCommands[i].GetType(), second.RenderCommands[i].GetType());
        }
    }

    [Fact]
    public void Render_DisabledButton_HasNoHitTestAction()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button(
            "Disabled",
            id: "disabled",
            action: UiAction.Named("disabled"),
            disabled: true);

        var frame = pipeline.Render(ui, width: 200, height: 100);
        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("disabled")].Rect;
        var point = new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0)));

        var hit = frame.HitTest.HitTest(point);

        Assert.Null(hit);
    }


    [Fact]
    public void Pipeline_RendersFlatUiDocument()
    {
        var pipeline = new MachinaRasterPipeline();
        var document = UiDocument.Create(
            [
                Row.Root("root", View.Rect(background: ColorToken.Hex(0x202020FF))),
                Row.Anchor("button", "root", left: 20, top: 20, width: 120, height: 32, view: Machina.Standard.Authoring.StandardView.Button("Go", UiAction.Named("go")))
            ]);

        var frame = pipeline.Render(document, width: 220, height: 120);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);

        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("button")].Rect;
        var point = new PointerPoint((float)(rect.X + rect.Width / 2.0), (float)(rect.Y + rect.Height / 2.0));
        var hit = frame.HitTest.HitTest(point);
        Assert.NotNull(hit);
        Assert.Equal("go", hit!.Action.Name);
        Assert.Equal(document.Rows.Count, frame.Lowering.Rows.Count);
    }
    private static int CountNonTransparentPixels(Machina.Renderer.Raster.Dominatus.Models.RasterFrame frame)
    {
        var count = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.Surface.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
