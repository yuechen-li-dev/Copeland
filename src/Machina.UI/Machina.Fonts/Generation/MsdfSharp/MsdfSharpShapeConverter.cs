using Msdfgen;

namespace Machina.Fonts.Generation.MsdfSharp;

internal static class MsdfSharpShapeConverter
{
    public static MsdfSharpShapeConversion Convert(GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.Contours.Count == 0)
        {
            return new MsdfSharpShapeConversion(
                false,
                null,
                [CreateDiagnostic(
                    outline.Key,
                    FontGenerationDiagnosticCode.EmptyOutline,
                    "Distance-field generation requires at least one contour.")]);
        }

        try
        {
            Shape shape = new();
            shape.SetYAxisOrientation(YAxisOrientation.Upward);

            foreach (GlyphContour sourceContour in outline.Contours)
            {
                if (sourceContour.Segments.Count == 0)
                {
                    continue;
                }

                Contour contour = new();
                foreach (GlyphOutlineSegment sourceSegment in sourceContour.Segments)
                {
                    if (!IsDegenerate(sourceSegment))
                    {
                        contour.AddEdge(ConvertSegment(sourceSegment));
                    }
                }

                if (contour.Edges.Count > 0)
                {
                    shape.AddContour(contour);
                }
            }

            if (shape.Contours.Count == 0 || shape.EdgeCount() == 0)
            {
                return new MsdfSharpShapeConversion(
                    false,
                    null,
                    [CreateDiagnostic(
                        outline.Key,
                        FontGenerationDiagnosticCode.EmptyOutline,
                        "Distance-field generation requires at least one edge.")]);
            }

            return new MsdfSharpShapeConversion(true, shape, []);
        }
        catch (Exception ex)
        {
            return new MsdfSharpShapeConversion(
                false,
                null,
                [CreateDiagnostic(
                    outline.Key,
                    FontGenerationDiagnosticCode.DistanceFieldGenerationFailed,
                    $"Failed to convert Machina outline to MSDF shape: {ex.Message}")]);
        }
    }

    private static bool IsDegenerate(GlyphOutlineSegment segment)
    {
        return segment switch
        {
            GlyphLineSegment line => PointsEqual(line.P0, line.P1),
            GlyphQuadraticSegment quadratic =>
                PointsEqual(quadratic.P0, quadratic.P1)
                && PointsEqual(quadratic.P1, quadratic.P2),
            GlyphCubicSegment cubic =>
                PointsEqual(cubic.P0, cubic.P1)
                && PointsEqual(cubic.P1, cubic.P2)
                && PointsEqual(cubic.P2, cubic.P3),
            _ => false,
        };
    }

    private static bool PointsEqual(GlyphPoint left, GlyphPoint right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static EdgeSegment ConvertSegment(GlyphOutlineSegment sourceSegment)
    {
        return sourceSegment switch
        {
            GlyphLineSegment line => new LinearSegment(
                ToVector(line.P0),
                ToVector(line.P1),
                EdgeColor.WHITE),
            GlyphQuadraticSegment quadratic => new QuadraticSegment(
                ToVector(quadratic.P0),
                ToVector(quadratic.P1),
                ToVector(quadratic.P2),
                EdgeColor.WHITE),
            GlyphCubicSegment cubic => new CubicSegment(
                ToVector(cubic.P0),
                ToVector(cubic.P1),
                ToVector(cubic.P2),
                ToVector(cubic.P3),
                EdgeColor.WHITE),
            _ => throw new InvalidOperationException($"Unsupported outline segment type '{sourceSegment.GetType().Name}'."),
        };
    }

    private static Vector2 ToVector(GlyphPoint point)
    {
        return new Vector2(point.X, point.Y);
    }

    private static FontGenerationDiagnostic CreateDiagnostic(
        GlyphKey key,
        FontGenerationDiagnosticCode code,
        string message)
    {
        return new FontGenerationDiagnostic(
            FontGenerationDiagnosticSeverity.Error,
            code,
            message,
            key);
    }
}
