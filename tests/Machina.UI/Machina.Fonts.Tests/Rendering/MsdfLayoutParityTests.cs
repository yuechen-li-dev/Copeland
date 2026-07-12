using System.Text;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class MsdfLayoutParityTests
{
    [Fact]
    public async Task MsdfLayout_UsesSameGlyphOrderAsDirectOutline()
    {
        LayoutParityFixture fixture = await CreateFixtureAsync("Hello Machina");

        Assert.Equal(
            fixture.Direct.Glyphs.Select(static glyph => glyph.Key.Codepoint).ToArray(),
            fixture.Layout.Placements.Select(static placement => placement.Key.Codepoint).ToArray());
    }

    [Fact]
    public async Task MsdfLayout_UsesSameAdvancesAsDirectOutline()
    {
        LayoutParityFixture fixture = await CreateFixtureAsync("Direct outline static text");

        Assert.Equal(fixture.Direct.Glyphs.Count, fixture.Layout.Placements.Count);

        for (int index = 0; index < fixture.Layout.Placements.Count; index++)
        {
            Assert.Equal(fixture.Direct.Glyphs[index].X, fixture.Layout.Placements[index].X, 6);
            Assert.Equal(fixture.Direct.Glyphs[index].BaselineY, fixture.Layout.Placements[index].BaselineY, 6);
        }
    }

    [Fact]
    public async Task MsdfLayout_UsesSamePairAdjustmentsAsDirectOutline()
    {
        LayoutParityFixture fixture = await CreateFixtureAsync("AV To Ta Wa Yo");

        Assert.Equal(fixture.Direct.Glyphs.Count, fixture.Layout.Placements.Count);

        for (int index = 1; index < fixture.Layout.Placements.Count; index++)
        {
            double directAdvance = fixture.Direct.Glyphs[index].X - fixture.Direct.Glyphs[index - 1].X;
            double msdfAdvance = fixture.Layout.Placements[index].X - fixture.Layout.Placements[index - 1].X;
            Assert.Equal(directAdvance, msdfAdvance, 6);
        }
    }

    [Fact]
    public async Task MsdfLayout_WhitespaceMatchesDirectOutline()
    {
        LayoutParityFixture fixture = await CreateFixtureAsync("A A");

        Assert.Equal(fixture.Direct.Glyphs.Count, fixture.Layout.Placements.Count);
        Assert.Contains(fixture.Direct.Glyphs, static glyph => glyph.IsWhitespace);
        Assert.Contains(fixture.Layout.Placements, static placement => placement.IsWhitespace);

        for (int index = 0; index < fixture.Layout.Placements.Count; index++)
        {
            Assert.Equal(fixture.Direct.Glyphs[index].IsWhitespace, fixture.Layout.Placements[index].IsWhitespace);
            Assert.Equal(fixture.Direct.Glyphs[index].X, fixture.Layout.Placements[index].X, 6);
        }
    }

    private static async Task<LayoutParityFixture> CreateFixtureAsync(string text)
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        DirectOutlineStaticTextRenderer renderer = new(source, source);
        DirectOutlineTextRenderResult direct = await renderer.RenderAsync(
            new DirectOutlineTextRenderOptions(
                text,
                TypographyKerningFixtureFont.Face,
                32d,
                480,
                96,
                new Rgba32(240, 240, 240, 255),
                new Rgba32(16, 16, 24, 255),
                8d,
                40d,
                UsePairAdjustments: true));

        Assert.True(direct.Success);

        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            TypographyKerningFixtureFont.Face,
            32d,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright);

        GlyphOutlineLoadOptions loadOptions = new(
            32f,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);

        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = [];
        foreach (GlyphKey key in run.GlyphKeys.Distinct())
        {
            GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
                key.Face,
                key.Codepoint,
                loadOptions);

            Assert.True(result.Success);
            Assert.NotNull(result.Outline);
            metricsByGlyph[key] = result.Outline!.Metrics;
        }

        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = await CollectPairAdjustmentsAsync(source, run);
        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
            run,
            metricsByGlyph,
            new DistanceFieldTextRenderOptions(
                480,
                96,
                TypographyKerningFixtureFont.Face,
                32d,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright,
                DistanceFieldKind.Msdf,
                32,
                32,
                4d,
                new Rgba32(240, 240, 240, 255),
                new Rgba32(16, 16, 24, 255),
                8d,
                40d),
            pairAdjustments: pairAdjustments);

        return new LayoutParityFixture(direct, layout);
    }

    private static async Task<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        TypographyGlyphOutlineSource source,
        DistanceFieldTextRun run)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> adjustments = [];
        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;

        foreach (GlyphKey key in run.GlyphKeys)
        {
            bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
            if (previousKey is GlyphKey previous && !previousWasWhitespace && !isWhitespace)
            {
                GlyphPairAdjustment? adjustment = await source.GetPairAdjustmentAsync(previous, key);
                if (adjustment is not null)
                {
                    adjustments[new GlyphPairKey(previous, key)] = adjustment;
                }
            }

            previousKey = key;
            previousWasWhitespace = isWhitespace;
        }

        return adjustments;
    }

    private sealed record LayoutParityFixture(
        DirectOutlineTextRenderResult Direct,
        DistanceFieldTextLayoutResult Layout);
}
