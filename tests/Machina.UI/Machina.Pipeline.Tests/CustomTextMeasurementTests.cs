using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class CustomTextMeasurementTests
{
    [Fact]
    public void Pipeline_retains_outline_font_dimensions_in_anchored_text()
    {
        var ui = UI.Surface(width: 200, height: 100, children:
        [
            UI.Anchor(UI.Text("Caption", id: "caption"), left: 10, top: 10, width: 180, height: 60)
        ]);
        var pipeline = new MachinaPresentationPipeline();
        var ordinary = pipeline.Prepare(ui, 200, 100);
        var custom = pipeline.Prepare(ui, 200, 100, new UiLoweringOptions(new OutlineMetrics()));
        var ordinaryRect = ordinary.Resolved.Nodes[new NodeId("caption")].Rect;
        var customRect = custom.Resolved.Nodes[new NodeId("caption")].Rect;
        Assert.True(customRect.Height >= 38);
        Assert.NotEqual(ordinaryRect.Height, customRect.Height);
        Assert.Equal(ordinaryRect.X, customRect.X);
        Assert.Equal(ordinaryRect.Y, customRect.Y);
    }

    private sealed class OutlineMetrics : ITextMeasurer
    {
        public IntrinsicSize MeasureText(string text, TextStyle style) => new(100, 38);
    }
}
