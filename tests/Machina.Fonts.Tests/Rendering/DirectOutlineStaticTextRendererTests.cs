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

    [Fact]
    public async Task DirectOutlineStaticRenderer_RendersPresenterTextSamples()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyKerningFixtureFont.CreateSource());

        string[] samples =
        [
            "Hello Machina",
            "Machina UI",
            "Settings",
            "Direct outline static text",
            "AV To Ta Wa Yo",
            "Aa0 1234567890",
            "The quick brown fox jumps over the lazy dog.",
        ];

        foreach (string sample in samples)
        {
            DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateCrimsonOptions(sample, 24d));
            Assert.True(result.Success, sample);
            Assert.NotNull(result.Image);
            Assert.NotNull(result.InkBounds);
            Assert.True(result.InkBounds!.Width > 0, sample);
            Assert.True(result.InkBounds.Height > 0, sample);
        }
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_SupportsSmallUiSizes()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyKerningFixtureFont.CreateSource());

        DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateCrimsonOptions("Settings", 16d));

        Assert.True(result.Success);
        Assert.NotNull(result.InkBounds);
        Assert.True(result.InkBounds!.Width > 0);
        Assert.True(result.InkBounds.Height > 0);
    }

    [Fact]
    public async Task DirectOutlineStaticRenderer_SupportsMultipleFontSizes()
    {
        DirectOutlineStaticTextRenderer renderer = new(TypographyKerningFixtureFont.CreateSource());

        double[] sizes = [16d, 24d, 32d];
        InkMaskBounds? previous = null;

        foreach (double size in sizes)
        {
            DirectOutlineTextRenderResult result = await renderer.RenderAsync(CreateCrimsonOptions("Hello Machina", size));
            Assert.True(result.Success);
            Assert.NotNull(result.InkBounds);

            if (previous is not null)
            {
                Assert.True(result.InkBounds!.Width > previous.Width);
                Assert.True(result.InkBounds.Height > previous.Height);
            }

            previous = result.InkBounds;
        }
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

    private static DirectOutlineTextRenderOptions CreateCrimsonOptions(string text, double emSize)
    {
        return new DirectOutlineTextRenderOptions(
            text,
            TypographyKerningFixtureFont.Face,
            emSize,
            640,
            96,
            Foreground,
            Background,
            8d,
            emSize + 16d,
            Supersample: 4);
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
