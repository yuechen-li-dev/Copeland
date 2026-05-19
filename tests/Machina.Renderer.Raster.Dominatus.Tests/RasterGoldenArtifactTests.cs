using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Styling;
using Machina.Renderer.Raster.Dominatus.Tests.Support;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Renderer.Raster.Dominatus.Tests;

public sealed class RasterGoldenArtifactTests
{
    [Fact]
    public void RectOnlyGolden_HasExpectedPpmSha()
    {
        var ui = UI.Rect(id: "panel", width: 20, height: 10, color: ColorToken.Hex(0xFF0000FF));
        var frame = RasterRenderTestPipeline.Render(ui, 20, 10);
        var ppm = frame.ToPpm();

        RasterArtifactAssertions.AssertPpmHeaderAndLength(ppm, 20, 10);
        RasterArtifactAssertions.MaybeWriteArtifact("rect-only", ppm);

        var sha = RasterArtifactAssertions.Sha256Hex(ppm);
        Assert.Equal("9d20349dee3efb17743b6cec1a06e566aa0fecb56283507da209cf4dc515fdf0", sha);
    }

    [Fact]
    public void TextOnlyGolden_HasExpectedPpmSha()
    {
        var ui = UI.Text("Hi", id: "hi", color: ColorToken.White, size: TextSize.Md);
        var frame = RasterRenderTestPipeline.Render(ui, 40, 20);
        var ppm = frame.ToPpm();

        RasterArtifactAssertions.AssertPpmHeaderAndLength(ppm, 40, 20);
        RasterArtifactAssertions.MaybeWriteArtifact("text-only", ppm);

        var sha = RasterArtifactAssertions.Sha256Hex(ppm);
        Assert.Equal("dd70893eb1c04136f9c856acb778a5151b2b2951d777382951dc0f29c9d02778", sha);
    }

    [Fact]
    public void StandardSampleGolden_HasExpectedPpmSha()
    {
        var ui = StandardUI.Card(
            id: "card",
            width: 80,
            height: 50,
            child: UI.Column(
                id: "content",
                gap: 4,
                children:
                [
                    UI.Text("Hi", id: "title", color: ColorToken.White),
                    StandardUI.Button("Go", id: "go", action: UiAction.Named("go")),
                ]));

        var frame = RasterRenderTestPipeline.Render(ui, 100, 80);
        var ppm = frame.ToPpm();

        RasterArtifactAssertions.AssertPpmHeaderAndLength(ppm, 100, 80);
        RasterArtifactAssertions.MaybeWriteArtifact("standard-card", ppm);

        var sha = RasterArtifactAssertions.Sha256Hex(ppm);
        Assert.Equal("8ba24949fc7ccfccd98ddbf450fee86f0877c6872dfd746a7ebbe8d834a4f1ef", sha);
    }

    [Fact]
    public void GoldenRendering_IsDeterministicAcrossRepeatedRuns()
    {
        var ui = UI.Rect(id: "panel", width: 20, height: 10, color: ColorToken.Hex(0xFF0000FF));

        var first = RasterRenderTestPipeline.Render(ui, 20, 10).ToPpm();
        var second = RasterRenderTestPipeline.Render(ui, 20, 10).ToPpm();

        Assert.Equal(first, second);
        Assert.Equal(RasterArtifactAssertions.Sha256Hex(first), RasterArtifactAssertions.Sha256Hex(second));
    }
}
