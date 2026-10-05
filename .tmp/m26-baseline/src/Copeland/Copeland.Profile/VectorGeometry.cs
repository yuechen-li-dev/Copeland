using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.Profile;

public enum VectorFillRule
{
    NonZero,
}

public readonly record struct VectorPoint
{
    public VectorPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Vector coordinates must be finite.");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

public abstract record VectorSegment;

public enum VectorContourRole
{
    Auto,
    Outer,
    Hole,
}

public sealed record VectorLine(VectorPoint P0, VectorPoint P1) : VectorSegment;

public sealed record VectorQuadratic(VectorPoint P0, VectorPoint P1, VectorPoint P2) : VectorSegment;

public sealed record VectorCubic(VectorPoint P0, VectorPoint P1, VectorPoint P2, VectorPoint P3) : VectorSegment;

public sealed record VectorContour
{
    public VectorContour(IReadOnlyList<VectorSegment> segments, VectorContourRole role = VectorContourRole.Auto)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.Where(static segment => !IsExactlyZeroLength(segment)).ToArray();
        Role = role;
    }

    public IReadOnlyList<VectorSegment> Segments { get; }

    public VectorContourRole Role { get; }

    private static bool IsExactlyZeroLength(VectorSegment segment)
    {
        return segment switch
        {
            VectorLine line => line.P0 == line.P1,
            VectorQuadratic quadratic => quadratic.P0 == quadratic.P1 && quadratic.P1 == quadratic.P2,
            VectorCubic cubic => cubic.P0 == cubic.P1 && cubic.P1 == cubic.P2 && cubic.P2 == cubic.P3,
            _ => throw new InvalidOperationException($"Unknown vector segment '{segment.GetType().Name}'."),
        };
    }
}

