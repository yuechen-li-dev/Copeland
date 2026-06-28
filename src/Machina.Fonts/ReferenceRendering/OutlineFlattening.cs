using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record OutlineFlatteningOptions(int CurveSubdivisionCount = 24)
{
    public OutlineFlatteningOptions Validate()
    {
        if (CurveSubdivisionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CurveSubdivisionCount));
        }

        return this;
    }
}

public sealed record FlattenedGlyphContour(IReadOnlyList<GlyphPoint> Points);

public sealed record FlattenedGlyphOutline(
    GlyphKey Key,
    GlyphBounds Bounds,
    IReadOnlyList<FlattenedGlyphContour> Contours);

public static class OutlineFlattening
{
    public static FlattenedGlyphOutline FlattenOutline(
        GlyphOutline outline,
        OutlineFlatteningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(outline);

        OutlineFlatteningOptions validated = (options ?? new OutlineFlatteningOptions()).Validate();
        List<FlattenedGlyphContour> contours = new(outline.Contours.Count);

        foreach (GlyphContour contour in outline.Contours)
        {
            contours.Add(FlattenContour(contour, validated));
        }

        return new FlattenedGlyphOutline(outline.Key, outline.Bounds, contours);
    }

    public static FlattenedGlyphContour FlattenContour(
        GlyphContour contour,
        OutlineFlatteningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contour);

        OutlineFlatteningOptions validated = (options ?? new OutlineFlatteningOptions()).Validate();
        List<GlyphPoint> points = [];

        foreach (GlyphOutlineSegment segment in contour.Segments)
        {
            switch (segment)
            {
                case GlyphLineSegment line:
                    AppendPoint(points, line.P0);
                    AppendPoint(points, line.P1);
                    break;

                case GlyphQuadraticSegment quadratic:
                    AppendPoint(points, quadratic.P0);
                    AppendCurvePoints(
                        points,
                        validated.CurveSubdivisionCount,
                        t => EvaluateQuadratic(quadratic, t));
                    break;

                case GlyphCubicSegment cubic:
                    AppendPoint(points, cubic.P0);
                    AppendCurvePoints(
                        points,
                        validated.CurveSubdivisionCount,
                        t => EvaluateCubic(cubic, t));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported outline segment '{segment.GetType().Name}'.");
            }
        }

        if (points.Count > 1 && PointsEqual(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return new FlattenedGlyphContour(points);
    }

    private static void AppendCurvePoints(
        List<GlyphPoint> points,
        int subdivisionCount,
        Func<double, GlyphPoint> evaluator)
    {
        for (int step = 1; step <= subdivisionCount; step++)
        {
            double t = step / (double)subdivisionCount;
            AppendPoint(points, evaluator(t));
        }
    }

    private static GlyphPoint EvaluateQuadratic(GlyphQuadraticSegment segment, double t)
    {
        double oneMinusT = 1d - t;
        double x =
            (oneMinusT * oneMinusT * segment.P0.X) +
            (2d * oneMinusT * t * segment.P1.X) +
            (t * t * segment.P2.X);
        double y =
            (oneMinusT * oneMinusT * segment.P0.Y) +
            (2d * oneMinusT * t * segment.P1.Y) +
            (t * t * segment.P2.Y);
        return new GlyphPoint(x, y);
    }

    private static GlyphPoint EvaluateCubic(GlyphCubicSegment segment, double t)
    {
        double oneMinusT = 1d - t;
        double x =
            (oneMinusT * oneMinusT * oneMinusT * segment.P0.X) +
            (3d * oneMinusT * oneMinusT * t * segment.P1.X) +
            (3d * oneMinusT * t * t * segment.P2.X) +
            (t * t * t * segment.P3.X);
        double y =
            (oneMinusT * oneMinusT * oneMinusT * segment.P0.Y) +
            (3d * oneMinusT * oneMinusT * t * segment.P1.Y) +
            (3d * oneMinusT * t * t * segment.P2.Y) +
            (t * t * t * segment.P3.Y);
        return new GlyphPoint(x, y);
    }

    private static void AppendPoint(List<GlyphPoint> points, GlyphPoint point)
    {
        if (points.Count == 0 || !PointsEqual(points[^1], point))
        {
            points.Add(point);
        }
    }

    private static bool PointsEqual(GlyphPoint left, GlyphPoint right)
    {
        return left.X == right.X && left.Y == right.Y;
    }
}
