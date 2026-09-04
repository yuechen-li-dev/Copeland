using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record OutlineTransformFacts(
    double FontUnitsScale,
    double LocalOffsetX,
    double LocalOffsetY,
    double GlyphOriginX,
    double GlyphOriginY,
    double BaselineY,
    string YAxisLaw);

public sealed record PositionedGlyphOutline(
    ushort GlyphId,
    MachinaTextSpan SourceSpan,
    IReadOnlyList<GlyphContour> Contours,
    MachinaPlaneBounds Bounds,
    OutlineTransformFacts Transform);

public sealed record OutlineFit(
    double ScaleX,
    double ScaleY,
    double TranslateX,
    double TranslateY,
    double RootMeanSquareResidual,
    double MaximumResidual);

public sealed record OutlineComparisonResult(
    double SymmetricRootMeanSquareDistance,
    double HausdorffDistance,
    OutlineFit TranslationOnly,
    OutlineFit TranslationAndUniformScale,
    OutlineFit TranslationAndNonUniformScale);

public static class PositionedOutlineGeometry
{
    public static PositionedGlyphOutline FromTypography(
        ushort glyphId,
        MachinaTextSpan sourceSpan,
        GlyphOutline outline,
        double originX,
        double baselineY,
        double fontUnitsScale)
    {
        IReadOnlyList<GlyphContour> transformed = TransformContours(
            outline.Contours,
            point => new GlyphPoint(originX + point.X, baselineY - point.Y));
        MachinaPlaneBounds bounds = ComputeBounds(transformed, originX, baselineY);
        return new PositionedGlyphOutline(
            glyphId,
            sourceSpan,
            transformed,
            bounds,
            new OutlineTransformFacts(fontUnitsScale, 0d, 0d, originX, baselineY, baselineY, "comparisonY = baselineY - fontY"));
    }

    public static PositionedGlyphOutline FromComparisonSpace(
        ushort glyphId,
        MachinaTextSpan sourceSpan,
        IReadOnlyList<GlyphContour> contours,
        double fontUnitsScale,
        double offsetX,
        double offsetY,
        double originX,
        double originY,
        double baselineY,
        string yAxisLaw)
    {
        MachinaPlaneBounds bounds = ComputeBounds(contours, originX, baselineY);
        return new PositionedGlyphOutline(
            glyphId,
            sourceSpan,
            contours,
            bounds,
            new OutlineTransformFacts(fontUnitsScale, offsetX, offsetY, originX, originY, baselineY, yAxisLaw));
    }

    public static OutlineComparisonResult Compare(
        PositionedGlyphOutline reference,
        PositionedGlyphOutline actual,
        int samplesPerCurve = 12)
    {
        GlyphPoint[] referencePoints = Sample(reference.Contours, samplesPerCurve);
        GlyphPoint[] actualPoints = Sample(actual.Contours, samplesPerCurve);
        if (referencePoints.Length == 0 || actualPoints.Length == 0)
        {
            return new OutlineComparisonResult(0d, 0d, IdentityFit(), IdentityFit(), IdentityFit());
        }

        (double rms, double maximum) = SymmetricNearestDistance(referencePoints, actualPoints);
        OutlineFit translation = FitTranslation(referencePoints, actualPoints);
        OutlineFit uniform = FitScale(referencePoints, actualPoints, uniform: true);
        OutlineFit nonUniform = FitScale(referencePoints, actualPoints, uniform: false);
        return new OutlineComparisonResult(rms, maximum, translation, uniform, nonUniform);
    }

    public static MachinaPlaneBounds ComputeBounds(
        IReadOnlyList<GlyphContour> contours,
        double emptyX = 0d,
        double emptyY = 0d)
    {
        GlyphPoint[] points = Sample(contours, 8);
        if (points.Length == 0)
        {
            return new MachinaPlaneBounds(emptyX, emptyY, emptyX, emptyY);
        }

        return new MachinaPlaneBounds(
            points.Min(static point => point.X),
            points.Min(static point => point.Y),
            points.Max(static point => point.X),
            points.Max(static point => point.Y));
    }

    private static IReadOnlyList<GlyphContour> TransformContours(
        IReadOnlyList<GlyphContour> contours,
        Func<GlyphPoint, GlyphPoint> transform)
    {
        return contours.Select(contour => new GlyphContour(contour.Segments.Select(segment => (GlyphOutlineSegment)(segment switch
        {
            GlyphLineSegment line => new GlyphLineSegment(transform(line.P0), transform(line.P1)),
            GlyphQuadraticSegment quadratic => new GlyphQuadraticSegment(
                transform(quadratic.P0),
                transform(quadratic.P1),
                transform(quadratic.P2)),
            GlyphCubicSegment cubic => new GlyphCubicSegment(
                transform(cubic.P0),
                transform(cubic.P1),
                transform(cubic.P2),
                transform(cubic.P3)),
            _ => throw new InvalidOperationException($"Unsupported outline segment {segment.GetType().Name}."),
        })).ToArray())).ToArray();
    }

    private static GlyphPoint[] Sample(IReadOnlyList<GlyphContour> contours, int samplesPerCurve)
    {
        List<GlyphPoint> points = [];
        foreach (GlyphContour contour in contours)
        {
            foreach (GlyphOutlineSegment segment in contour.Segments)
            {
                int count = segment is GlyphLineSegment ? 1 : samplesPerCurve;
                for (int index = 0; index <= count; index++)
                {
                    double t = index / (double)count;
                    points.Add(Evaluate(segment, t));
                }
            }
        }

        return [.. points];
    }

