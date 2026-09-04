using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests.Generation;

public sealed class GlyphOutlineRecordsTests
{
    [Fact]
    public void GlyphPoint_RejectsNonFiniteCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphPoint(0, double.PositiveInfinity));
    }

    [Fact]
    public void GlyphBounds_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphBounds(1, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphBounds(0, 2, 1, 1));
    }

    [Fact]
    public void GlyphContour_RejectsNullSegments()
    {
        GlyphOutlineSegment?[] segments = [new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(1, 1)), null];
        Assert.Throws<ArgumentException>(() => new GlyphContour(segments!));
    }

    [Fact]
    public void GlyphOutline_RejectsNullMetricsOrContours()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12);
        GlyphBounds bounds = new(0, 0, 10, 10);
        GlyphContour contour = new([new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(1, 1))]);

        Assert.Throws<ArgumentNullException>(() => new GlyphOutline(key, null!, bounds, [contour]));
        Assert.Throws<ArgumentNullException>(() => new GlyphOutline(key, new GlyphMetrics(8, 0, 10, 8, 12), bounds, null!));
    }

    [Fact]
    public void GlyphSegments_AcceptLineQuadraticCubic()
    {
        GlyphLineSegment line = new(new GlyphPoint(0, 0), new GlyphPoint(1, 1));
        GlyphQuadraticSegment quadratic = new(new GlyphPoint(0, 0), new GlyphPoint(1, 2), new GlyphPoint(2, 0));
        GlyphCubicSegment cubic = new(new GlyphPoint(0, 0), new GlyphPoint(1, 2), new GlyphPoint(2, 2), new GlyphPoint(3, 0));

        Assert.Equal(new GlyphPoint(1, 1), line.P1);
        Assert.Equal(new GlyphPoint(2, 0), quadratic.P2);
        Assert.Equal(new GlyphPoint(3, 0), cubic.P3);
    }

    [Fact]
    public void GlyphOutlineFingerprint_IsDeterministicAndGeometrySensitive()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 12);
        GlyphMetrics metrics = new(8, 0, 10, 8, 12);
        GlyphBounds bounds = new(0, 0, 10, 10);
        GlyphOutline first = new(key, metrics, bounds,
        [
            new GlyphContour([new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(1, 1))]),
        ]);
        GlyphOutline second = new(key, metrics, bounds,
        [
            new GlyphContour([new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(1, 2))]),
        ]);

        Assert.Equal(GlyphOutlineFingerprint.ComputeSha256(first), GlyphOutlineFingerprint.ComputeSha256(first));
        Assert.NotEqual(GlyphOutlineFingerprint.ComputeSha256(first), GlyphOutlineFingerprint.ComputeSha256(second));
    }
}
