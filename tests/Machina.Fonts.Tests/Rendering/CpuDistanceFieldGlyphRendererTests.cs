using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class CpuDistanceFieldGlyphRendererTests
{
    [Fact]
    public void RenderGlyph_ProducesImageWithExpectedDimensions()
    {
        DistanceFieldPageReference page = CreateBinaryPage();
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 4, 4, page.Width, page.Height);

        RgbaImage image = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(12, 9, Rgba32.White, Rgba32.Black));

        Assert.Equal(12, image.Width);
        Assert.Equal(9, image.Height);
    }

    [Fact]
    public void RenderGlyph_UsesForegroundAndBackground()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            2,
            1,
            static (x, _) => x == 0 ? [0.1f, 0.1f, 0.1f] : [0.9f, 0.9f, 0.9f]);
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 2, 1, page.Width, page.Height);

        RgbaImage image = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(
                2,
                1,
                new Rgba32(255, 0, 0, 255),
                new Rgba32(0, 0, 255, 255),
                Threshold: 0.5d));

        Rgba32 left = image.GetPixel(0, 0);
        Rgba32 right = image.GetPixel(1, 0);

        Assert.True(left.B > left.R);
        Assert.True(right.R > right.B);
    }

    [Fact]
    public void RenderGlyph_ProducesNonUniformPixelsForSyntheticField()
    {
        DistanceFieldPageReference page = CreateGradientPage();
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 4, 4, page.Width, page.Height);

        RgbaImage image = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(8, 8, Rgba32.White, Rgba32.Black));

        Assert.Contains(image.Pixels, pixel => pixel != image.Pixels[0]);
    }

    [Fact]
    public void RenderGlyph_IsDeterministic()
    {
        DistanceFieldPageReference page = CreateGradientPage();
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 4, 4, page.Width, page.Height);
        DistanceFieldRenderOptions options = new(10, 10, Rgba32.White, Rgba32.Black, PxRange: 4d, Threshold: 0.5d);

        RgbaImage first = CpuDistanceFieldGlyphRenderer.RenderGlyph(page, entry, options);
        RgbaImage second = CpuDistanceFieldGlyphRenderer.RenderGlyph(page, entry, options);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void RenderGlyph_RespectsEntryRect()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            4,
            2,
            static (x, _) => x < 2 ? [0.1f, 0.1f, 0.1f] : [0.9f, 0.9f, 0.9f]);
        GlyphAtlasEntry leftEntry = RenderingTestHelpers.CreateEntry(0, 0, 2, 2, page.Width, page.Height, 'L');
        GlyphAtlasEntry rightEntry = RenderingTestHelpers.CreateEntry(2, 0, 2, 2, page.Width, page.Height, 'R');
        DistanceFieldRenderOptions options = new(6, 6, Rgba32.White, Rgba32.Black);

        RgbaImage leftImage = CpuDistanceFieldGlyphRenderer.RenderGlyph(page, leftEntry, options);
        RgbaImage rightImage = CpuDistanceFieldGlyphRenderer.RenderGlyph(page, rightEntry, options);

        Assert.True(leftImage.GetPixel(3, 3).R < 64);
        Assert.True(rightImage.GetPixel(3, 3).R > 191);
    }

    [Fact]
    public void RenderGlyph_CanFlipYOrUsesDocumentedYPolicy()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            1,
            2,
            static (_, y) => y == 0 ? [0.9f, 0.9f, 0.9f] : [0.1f, 0.1f, 0.1f]);
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 1, 2, page.Width, page.Height);

        RgbaImage normal = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(1, 2, Rgba32.White, Rgba32.Black, FlipY: false));
        RgbaImage flipped = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(1, 2, Rgba32.White, Rgba32.Black, FlipY: true));

        Assert.True(normal.GetPixel(0, 0).R > normal.GetPixel(0, 1).R);
        Assert.True(flipped.GetPixel(0, 1).R > flipped.GetPixel(0, 0).R);
    }

    [Fact]
    public void CpuDistanceFieldGlyphRenderer_UsesPlacementBounds()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("machina-reference-render"), 'A', 32);
        GlyphMetrics metrics = new(4, 0, 6, 4, 6);
        GlyphAtlasEntry entry = new(
            key,
            0,
            0,
            0,
            8,
            6,
            0d,
            0d,
            1d,
            1d,
            metrics,
            new GlyphFieldPlacement(1d, -5d, 3d, -1d, 4d, 1d));
        DistanceFieldGlyphPlacement placement = new(key, metrics, 10d, 12d, 1d, false);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(placement, entry);

        Assert.Equal(11, bounds.X);
        Assert.Equal(7, bounds.Y);
        Assert.Equal(2, bounds.Width);
        Assert.Equal(4, bounds.Height);
    }

    private static DistanceFieldPageReference CreateBinaryPage()
    {
        return RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            4,
            4,
            static (x, y) =>
            {
                bool inside = x is 1 or 2 && y is 1 or 2;
                float value = inside ? 0.9f : 0.1f;
                return [value, value, value];
            });
    }

    private static DistanceFieldPageReference CreateGradientPage()
    {
        return RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            4,
            4,
            static (x, y) =>
            {
                float value = 0.15f + ((x + y) * 0.1f);
                return [value + 0.02f, value, value + 0.04f];
            });
    }
}