public readonly record struct VectorBounds
{
    public VectorBounds(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
        {
            throw new ArgumentOutOfRangeException(nameof(minX), "Vector bounds must be finite.");
        }
        if (maxX <= minX || maxY <= minY)
        {
            throw new ArgumentException("Vector bounds must have positive width and height.");
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;
}

public sealed record VectorShape
{
    public VectorShape(IReadOnlyList<VectorContour> contours, VectorFillRule fillRule = VectorFillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(contours);
        if (fillRule != VectorFillRule.NonZero)
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule), "M5 supports the non-zero fill law only.");
        }

        VectorContour[] sanitized = contours.Where(static contour => contour.Segments.Count > 0).ToArray();
        Contours = NormalizeOrientations(sanitized);
        if (Contours.Count == 0)
        {
            throw new ArgumentException("Vector geometry must contain at least one non-degenerate contour.", nameof(contours));
        }

        FillRule = fillRule;
        Bounds = CalculateBounds(Contours);
        NormalizedGeometryHash = CalculateHash(Contours, fillRule);
    }

    public IReadOnlyList<VectorContour> Contours { get; }

    public VectorFillRule FillRule { get; }

    public VectorBounds Bounds { get; }

    public string NormalizedGeometryHash { get; }

    private static IReadOnlyList<VectorContour> NormalizeOrientations(IReadOnlyList<VectorContour> contours)
    {
        IReadOnlyList<VectorPoint>[] flattened = contours.Select(Flatten).ToArray();
        double[] absoluteAreas = flattened.Select(points => Math.Abs(SignedArea(points))).ToArray();
        VectorContour[] normalized = new VectorContour[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            VectorPoint sample = flattened[index][0];
            int depth = 0;
            for (int other = 0; other < contours.Count; other++)
            {
                if (other != index
                    && absoluteAreas[other] > absoluteAreas[index]
                    && ContainsEvenOdd(flattened[other], sample))
                {
                    depth++;
                }
            }
            // MSDF-Sharp's upward-Y convention treats clockwise outer contours as filled.
            // Alternate direction by nesting depth so non-zero holes are canonical.
            bool shouldBePositive = contours[index].Role switch
            {
                VectorContourRole.Outer => false,
                VectorContourRole.Hole => true,
                _ => depth % 2 != 0,
            };
            bool isPositive = SignedArea(flattened[index]) > 0;
            normalized[index] = shouldBePositive == isPositive ? contours[index] : Reverse(contours[index]);
        }
        return normalized;
    }

    private static VectorContour Reverse(VectorContour contour)
    {
        return new VectorContour(contour.Segments.Reverse().Select<VectorSegment, VectorSegment>(segment => segment switch
        {
            VectorLine line => new VectorLine(line.P1, line.P0),
            VectorQuadratic quadratic => new VectorQuadratic(quadratic.P2, quadratic.P1, quadratic.P0),
            VectorCubic cubic => new VectorCubic(cubic.P3, cubic.P2, cubic.P1, cubic.P0),
            _ => throw new InvalidOperationException(),
        }).ToArray(), contour.Role);
    }

    private static IReadOnlyList<VectorPoint> Flatten(VectorContour contour)
    {
        List<VectorPoint> result = [];
        foreach (VectorSegment segment in contour.Segments)
        {
            VectorPoint start = segment switch
            {
                VectorLine line => line.P0,
                VectorQuadratic quadratic => quadratic.P0,
                VectorCubic cubic => cubic.P0,
                _ => throw new InvalidOperationException(),
            };
            if (result.Count == 0 || result[^1] != start)
            {
                result.Add(start);
            }
            for (int step = 1; step <= (segment is VectorLine ? 1 : 24); step++)
            {
                double t = step / (double)(segment is VectorLine ? 1 : 24);
                double s = 1 - t;
                result.Add(segment switch
                {
                    VectorLine line => line.P1,
                    VectorQuadratic quadratic => new VectorPoint(
                        (s * s * quadratic.P0.X) + (2 * s * t * quadratic.P1.X) + (t * t * quadratic.P2.X),
                        (s * s * quadratic.P0.Y) + (2 * s * t * quadratic.P1.Y) + (t * t * quadratic.P2.Y)),
                    VectorCubic cubic => new VectorPoint(
                        (s * s * s * cubic.P0.X) + (3 * s * s * t * cubic.P1.X) + (3 * s * t * t * cubic.P2.X) + (t * t * t * cubic.P3.X),
                        (s * s * s * cubic.P0.Y) + (3 * s * s * t * cubic.P1.Y) + (3 * s * t * t * cubic.P2.Y) + (t * t * t * cubic.P3.Y)),
                    _ => throw new InvalidOperationException(),
                });
            }
        }
        if (result.Count > 1 && result[0] == result[^1])
        {
            result.RemoveAt(result.Count - 1);
        }
        return result;
    }

    private static double SignedArea(IReadOnlyList<VectorPoint> contour)
    {
        double area = 0;
        for (int index = 0; index < contour.Count; index++)
        {
            VectorPoint left = contour[index];
            VectorPoint right = contour[(index + 1) % contour.Count];
            area += (left.X * right.Y) - (right.X * left.Y);
        }
        return area / 2;
    }

    private static bool ContainsEvenOdd(IReadOnlyList<VectorPoint> contour, VectorPoint point)
    {
        bool inside = false;
        for (int index = 0; index < contour.Count; index++)
        {
            VectorPoint left = contour[index];
            VectorPoint right = contour[(index + 1) % contour.Count];
            if (((left.Y > point.Y) != (right.Y > point.Y))
                && point.X < (((right.X - left.X) * (point.Y - left.Y) / (right.Y - left.Y)) + left.X))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static VectorBounds CalculateBounds(IReadOnlyList<VectorContour> contours)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach (VectorPoint point in EnumeratePoints(contours))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new VectorBounds(minX, minY, maxX, maxY);
    }

    private static string CalculateHash(IReadOnlyList<VectorContour> contours, VectorFillRule fillRule)
    {
        StringBuilder canonical = new();
        canonical.Append("vector-shape-v1|").Append(fillRule).Append('|');
        foreach (VectorContour contour in contours)
        {
            canonical.Append("contour|");
            if (contour.Role != VectorContourRole.Auto)
            {
                canonical.Append(contour.Role).Append('|');
            }
            foreach (VectorSegment segment in contour.Segments)
            {
                switch (segment)
                {
                    case VectorLine line:
                        Append(canonical, "L", line.P0, line.P1);
                        break;
                    case VectorQuadratic quadratic:
                        Append(canonical, "Q", quadratic.P0, quadratic.P1, quadratic.P2);
                        break;
                    case VectorCubic cubic:
                        Append(canonical, "C", cubic.P0, cubic.P1, cubic.P2, cubic.P3);
                        break;
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder target, string kind, params VectorPoint[] points)
    {
        target.Append(kind);
        foreach (VectorPoint point in points)
        {
            target.Append('|').Append(point.X.ToString("R", CultureInfo.InvariantCulture));
            target.Append(',').Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
        }
        target.Append(';');
    }

    private static IEnumerable<VectorPoint> EnumeratePoints(IReadOnlyList<VectorContour> contours)
    {
        foreach (VectorContour contour in contours)
        {
            foreach (VectorSegment segment in contour.Segments)
            {
                switch (segment)
                {
                    case VectorLine line:
                        yield return line.P0;
                        yield return line.P1;
                        break;
                    case VectorQuadratic quadratic:
                        yield return quadratic.P0;
                        yield return quadratic.P1;
                        yield return quadratic.P2;
                        break;
                    case VectorCubic cubic:
                        yield return cubic.P0;
                        yield return cubic.P1;
                        yield return cubic.P2;
                        yield return cubic.P3;
                        break;
                }
            }
        }
    }
}
