using System.Security.Cryptography;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Resolved2D;
using Aurelian.Rendering.Raster;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Presentation;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus;
using Machina.Renderer.Raster.Text;
using Machina.Standard.Authoring;
using Xunit;
using MachinaFill = Machina.Presentation.FillRectangleOperation;
using MachinaPop = Machina.Presentation.PopClipOperation;
using MachinaPush = Machina.Presentation.PushRectangularClipOperation;
using MachinaStroke = Machina.Presentation.StrokeRectangleOperation;
using MachinaText = Machina.Presentation.PositionedTextOperation;

namespace Aurelian.Machina.Tests;

public sealed class AurelianCpuRasterParityTests
{
    [Fact]
    public void TransparentEmptyFrame_HasExactLegacyParity()
    {
        AssertParity(new MachinaPresentationFrame(new MachinaPresentationViewport(3, 2), []));
    }

    [Fact]
    public void OpaqueFill_HasExactLegacyParity()
    {
        AssertParity(Frame(
            4,
            3,
            new MachinaFill("fill", new Rect(0.2, 0.2, 2.1, 1.1), ColorToken.Hex(0xAABBCCFF))));
    }

    [Fact]
    public void ClippedFill_HasExactLegacyParity()
    {
        AssertParity(Frame(
            5,
            5,
            new MachinaPush("clip", new Rect(1, 1, 2, 2)),
            new MachinaFill("fill", new Rect(0, 0, 5, 5), ColorToken.Hex(0xFF0000FF)),
            new MachinaPop()));
    }

    [Fact]
    public void NestedClips_HaveExactLegacyParity()
    {
        AssertParity(Frame(
            6,
            6,
            new MachinaPush("clip-1", new Rect(1, 1, 4, 4)),
            new MachinaPush("clip-2", new Rect(3, 0, 4, 4)),
            new MachinaFill("fill", new Rect(0, 0, 6, 6), ColorToken.White),
            new MachinaPop(),
            new MachinaPop()));
    }

    [Fact]
    public void AlphaBlending_HasExactLegacyParity()
    {
        AssertParity(Frame(
            1,
            1,
            new MachinaFill("background", new Rect(0, 0, 1, 1), ColorToken.Hex(0x000000FF)),
            new MachinaFill("foreground", new Rect(0, 0, 1, 1), ColorToken.Hex(0xFF000080))));
    }

    [Fact]
    public void InsideStroke_HasExactLegacyParity()
    {
        AssertParity(Frame(
            6,
            6,
            new MachinaStroke("stroke", new Rect(1, 1, 4, 4), ColorToken.Hex(0x00FF00FF), 1.5)));
    }

    [Fact]
    public void OrderedOverlappingOperations_HaveExactLegacyParity()
    {
        AssertParity(Frame(
            3,
            3,
            new MachinaFill("blue", new Rect(0, 0, 3, 3), ColorToken.Hex(0x0000FFFF)),
            new MachinaFill("red", new Rect(1, 1, 2, 2), ColorToken.Hex(0xFF000080))));
    }

    [Fact]
    public void PrimitivePositionedText_HasExactLegacyParity()
    {
        AssertParity(Frame(
            30,
            20,
            new MachinaText(
                "text",
                new Rect(0, 0, 25, 21),
                "Hi",
                new TextStyle(Size: TextSize.Sm, AlignX: TextAlignX.Right, AlignY: TextAlignY.Bottom),
                ColorToken.White)));
    }

    [Fact]
    public void RichTextPositionOutputReducedToTextOperations_HasExactLegacyParity()
    {
        AssertParity(Frame(
            80,
            30,
            new MachinaText("article.b0.l0.r0", new Rect(1, 1, 30, 14), "Title", new TextStyle(Color: ColorToken.White, Size: TextSize.H1), ColorToken.White),
            new MachinaText("article.b1.l0.r0", new Rect(1, 17, 30, 10), "body", new TextStyle(Color: ColorToken.Hex(0xAABBCCFF), Size: TextSize.Sm), ColorToken.Hex(0xAABBCCFF))));
    }

