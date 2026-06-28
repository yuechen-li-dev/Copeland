using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests.Generation;

public sealed class FakeGlyphOutlineSourceTests
{
    private static readonly FontFaceId Face = new("Fake");

    [Fact]
    public async Task LoadGlyphOutlineAsync_ReturnsDeterministicOutline()
    {
        FakeGlyphOutlineSource source = new();
        GlyphOutlineLoadOptions options = new(12, 0, GlyphHintingMode.None, normalizeToEm: true);

        GlyphOutlineLoadResult first = await source.LoadGlyphOutlineAsync(Face, 'A', options);
        GlyphOutlineLoadResult second = await source.LoadGlyphOutlineAsync(Face, 'A', options);

        Assert.True(first.Success);
        Assert.NotNull(first.Outline);
        Assert.NotNull(second.Outline);
        Assert.Equal(first.Outline.Key, second.Outline.Key);
        Assert.Equal(first.Outline.Metrics, second.Outline.Metrics);
        Assert.Equal(first.Outline.Bounds, second.Outline.Bounds);
        Assert.Equal(first.Outline.Contours.Count, second.Outline.Contours.Count);
        Assert.Equal(
            first.Outline.Contours[0].Segments.Select(static segment => segment.GetType().Name).ToArray(),
            second.Outline.Contours[0].Segments.Select(static segment => segment.GetType().Name).ToArray());
    }

    [Fact]
    public async Task LoadGlyphOutlineAsync_ReturnsMetrics()
    {
        FakeGlyphOutlineSource source = new();
        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            Face,
            'A',
            new GlyphOutlineLoadOptions(14, 0, GlyphHintingMode.Auto, normalizeToEm: false));

        Assert.NotNull(result.Metrics);
        Assert.Equal(result.Outline!.Metrics, result.Metrics);
        Assert.True(result.Metrics.Advance > 0);
    }

    [Fact]
    public async Task LoadGlyphOutlineAsync_SupportsEmptyWhitespaceOutline()
    {
        FakeGlyphOutlineSource source = new();
        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            Face,
            ' ',
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true));

        Assert.True(result.Success);
        Assert.NotNull(result.Outline);
        Assert.Empty(result.Outline.Contours);
        Assert.NotNull(result.Metrics);
    }

    [Fact]
    public async Task LoadGlyphOutlineAsync_ReportsConfiguredMissingGlyph()
    {
        FakeGlyphOutlineSource source = new(['?']);
        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            Face,
            '?',
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true));

        Assert.False(result.Success);
        Assert.Null(result.Outline);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.MissingGlyph);
        Assert.NotNull(result.Metrics);
    }

    [Fact]
    public async Task LoadGlyphOutlineAsync_CanProduceQuadraticAndCubicSegments()
    {
        FakeGlyphOutlineSource source = new();
        GlyphOutlineLoadOptions options = new(12, 0, GlyphHintingMode.None, normalizeToEm: true);

        GlyphOutlineLoadResult quadratic = await source.LoadGlyphOutlineAsync(Face, '~', options);
        GlyphOutlineLoadResult cubic = await source.LoadGlyphOutlineAsync(Face, '&', options);

        Assert.Contains(quadratic.Outline!.Contours.SelectMany(static contour => contour.Segments), static segment => segment is GlyphQuadraticSegment);
        Assert.Contains(cubic.Outline!.Contours.SelectMany(static contour => contour.Segments), static segment => segment is GlyphCubicSegment);
    }

    [Fact]
    public async Task LoadGlyphOutlineAsync_RespectsCancellation()
    {
        FakeGlyphOutlineSource source = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => source.LoadGlyphOutlineAsync(
            Face,
            'A',
            new GlyphOutlineLoadOptions(12, 0, GlyphHintingMode.None, normalizeToEm: true),
            cts.Token).AsTask());
    }
}
