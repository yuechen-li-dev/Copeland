using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DirectOutlineStaticTextRendererTests
{
    private static readonly Rgba32 Background = new(16, 16, 24, 255);
    private static readonly Rgba32 Foreground = new(240, 240, 240, 255);

    [Fact]
    public void TextRenderStrategy_DefaultStatic_IsDirectOutline()
    {
        Assert.Equal(MachinaTextRenderStrategy.DirectOutlineStatic, MachinaTextRenderStrategyCatalog.DefaultStatic);
    }

    [Fact]
    public void TextRenderStrategy_Msdf_IsExplicitExperimental()
    {
        Assert.True(MachinaTextRenderStrategyCatalog.IsExperimental(MachinaTextRenderStrategy.MsdfScalableExperimental));
        Assert.False(MachinaTextRenderStrategyCatalog.IsExperimental(MachinaTextRenderStrategy.DirectOutlineStatic));
    }

    [Fact]
    public void TextRenderStrategy_NamesAreStableForManifest()
    {
        Assert.Equal("DirectOutlineStatic", MachinaTextRenderStrategyCatalog.GetStableName(MachinaTextRenderStrategy.DirectOutlineStatic));
        Assert.Equal("MsdfScalableExperimental", MachinaTextRenderStrategyCatalog.GetStableName(MachinaTextRenderStrategy.MsdfScalableExperimental));
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_RendersNonEmptyText()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());

        DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateOptions("Machina", TypographyFixtureFont.Face));

        Assert.True(result.Success);
        Assert.NotNull(result.Image);
        Assert.NotNull(result.Mask);
        Assert.True(CountInk(result.Mask!) > 0);
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_WhitespaceAdvancesButDoesNotInk()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());

        DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateOptions("A A", TypographyFixtureFont.Face));

        Assert.True(result.Success);
        Assert.NotNull(result.Mask);

        DirectOutlineGlyphRenderPlacement whitespace = Assert.Single(result.Glyphs, static glyph => glyph.IsWhitespace);
        Assert.NotNull(whitespace);
        Assert.Null(whitespace.InkBounds);

        DirectOutlineGlyphRenderPlacement left = result.Glyphs[0];
        DirectOutlineGlyphRenderPlacement right = result.Glyphs[2];
        Assert.True(right.X > whitespace.X);
        Assert.True(left.X < whitespace.X);
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_ReturnsGlyphPlacements()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());

        DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateOptions("Aa0", TypographyFixtureFont.Face));

        Assert.True(result.Success);
        Assert.Equal(3, result.Glyphs.Count);
        Assert.All(result.Glyphs.Where(static glyph => !glyph.IsWhitespace), glyph => Assert.NotNull(glyph.InkBounds));
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_ReturnsInkBounds()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());

        DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateOptions("Hello Machina", TypographyFixtureFont.Face));

        Assert.True(result.Success);
        Assert.NotNull(result.InkBounds);
        Assert.True(result.InkBounds!.Width > 0);
        Assert.True(result.InkBounds.Height > 0);
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_IsDeterministic()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());

        DirectOutlineTextRenderResult first = await renderer.RenderAsync(CreateOptions("Machina", TypographyFixtureFont.Face));
        DirectOutlineTextRenderResult second = await renderer.RenderAsync(CreateOptions("Machina", TypographyFixtureFont.Face));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(ToPixels(first.Image!), ToPixels(second.Image!));
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_DoesNotUseMsdfArtifacts()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyFixtureFont.CreateSource());
        string directory = Path.Combine(Path.GetTempPath(), "machina-direct-outline-m9d", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateOptions("Machina", TypographyFixtureFont.Face));

            Assert.True(result.Success);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.dfpage", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.ppm", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.font-atlas.toml", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_UsesExistingPairAdjustments()
    {
        TypographyGlyphOutlineSource sourceWithKerning = TypographyKerningFixtureFont.CreateSource();
        DirectOutlineStaticTextRenderer kernedRenderer = new(sourceWithKerning);
        DirectOutlineStaticTextRenderer unkernedRenderer = new(new NoPairAdjustmentOutlineSource(sourceWithKerning));

        DirectOutlineTextRenderResult kerned = await kernedRenderer.RenderAsync(CreateOptions("AV", TypographyKerningFixtureFont.Face));
        DirectOutlineTextRenderResult plain = await unkernedRenderer.RenderAsync(
            CreateOptions("AV", TypographyKerningFixtureFont.Face) with { UsePairAdjustments = false });

        Assert.True(kerned.Success);
        Assert.True(plain.Success);
        Assert.True(kerned.InkBounds!.Right < plain.InkBounds!.Right);
    }

    private static DirectOutlineTextRenderOptions CreateOptions(string text, FontFaceId face)
    {
        return new DirectOutlineTextRenderOptions(
            text,
            face,
            32d,
            160,
            64,
            Foreground,
            Background,
            8d,
            40d,
            Supersample: 4,
            ShowBaselineGuide: true,
            BaselineGuideColor: new Rgba32(255, 0, 0, 255));
    }

    private static int CountInk(InkMask mask)
    {
        int count = 0;
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.IsInk(x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] ToPixels(RgbaImage image)
    {
        return image.Pixels
            .SelectMany(static pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A })
            .ToArray();
    }

    private sealed class NoPairAdjustmentOutlineSource : IGlyphOutlineSource
    {
        private readonly IGlyphOutlineSource inner;

        public NoPairAdjustmentOutlineSource(IGlyphOutlineSource inner)
        {
            this.inner = inner;
        }

        public ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
            FontFaceId face,
            int codepoint,
            GlyphOutlineLoadOptions options,
            CancellationToken cancellationToken = default)
        {
            return inner.LoadGlyphOutlineAsync(face, codepoint, options, cancellationToken);
        }
    }
}