    [Fact]
    public void MixedFillStrokeTextAndClipFrame_HasExactLegacyParity()
    {
        AssertParity(Frame(
            40,
            25,
            new MachinaFill("background", new Rect(0, 0, 40, 25), ColorToken.Hex(0x102030FF)),
            new MachinaStroke("border", new Rect(1, 1, 38, 23), ColorToken.Hex(0xFFFFFFFF), 1),
            new MachinaPush("content-clip", new Rect(3, 3, 20, 15)),
            new MachinaText("label", new Rect(3, 3, 30, 20), "Hello", new TextStyle(Color: ColorToken.Hex(0xFFCC00FF), Size: TextSize.Md), ColorToken.Hex(0xFFCC00FF)),
            new MachinaPop()));
    }

    [Fact]
    public void CanonicalGoldenPath_AuthoredStandardDocument_TranslatesAndRendersWithExactLegacyParity()
    {
        var ui = StandardUI.Card(
            id: "card",
            width: 120,
            height: 80,
            child: UI.Column(
                id: "content",
                gap: 4,
                children:
                [
                    UI.Text("Hi", id: "title", color: ColorToken.White),
                    StandardUI.Button("Go", id: "go", action: UiAction.Named("go"))
                ]));

        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 140, 120));
        MachinaPresentationFrame frame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(140, 120));

        Assert.NotEmpty(frame.Operations);
        Assert.Equal(frame.Operations.Count, MachinaPresentationTranslator.Translate(frame).Operations.Count);
        AssertParity(frame);
    }

    private static MachinaPresentationFrame Frame(int width, int height, params MachinaPresentationOperation[] operations)
    {
        return new MachinaPresentationFrame(new MachinaPresentationViewport(width, height), operations);
    }

    private static void AssertParity(MachinaPresentationFrame legacyInput)
    {
        global::Machina.Renderer.Raster.Dominatus.Models.RasterFrame legacyFrame = RenderLegacy(legacyInput);
        RasterFrame aurelianFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(legacyInput));

        Assert.Equal(legacyFrame.Width, aurelianFrame.Surface.Width);
        Assert.Equal(legacyFrame.Height, aurelianFrame.Surface.Height);

        for (var y = 0; y < legacyFrame.Height; y++)
        {
            for (var x = 0; x < legacyFrame.Width; x++)
            {
                Rgba32 legacyPixel = legacyFrame.Surface.GetPixel(x, y);
                var expected = new Resolved2DRgbaColor(legacyPixel.R, legacyPixel.G, legacyPixel.B, legacyPixel.A);
                Resolved2DRgbaColor actual = aurelianFrame.Surface.GetPixel(x, y);
                Assert.True(expected == actual, $"Pixel ({x}, {y}) differs. Legacy={expected}; Aurelian={actual}.");
            }
        }

        byte[] legacyPpm = legacyFrame.ToPpm();
        byte[] aurelianPpm = RasterPpmEncoder.EncodeP6(aurelianFrame.Surface);
        Assert.Equal(legacyPpm, aurelianPpm);
        Assert.Equal(SHA256.HashData(legacyPpm), SHA256.HashData(aurelianPpm));
    }

    private static global::Machina.Renderer.Raster.Dominatus.Models.RasterFrame RenderLegacy(MachinaPresentationFrame frame)
    {
        var recorder = new RasterRenderRecorder();
        var textRasterizer = new DebugBitmapTextRasterizer();
        recorder.BeginFrame(frame.Viewport.Width, frame.Viewport.Height);

        foreach (MachinaPresentationOperation operation in frame.Operations)
        {
            switch (operation)
            {
                case MachinaFill fill:
                    recorder.FillRect(fill.SourceId, fill.Rect, fill.Color);
                    break;
                case MachinaStroke stroke:
                    recorder.StrokeRect(stroke.SourceId, stroke.Rect, stroke.Color, stroke.Thickness);
                    break;
                case MachinaText text:
                    recorder.DrawText(text.SourceId, text.Rect, text.Text, text.Style, textRasterizer);
                    break;
                case MachinaPush push:
                    recorder.PushClip(push.SourceId, push.Rect);
                    break;
                case MachinaPop:
                    recorder.PopClip();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported legacy operation '{operation.GetType().FullName}'.");
            }
        }

        recorder.EndFrame();
        return Assert.Single(recorder.CompletedFrames);
    }

}