    private static GlyphPoint Evaluate(GlyphOutlineSegment segment, double t)
    {
        double u = 1d - t;
        return segment switch
        {
            GlyphLineSegment line => new GlyphPoint(
                (u * line.P0.X) + (t * line.P1.X),
                (u * line.P0.Y) + (t * line.P1.Y)),
            GlyphQuadraticSegment quadratic => new GlyphPoint(
                (u * u * quadratic.P0.X) + (2d * u * t * quadratic.P1.X) + (t * t * quadratic.P2.X),
                (u * u * quadratic.P0.Y) + (2d * u * t * quadratic.P1.Y) + (t * t * quadratic.P2.Y)),
            GlyphCubicSegment cubic => new GlyphPoint(
                (u * u * u * cubic.P0.X) + (3d * u * u * t * cubic.P1.X) + (3d * u * t * t * cubic.P2.X) + (t * t * t * cubic.P3.X),
                (u * u * u * cubic.P0.Y) + (3d * u * u * t * cubic.P1.Y) + (3d * u * t * t * cubic.P2.Y) + (t * t * t * cubic.P3.Y)),
            _ => throw new InvalidOperationException($"Unsupported outline segment {segment.GetType().Name}."),
        };
    }

    private static OutlineFit FitTranslation(GlyphPoint[] reference, GlyphPoint[] actual)
    {
        GlyphPoint referenceCenter = Center(reference);
        GlyphPoint actualCenter = Center(actual);
        return EvaluateFit(reference, actual, 1d, 1d, referenceCenter.X - actualCenter.X, referenceCenter.Y - actualCenter.Y);
    }

    private static OutlineFit FitScale(GlyphPoint[] reference, GlyphPoint[] actual, bool uniform)
    {
        GlyphPoint referenceCenter = Center(reference);
        GlyphPoint actualCenter = Center(actual);
        double scaleX = LeastSquaresScale(reference, actual, referenceCenter, actualCenter, useX: true);
        double scaleY = LeastSquaresScale(reference, actual, referenceCenter, actualCenter, useX: false);
        if (uniform)
        {
            double numerator = 0d;
            double denominator = 0d;
            int count = Math.Min(reference.Length, actual.Length);
            for (int index = 0; index < count; index++)
            {
                double ax = actual[index].X - actualCenter.X;
                double ay = actual[index].Y - actualCenter.Y;
                numerator += (ax * (reference[index].X - referenceCenter.X)) + (ay * (reference[index].Y - referenceCenter.Y));
                denominator += (ax * ax) + (ay * ay);
            }

            scaleX = scaleY = denominator == 0d ? 1d : numerator / denominator;
        }

        double dx = referenceCenter.X - (actualCenter.X * scaleX);
        double dy = referenceCenter.Y - (actualCenter.Y * scaleY);
        return EvaluateFit(reference, actual, scaleX, scaleY, dx, dy);
    }

    private static double LeastSquaresScale(
        GlyphPoint[] reference,
        GlyphPoint[] actual,
        GlyphPoint referenceCenter,
        GlyphPoint actualCenter,
        bool useX)
    {
        double numerator = 0d;
        double denominator = 0d;
        int count = Math.Min(reference.Length, actual.Length);
        for (int index = 0; index < count; index++)
        {
            double a = useX ? actual[index].X - actualCenter.X : actual[index].Y - actualCenter.Y;
            double r = useX ? reference[index].X - referenceCenter.X : reference[index].Y - referenceCenter.Y;
            numerator += a * r;
            denominator += a * a;
        }

        return denominator == 0d ? 1d : numerator / denominator;
    }

    private static OutlineFit EvaluateFit(
        GlyphPoint[] reference,
        GlyphPoint[] actual,
        double scaleX,
        double scaleY,
        double dx,
        double dy)
    {
        GlyphPoint[] transformed = actual.Select(point => new GlyphPoint(
            (point.X * scaleX) + dx,
            (point.Y * scaleY) + dy)).ToArray();
        (double rms, double maximum) = SymmetricNearestDistance(reference, transformed);
        return new OutlineFit(scaleX, scaleY, dx, dy, rms, maximum);
    }

    private static (double Rms, double Maximum) SymmetricNearestDistance(GlyphPoint[] left, GlyphPoint[] right)
    {
        double sumSquares = 0d;
        double maximum = 0d;
        int count = 0;
        Accumulate(left, right, ref sumSquares, ref maximum, ref count);
        Accumulate(right, left, ref sumSquares, ref maximum, ref count);
        return (Math.Sqrt(sumSquares / count), maximum);
    }

    private static void Accumulate(
        GlyphPoint[] sources,
        GlyphPoint[] targets,
        ref double sumSquares,
        ref double maximum,
        ref int count)
    {
        foreach (GlyphPoint source in sources)
        {
            double nearestSquared = double.PositiveInfinity;
            foreach (GlyphPoint target in targets)
            {
                double dx = source.X - target.X;
                double dy = source.Y - target.Y;
                nearestSquared = Math.Min(nearestSquared, (dx * dx) + (dy * dy));
            }

            sumSquares += nearestSquared;
            maximum = Math.Max(maximum, Math.Sqrt(nearestSquared));
            count++;
        }
    }

    private static GlyphPoint Center(GlyphPoint[] points)
    {
        return new GlyphPoint(points.Average(static point => point.X), points.Average(static point => point.Y));
    }

    private static OutlineFit IdentityFit()
    {
        return new OutlineFit(1d, 1d, 0d, 0d, 0d, 0d);
    }
}
