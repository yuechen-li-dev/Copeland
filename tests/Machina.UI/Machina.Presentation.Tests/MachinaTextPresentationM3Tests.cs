using Machina.Core.Styling;
using Machina.Fonts;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Geometry;
using Xunit;

namespace Machina.Presentation.Tests;

public sealed class MachinaTextPresentationM3Tests
{
    [Fact]
    public void ExistingTextDefaultsToRasterPixel()
    {
        var operation = new PositionedTextOperation(
            "label",
            new Rect(0, 0, 80, 20),
            "Inventory",
            new TextStyle(),
            ColorToken.White);

        Assert.Equal(MachinaTextRenderingMode.RasterPixel, operation.RenderingMode);
        Assert.Null(operation.Primitive);
    }

    [Fact]
    public void SameGlyphRunCanSelectEitherRealizationWithoutChangingGeometry()
    {
        MachinaGlyphRun run = CreateRun("Hello Machina");
        var atlas = new MachinaFontAtlasId("crimson-32-sha256-fixture");
        var raster = new MachinaTextPresentationPrimitive(run, atlas, MachinaTextRenderingMode.RasterPixel);
        var msdf = new MachinaTextPresentationPrimitive(run, atlas, MachinaTextRenderingMode.Msdf);

        Assert.Same(raster.GlyphRun, msdf.GlyphRun);
        Assert.Equal(raster.GlyphRun.Glyphs, msdf.GlyphRun.Glyphs);
        Assert.Equal(atlas, raster.AtlasIdentity);
        Assert.Equal(atlas, msdf.AtlasIdentity);
    }

    [Fact]
    public void PresentationModePatchPreservesViewportOrderingRectsAndText()
    {
        var text = new PositionedTextOperation(
            "heading",
            new Rect(12, 8, 200, 64),
            "Hello Machina",
            new TextStyle(Size: TextSize.H1),
            ColorToken.White);
        var frame = new MachinaPresentationFrame(
            new MachinaPresentationViewport(320, 180),
            [new FillRectangleOperation("panel", new Rect(0, 0, 320, 180), ColorToken.Hex(0x000000FF)), text]);
        MachinaGlyphRun run = CreateRun(text.Text);

        MachinaPresentationFrame updated = MachinaTextPresentationFrame.Apply(
            frame,
            new Dictionary<string, MachinaTextPresentationPrimitive>
            {
                [text.SourceId] = new(run, new MachinaFontAtlasId("fixture"), MachinaTextRenderingMode.Msdf),
            });

        Assert.Equal(frame.Viewport, updated.Viewport);
        Assert.IsType<FillRectangleOperation>(updated.Operations[0]);
        var updatedText = Assert.IsType<PositionedTextOperation>(updated.Operations[1]);
        Assert.Equal(text.Rect, updatedText.Rect);
        Assert.Equal(text.Text, updatedText.Text);
        Assert.Equal(MachinaTextRenderingMode.Msdf, updatedText.RenderingMode);
        Assert.Same(run, updatedText.Primitive!.GlyphRun);
    }

    [Fact]
    public void PrimitiveRejectsGlyphRunForDifferentSemanticText()
    {
        MachinaGlyphRun run = CreateRun("Settings");
        var primitive = new MachinaTextPresentationPrimitive(
            run,
            new MachinaFontAtlasId("fixture"),
            MachinaTextRenderingMode.Msdf);

        Assert.Throws<ArgumentException>(() => new PositionedTextOperation(
            "inventory",
            new Rect(0, 0, 100, 20),
            "Inventory",
            new TextStyle(),
            ColorToken.White,
            primitive));
    }

    private static MachinaGlyphRun CreateRun(string text)
    {
        FontFaceId face = new("fixture");
        MachinaGlyphPlacement[] glyphs = text.Select((character, index) => new MachinaGlyphPlacement(
            GlyphKey.FromChar(face, character, 32),
            GlyphId: null,
            new MachinaTextSpan(index, 1),
            OriginX: index * 12,
            BaselineY: 32,
            Advance: 12,
            new MachinaPlaneBounds(0, -24, 10, 2),
            TokenId: 0,
            IsWhitespace: char.IsWhiteSpace(character))).ToArray();
        return new MachinaGlyphRun(text, [], [], glyphs);
    }
}
