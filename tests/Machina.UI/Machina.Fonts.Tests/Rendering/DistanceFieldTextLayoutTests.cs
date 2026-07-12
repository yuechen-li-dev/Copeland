using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DistanceFieldTextLayoutTests
{
    private static readonly DistanceFieldTextRenderOptions Options = new(
        64,
        32,
        new FontFaceId("machina-layout"),
        32,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright,
        DistanceFieldKind.Msdf,
        16,
        16,
        2d,
        Rgba32.White,
        Rgba32.Black,
        3d,
        20d);

    [Fact]
    public void DistanceFieldTextLayout_UsesAdvanceForPenMovement()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create("AB", Options.Face, Options.EmSize, Options.Weight, Options.Slant);
        Dictionary<GlyphKey, GlyphMetrics> metrics = new()
        {
            [run.GlyphKeys[0]] = new GlyphMetrics(5, 1, 7, 4, 6),
            [run.GlyphKeys[1]] = new GlyphMetrics(7, 2, 7, 4, 6),
        };

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metrics, Options);

        Assert.Equal(Options.X, layout.Placements[0].X);
        Assert.Equal(Options.X + 5d, layout.Placements[1].X);
        Assert.Equal(12d, layout.Width);
    }

    [Fact]
    public void DistanceFieldTextLayout_UsesBearingForDrawPlacement()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create("A", Options.Face, Options.EmSize, Options.Weight, Options.Slant);
        GlyphMetrics metrics = new(5, 2.5, 7.5, 4, 6);
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = new()
        {
            [run.GlyphKeys[0]] = metrics,
        };

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metricsByGlyph, Options);

        Assert.Equal(metrics, layout.Placements[0].Metrics);
        Assert.Equal(Options.BaselineY, layout.Placements[0].BaselineY);
        Assert.Equal(Options.X + metrics.BearingX, layout.Placements[0].X + layout.Placements[0].Metrics.BearingX);
        Assert.Equal(Options.BaselineY - metrics.BearingY, layout.Placements[0].BaselineY - layout.Placements[0].Metrics.BearingY);
    }

    [Fact]
    public void DistanceFieldTextLayout_WhitespaceAdvancesWithoutQuad()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create("A A", Options.Face, Options.EmSize, Options.Weight, Options.Slant);
        Dictionary<GlyphKey, GlyphMetrics> metrics = run.GlyphKeys.Distinct().ToDictionary(
            static key => key,
            static key => key.Codepoint == ' '
                ? new GlyphMetrics(6, 0, 0, 0, 0)
                : new GlyphMetrics(5, 1, 7, 4, 6));

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metrics, Options);

        Assert.Equal(3, layout.Placements.Count);
        Assert.False(layout.Placements[0].IsWhitespace);
        Assert.True(layout.Placements[1].IsWhitespace);
        Assert.False(layout.Placements[2].IsWhitespace);
        Assert.Equal(Options.X + 11d, layout.Placements[2].X);
        Assert.Equal(16d, layout.Width);
    }

    [Fact]
    public void DistanceFieldTextLayout_AppliesPairAdjustment()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create("AV", Options.Face, Options.EmSize, Options.Weight, Options.Slant);
        Dictionary<GlyphKey, GlyphMetrics> metrics = run.GlyphKeys.ToDictionary(
            static key => key,
            static _ => new GlyphMetrics(6, 1, 7, 4, 6));
        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = new()
        {
            [new GlyphPairKey(run.GlyphKeys[0], run.GlyphKeys[1])] = new GlyphPairAdjustment(-2d),
        };

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metrics, Options, pairAdjustments: pairAdjustments);

        Assert.Equal(Options.X + 4d, layout.Placements[1].X);
        Assert.Equal(10d, layout.Width);
    }

    [Fact]
    public void DistanceFieldTextLayout_NoAdjustmentWhenSourceMissing()
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create("AV", Options.Face, Options.EmSize, Options.Weight, Options.Slant);
        Dictionary<GlyphKey, GlyphMetrics> metrics = run.GlyphKeys.ToDictionary(
            static key => key,
            static _ => new GlyphMetrics(6, 1, 7, 4, 6));

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metrics, Options);

        Assert.Equal(Options.X + 6d, layout.Placements[1].X);
        Assert.Equal(12d, layout.Width);
    }
}
