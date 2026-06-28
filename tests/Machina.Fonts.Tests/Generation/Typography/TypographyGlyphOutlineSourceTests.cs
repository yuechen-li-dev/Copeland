using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Generation.Typography;

public sealed class TypographyGlyphOutlineSourceTests
{
    private static readonly GlyphOutlineLoadOptions NormalizedOptions = new(32, 0, GlyphHintingMode.None, normalizeToEm: true);

    [Fact]
    public void FixtureFont_ExistsAndHasLicense()
    {
        Assert.True(File.Exists(TypographyFixtureFont.FontPath));
        Assert.True(File.Exists(TypographyFixtureFont.LicensePath));
        Assert.True(File.Exists(TypographyFixtureFont.ReadmePath));
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_LoadsFixtureFont()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'A',
            NormalizedOptions);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_LoadsMetricsForA()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'A',
            NormalizedOptions);

        Assert.True(result.Success);
        Assert.NotNull(result.Metrics);
        Assert.NotNull(result.Outline);
        Assert.Equal(GlyphKey.FromCodepoint(TypographyFixtureFont.Face, 'A', 32), result.Outline.Key);
        Assert.True(result.Metrics.Advance > 0);
        Assert.True(result.Metrics.Width >= 0);
        Assert.True(result.Metrics.Height >= 0);
        Assert.True(result.Outline.Bounds.MaxX >= result.Outline.Bounds.MinX);
        Assert.True(result.Outline.Bounds.MaxY >= result.Outline.Bounds.MinY);
        AssertClose(19.584, result.Metrics.Advance);
        AssertClose(0.48, result.Metrics.BearingX);
        AssertClose(22.4, result.Metrics.BearingY);
        AssertClose(18.624, result.Metrics.Width);
        AssertClose(22.4, result.Metrics.Height);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_SpaceHasMetricsAndEmptyContours()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            ' ',
            NormalizedOptions);

        Assert.True(result.Success);
        Assert.NotNull(result.Metrics);
        Assert.NotNull(result.Outline);
        Assert.True(result.Metrics.Advance > 0);
        Assert.Empty(result.Outline.Contours);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_LoadsContoursForA()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'A',
            NormalizedOptions);

        Assert.True(result.Success);
        Assert.NotNull(result.Outline);
        Assert.Equal(2, result.Outline.Contours.Count);

        int totalSegments = result.Outline.Contours.Sum(static contour => contour.Segments.Count);
        Assert.Equal(12, totalSegments);
        Assert.Equal("LLLLLLLL;LLLL;", SummarizeContours(result.Outline));
        Assert.All(
            EnumeratePoints(result.Outline),
            static point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            });
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_LoadsContoursForLowercaseAndDigit()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult lowercase = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'a',
            NormalizedOptions);

        GlyphOutlineLoadResult digit = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            '0',
            NormalizedOptions);

        Assert.True(lowercase.Success);
        Assert.True(digit.Success);
        Assert.NotEmpty(lowercase.Outline!.Contours);
        Assert.NotEmpty(digit.Outline!.Contours);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_PreservesQuadraticSegments()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'a',
            NormalizedOptions);

        Assert.True(result.Success);
        Assert.NotNull(result.Outline);
        Assert.Contains(
            result.Outline.Contours.SelectMany(static contour => contour.Segments),
            static segment => segment is GlyphQuadraticSegment);
        Assert.DoesNotContain(
            result.Outline.Contours.SelectMany(static contour => contour.Segments),
            static segment => segment is GlyphCubicSegment);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_ReportsMissingGlyph()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            0xE000,
            NormalizedOptions);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.MissingGlyph);
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_RespectsCancellation()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            'A',
            NormalizedOptions,
            cts.Token).AsTask());
    }

    [Fact]
    public async Task TypographyGlyphOutlineSource_IsDeterministic()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();

        GlyphOutlineLoadResult first = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            '&',
            NormalizedOptions);

        GlyphOutlineLoadResult second = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            '&',
            NormalizedOptions);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(first.Outline);
        Assert.NotNull(second.Outline);
        Assert.Equal(first.Metrics, second.Metrics);
        Assert.Equal(first.Outline.Bounds, second.Outline.Bounds);
        Assert.Equal(SummarizeContours(first.Outline), SummarizeContours(second.Outline));
    }

    [Fact]
    public async Task GlyphGenerationPipeline_WithTypographyOutlineAndFakeDistanceField_GeneratesField()
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();
        GlyphGenerationPipeline pipeline = new(source, new FakeGlyphDistanceFieldGenerator());
        GlyphKey key = GlyphKey.FromCodepoint(TypographyFixtureFont.Face, 'A', 32);
        MsdfGenerationSettings settings = new(DistanceFieldKind.Msdf, 6, 5, 4, 1, "proof", 1);

        GlyphGenerationResult first = await pipeline.GenerateAsync(key, NormalizedOptions, settings);
        GlyphGenerationResult second = await pipeline.GenerateAsync(key, NormalizedOptions, settings);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(first.DistanceField);
        Assert.NotNull(second.DistanceField);
        Assert.Equal(first.Outline!.Metrics, second.Outline!.Metrics);
        Assert.Equal(first.DistanceField.Data.ToArray(), second.DistanceField.Data.ToArray());
    }

    private static string SummarizeContours(GlyphOutline outline)
    {
        return string.Join(
            ";",
            outline.Contours.Select(static contour =>
                string.Concat(contour.Segments.Select(static segment => segment switch
                {
                    GlyphLineSegment => "L",
                    GlyphQuadraticSegment => "Q",
                    GlyphCubicSegment => "C",
                    _ => "?",
                })))) + ";";
    }

    private static IEnumerable<GlyphPoint> EnumeratePoints(GlyphOutline outline)
    {
        foreach (GlyphContour contour in outline.Contours)
        {
            foreach (GlyphOutlineSegment segment in contour.Segments)
            {
                switch (segment)
                {
                    case GlyphLineSegment line:
                        yield return line.P0;
                        yield return line.P1;
                        break;
                    case GlyphQuadraticSegment quadratic:
                        yield return quadratic.P0;
                        yield return quadratic.P1;
                        yield return quadratic.P2;
                        break;
                    case GlyphCubicSegment cubic:
                        yield return cubic.P0;
                        yield return cubic.P1;
                        yield return cubic.P2;
                        yield return cubic.P3;
                        break;
                }
            }
        }
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
    }
}
