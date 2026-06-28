using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Generation.Typography;

public sealed class TypographyGlyphPairAdjustmentSourceTests
{
    private static readonly GlyphOutlineLoadOptions NormalizedOptions = new(32, 0, GlyphHintingMode.None, normalizeToEm: true);

    [Fact]
    public void FixtureFont_ExistsAndHasLicense()
    {
        Assert.True(File.Exists(TypographyKerningFixtureFont.FontPath));
        Assert.True(File.Exists(TypographyKerningFixtureFont.LicensePath));
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_AdvanceAndBearing_AreStableForFixtureGlyphs()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult upperA = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'A',
            NormalizedOptions);
        GlyphOutlineLoadResult lowerA = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'a',
            NormalizedOptions);

        Assert.True(upperA.Success);
        Assert.True(lowerA.Success);
        Assert.NotNull(upperA.Metrics);
        Assert.NotNull(lowerA.Metrics);
        AssertClose(19.584, upperA.Metrics.Advance);
        AssertClose(0.48, upperA.Metrics.BearingX);
        AssertClose(22.4, upperA.Metrics.BearingY);
        AssertClose(19.584, lowerA.Metrics.Advance);
        AssertClose(1.504, lowerA.Metrics.BearingX);
        AssertClose(16.32, lowerA.Metrics.BearingY);
    }

    [Fact]
    public async Task TypographyPairAdjustmentSource_ReturnsExpectedAdjustmentForKnownPair()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        GlyphKey left = GlyphKey.FromChar(TypographyKerningFixtureFont.Face, 'A', 32);
        GlyphKey right = GlyphKey.FromChar(TypographyKerningFixtureFont.Face, 'V', 32);

        GlyphPairAdjustment? adjustment = await source.GetPairAdjustmentAsync(left, right);

        Assert.NotNull(adjustment);
        Assert.True(adjustment!.AdvanceX < 0d);
        Assert.InRange(adjustment.AdvanceX, -3d, -2d);
        Assert.Equal(0d, adjustment.AdvanceY);
    }

    [Fact]
    public async Task TypographyPairAdjustmentSource_ReturnsNoAdjustmentForSpaceMonoKnownPair()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();
        GlyphKey left = GlyphKey.FromChar(TypographyFixtureFont.Face, 'A', 32);
        GlyphKey right = GlyphKey.FromChar(TypographyFixtureFont.Face, 'V', 32);

        GlyphPairAdjustment? adjustment = await source.GetPairAdjustmentAsync(left, right);

        Assert.Null(adjustment);
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
    }
}
