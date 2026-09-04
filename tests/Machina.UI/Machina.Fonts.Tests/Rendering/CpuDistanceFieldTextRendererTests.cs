using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class CpuDistanceFieldTextRendererTests
{
    private static readonly Rgba32 Background = new(8, 8, 12, 255);
    private static readonly DistanceFieldTextRenderOptions Options = new(
        32,
        16,
        new FontFaceId("machina-reference-render"),
        32,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright,
        DistanceFieldKind.Msdf,
        4,
        6,
        4d,
        Rgba32.White,
        Background,
        1d,
        10d);

    [Fact]
    public void RenderText_PlacesGlyphsUsingAdvance()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        GlyphAtlasEntry entryB = CreateEntry('B', 4, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA, entryB);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Contains(FindNonBackgroundPixels(image), point => point.X < 4);
        Assert.Contains(FindNonBackgroundPixels(image), point => point.X >= 6);
    }

    [Fact]
    public void RenderText_SkipsWhitespaceButAdvancesPen()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("A A");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        int minX = FindNonBackgroundPixels(image).Min(static point => point.X);
        int maxX = FindNonBackgroundPixels(image).Max(static point => point.X);

        Assert.InRange(minX, 0, 1);
        Assert.True(maxX >= 9);
    }

    [Fact]
    public void RenderText_ProducesNonBlankImage()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("A");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Contains(image.Pixels, pixel => pixel != Background);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_BaselineGuideDrawsAtRequestedY()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 10);
        Rgba32 baselineGuide = new(255, 0, 0, 255);

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options with
            {
                BaselineY = 10,
                ShowBaselineGuide = true,
                BaselineGuideColor = baselineGuide,
            });

        for (int x = 0; x < image.Width; x++)
        {
            Assert.Equal(baselineGuide, image.GetPixel(x, 10));
        }
    }

    [Fact]
    public void RenderText_IsDeterministic()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        GlyphAtlasEntry entryB = CreateEntry('B', 4, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA, entryB);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        RgbaImage first = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);
        RgbaImage second = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void RenderText_RejectsMissingAtlasEntry()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options));

        Assert.Contains("U+0042", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderText_UsesBaselineAndBearing()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0, bearingY: 4);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult upperLayout = CreateLayout("A", baselineY: 6);
        DistanceFieldTextLayoutResult lowerLayout = CreateLayout("A", baselineY: 10);

        RgbaImage upper = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            upperLayout,
            Options with { BaselineY = 6 });
        RgbaImage lower = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            lowerLayout,
            Options with { BaselineY = 10 });

        int upperTop = FindNonBackgroundPixels(upper).Min(static point => point.Y);
        int lowerTop = FindNonBackgroundPixels(lower).Min(static point => point.Y);

        Assert.True(lowerTop > upperTop);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_UsesPlaneBoundsRelativeToBaseline()
    {
        GlyphFieldPlacement placement = new(-1d, -8d, 3d, 2d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, bearingY: 99d, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 14d);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);

        Assert.Equal(0, bounds.X);
        Assert.Equal(6, bounds.Y);
        Assert.Equal(4, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void GlyphFieldPlacement_ConvertsToDrawRectWithBaseline()
    {
        GlyphFieldPlacement placement = new(2d, -9d, 8d, 3d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 20d);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);

        Assert.Equal(3, bounds.X);
        Assert.Equal(11, bounds.Y);
        Assert.Equal(6, bounds.Width);
        Assert.Equal(12, bounds.Height);
    }

    [Fact]
    public void GlyphFieldPlacement_PreservesSubpixelPlaneUntilRasterSampling()
    {
        GlyphFieldPlacement placement = new(2d, -9d, 8d, 3d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 20.375d);

        DistanceFieldGlyphDrawPlane plane = CpuDistanceFieldGlyphRenderer.ComputeDrawPlane(layout.Placements[0], entry);

        Assert.Equal(3d, plane.X);
        Assert.Equal(11.375d, plane.Y);
        Assert.Equal(6d, plane.Width);
        Assert.Equal(12d, plane.Height);
    }

    [Fact]
    public void MsdfDrawRect_UsesPlaneBoundsNotAtlasRect()
    {
        GlyphFieldPlacement placement = new(0d, -6d, 4d, 0d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, atlasWidth: 24, atlasHeight: 20, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 18d);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);

        Assert.Equal(4, bounds.Width);
        Assert.Equal(6, bounds.Height);
    }

    [Fact]
    public void MsdfDrawRect_DoesNotDoubleApplyPadding()
    {
        GlyphFieldPlacement paddedPlacement = new(-4d, -10d, 8d, 2d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, atlasWidth: 32, atlasHeight: 32, placement: paddedPlacement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 18d);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);

        Assert.Equal(12, bounds.Width);
        Assert.Equal(12, bounds.Height);
        Assert.Equal(8, bounds.Y);
    }

    [Fact]
    public void MsdfDrawRect_DoesNotDoubleFlipY()
    {
        GlyphFieldPlacement placement = new(0d, -8d, 4d, 2d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 20d);

        DistanceFieldGlyphDrawBounds first = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);
        DistanceFieldGlyphDrawBounds second = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);

        Assert.Equal(first.Y, second.Y);
        Assert.Equal(12, first.Y);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_DoesNotDoubleApplyBearingYWhenUsingPlacement()
    {
        GlyphFieldPlacement placement = new(0d, -6d, 4d, 0d, 4d, 1d);
        GlyphAtlasEntry lowBearingEntry = CreateEntry('A', 0, 0, bearingY: 6d, placement: placement);
        GlyphAtlasEntry highBearingEntry = CreateEntry('A', 0, 0, bearingY: 18d, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: 12d);

        DistanceFieldGlyphDrawBounds lowBearingBounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], lowBearingEntry);
        DistanceFieldGlyphDrawBounds highBearingBounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], highBearingEntry);

        Assert.Equal(lowBearingBounds.Y, highBearingBounds.Y);
        Assert.Equal(6, lowBearingBounds.Y);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_BaselineLandsOnRequestedBaseline()
    {
        const int requestedBaseline = 30;
        GlyphFieldPlacement placement = new(0d, -8.4d, 4d, 1.2d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, bearingY: 6d, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: requestedBaseline);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);
        int baselineInOutput = CpuDistanceFieldGlyphRenderer.ComputeBaselineOffsetInOutput(entry, bounds.Height);

        Assert.Equal(10, bounds.Height);
        Assert.Equal(requestedBaseline, bounds.Y + baselineInOutput);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_DoesNotDoubleRoundBaseline()
    {
        const int requestedBaseline = 30;
        GlyphFieldPlacement placement = new(0d, -8.4d, 4d, 1.2d, 4d, 1d);
        GlyphAtlasEntry entry = CreateEntry('A', 0, 0, bearingY: 6d, placement: placement);
        DistanceFieldTextLayoutResult layout = CreateLayout("A", baselineY: requestedBaseline);

        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[0], entry);
        int baselineInOutput = CpuDistanceFieldGlyphRenderer.ComputeBaselineOffsetInOutput(entry, bounds.Height);
        int independentlyRoundedTop = (int)Math.Round(
            requestedBaseline + placement.PlaneTop,
            MidpointRounding.AwayFromZero);

        Assert.Equal(requestedBaseline, bounds.Y + baselineInOutput);
        Assert.NotEqual(independentlyRoundedTop, bounds.Y);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_UsesPlacementNotTileSize()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            16,
            6,
            static (_, _) => [1f, 1f, 1f]);
        GlyphFieldPlacement compactPlacement = new(0d, -6d, 2d, 0d, 4d, 1d);
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0, atlasWidth: 8, atlasHeight: 6, pageWidth: 16, pageHeight: 6, placement: compactPlacement);
        GlyphAtlasEntry entryB = CreateEntry('B', 8, 0, atlasWidth: 8, atlasHeight: 6, pageWidth: 16, pageHeight: 6, placement: compactPlacement);
        FontAtlasSnapshot snapshot = CreateSnapshot(16, 6, entryA, entryB);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        IReadOnlyList<(int X, int Y)> pixels = FindNonBackgroundPixels(image);
        Assert.DoesNotContain(pixels, point => point.X == 3 || point.X == 4);
        Assert.Contains(pixels, point => point.X <= 2);
        Assert.Contains(pixels, point => point.X >= 5);
    }

    [Fact]
    public void CpuDistanceFieldTextRenderer_ContiguousGlyphsDoNotOverlapDueToTileSize()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            16,
            6,
            static (_, _) => [1f, 1f, 1f]);
        GlyphFieldPlacement compactPlacement = new(0d, -6d, 2d, 0d, 4d, 1d);
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0, atlasWidth: 8, atlasHeight: 6, pageWidth: 16, pageHeight: 6, placement: compactPlacement);
        FontAtlasSnapshot snapshot = CreateSnapshot(16, 6, entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("AAA");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        IReadOnlyList<(int X, int Y)> pixels = FindNonBackgroundPixels(image);
        Assert.Contains(pixels, point => point.X <= 2);
        Assert.Contains(pixels, point => point.X >= 9);
        Assert.DoesNotContain(pixels, point => point.X == 3 || point.X == 7);
    }

    [Fact]
    public void DistanceFieldTextLayout_KerningStillAppliesBeforePlacement()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            "AV",
            Options.Face,
            Options.EmSize,
            Options.Weight,
            Options.Slant);
        Dictionary<GlyphKey, GlyphMetrics> metrics = run.GlyphKeys.ToDictionary(
            static key => key,
            static _ => new GlyphMetrics(4, 0, 6, 4, 6));
        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = new()
        {
            [new GlyphPairKey(run.GlyphKeys[0], run.GlyphKeys[1])] = new GlyphPairAdjustment(-1, 0),
        };

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
            run,
            metrics,
            Options,
            pairAdjustments: pairAdjustments);
        GlyphAtlasEntry entry = CreateEntry('V', 0, 0, placement: new GlyphFieldPlacement(0d, -6d, 2d, 0d, 4d, 1d));
        DistanceFieldGlyphDrawBounds bounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(layout.Placements[1], entry);

        Assert.Equal(4, layout.Placements[1].X);
        Assert.Equal(4, bounds.X);
    }

    [Fact]
    public void Whitespace_RemainsMetricsOnly()
    {
        DistanceFieldTextLayoutResult layout = CreateLayout("A A");

        Assert.True(layout.Placements[1].IsWhitespace);
        Assert.Equal(5d, layout.Placements[2].X - layout.Placements[1].X);
    }

    private static DistanceFieldTextLayoutResult CreateLayout(string text, double baselineY = 10d)
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            Options.Face,
            Options.EmSize,
            Options.Weight,
            Options.Slant);

        Dictionary<GlyphKey, GlyphMetrics> metrics = [];
        foreach (GlyphKey key in run.GlyphKeys)
        {
            metrics[key] = key.Codepoint == ' '
                ? new GlyphMetrics(5, 0, 0, 0, 0)
                : new GlyphMetrics(4, 0, 6, 4, 6);
        }

        return DistanceFieldTextLayout.Layout(
            run,
            metrics,
            Options with { BaselineY = baselineY });
    }

    private static FontAtlasSnapshot CreateSnapshot(int pageWidth, int pageHeight, params GlyphAtlasEntry[] entries)
    {
        return new FontAtlasSnapshot(
            1,
            [new FontAtlasPage(0, "synthetic.dfpage", pageWidth, pageHeight, null)],
            entries.ToDictionary(static entry => entry.Key, static entry => entry));
    }

    private static FontAtlasSnapshot CreateSnapshot(params GlyphAtlasEntry[] entries)
    {
        return CreateSnapshot(8, 6, entries);
    }

    private static GlyphAtlasEntry CreateEntry(
        char value,
        int x,
        int y,
        double bearingY = 6,
        int atlasWidth = 4,
        int atlasHeight = 6,
        int pageWidth = 8,
        int pageHeight = 6,
        GlyphFieldPlacement? placement = null)
    {
        GlyphKey key = GlyphKey.FromChar(Options.Face, value, Options.EmSize);
        GlyphMetrics metrics = new(4, 0, bearingY, 4, 6);
        placement ??= GlyphFieldPlacement.CreateFromMetricsBox(metrics);
        return new GlyphAtlasEntry(
            key,
            0,
            x,
            y,
            atlasWidth,
            atlasHeight,
            x / (double)pageWidth,
            y / (double)pageHeight,
            (x + atlasWidth) / (double)pageWidth,
            (y + atlasHeight) / (double)pageHeight,
            metrics,
            placement);
    }

    private static DistanceFieldPageReference CreateSolidPage()
    {
        return RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            8,
            6,
            static (_, _) => [1f, 1f, 1f]);
    }

    private static IReadOnlyList<(int X, int Y)> FindNonBackgroundPixels(RgbaImage image)
    {
        List<(int X, int Y)> result = [];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y) != Background)
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }
}
