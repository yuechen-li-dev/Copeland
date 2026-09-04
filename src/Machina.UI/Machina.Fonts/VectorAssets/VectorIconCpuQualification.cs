using Copeland.Profile;

namespace Machina.VectorAssets;

public sealed record VectorIconParityMetrics(
    int Size,
    double IntersectionOverUnion,
    double MeanEdgeDistance,
    double MaximumEdgeDistance,
    int DirectInkPixels,
    int MsdfInkPixels);

public static class VectorIconCpuQualification
{
    public static VectorIconParityMetrics Compare(VectorIconMsdfArtifact artifact, int size)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        bool[] direct = RenderDirect(artifact.Shape, size);
        bool[] msdf = RenderMsdf(artifact, size);
        int intersection = 0;
        int union = 0;
        int directInk = 0;
        int msdfInk = 0;
        for (int index = 0; index < direct.Length; index++)
        {
            directInk += direct[index] ? 1 : 0;
            msdfInk += msdf[index] ? 1 : 0;
            intersection += direct[index] && msdf[index] ? 1 : 0;
            union += direct[index] || msdf[index] ? 1 : 0;
        }

        List<(int X, int Y)> directEdges = ExtractEdges(direct, size);
        List<(int X, int Y)> msdfEdges = ExtractEdges(msdf, size);
        List<double> distances = [];
        AppendDistances(directEdges, msdfEdges, size, distances);
        AppendDistances(msdfEdges, directEdges, size, distances);
        return new VectorIconParityMetrics(
            size,
            union == 0 ? 1 : intersection / (double)union,
            distances.Count == 0 ? 0 : distances.Average(),
            distances.Count == 0 ? 0 : distances.Max(),
            directInk,
            msdfInk);
    }

    public static bool[] RenderDirect(VectorShape shape, int size, int supersample = 4)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (size <= 0 || supersample <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        IReadOnlyList<IReadOnlyList<VectorPoint>> contours = shape.Contours.Select(Flatten).ToArray();
        double scale = Math.Min(size / shape.Bounds.Width, size / shape.Bounds.Height);
        double renderedWidth = shape.Bounds.Width * scale;
        double renderedHeight = shape.Bounds.Height * scale;
        double left = (size - renderedWidth) / 2;
        double top = (size - renderedHeight) / 2;
        bool[] result = new bool[size * size];
        int samples = supersample * supersample;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < supersample; sy++)
                {
                    double pixelY = y + ((sy + 0.5) / supersample);
                    double worldY = shape.Bounds.MaxY - ((pixelY - top) / scale);
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        double pixelX = x + ((sx + 0.5) / supersample);
                        double worldX = shape.Bounds.MinX + ((pixelX - left) / scale);
                        if (ContainsNonZero(contours, worldX, worldY))
                        {
                            inside++;
                        }
                    }
                }
                result[(y * size) + x] = inside >= (samples / 2d);
            }
        }
        return result;
    }

    public static bool[] RenderMsdf(VectorIconMsdfArtifact artifact, int size)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        VectorBounds shape = artifact.PlaneBounds;
        double scale = Math.Min(size / shape.Width, size / shape.Height);
        double renderedWidth = shape.Width * scale;
        double renderedHeight = shape.Height * scale;
        double left = (size - renderedWidth) / 2;
        double top = (size - renderedHeight) / 2;
        double fieldScale = scale / artifact.ProjectionScale;
        bool[] result = new bool[size * size];
        for (int y = 0; y < size; y++)
        {
            double worldY = shape.MaxY - (((y + 0.5) - top) / scale);
            double v = (worldY - artifact.FieldBounds.MinY) / artifact.FieldBounds.Height;
            for (int x = 0; x < size; x++)
            {
                double worldX = shape.MinX + (((x + 0.5) - left) / scale);
                double u = (worldX - artifact.FieldBounds.MinX) / artifact.FieldBounds.Width;
                float distance = SampleMedian(artifact, u, v);
                double smoothing = 0.5 / Math.Max(1, artifact.PixelRange * fieldScale);
                double coverage = SmoothStep(0.5 - smoothing, 0.5 + smoothing, distance);
                result[(y * size) + x] = coverage >= 0.5;
            }
        }
        return result;
    }

    private static IReadOnlyList<VectorPoint> Flatten(VectorContour contour)
    {
        List<VectorPoint> points = [];
        foreach (VectorSegment segment in contour.Segments)
        {
            switch (segment)
            {
                case VectorLine line:
                    Append(points, line.P0);
                    Append(points, line.P1);
                    break;
                case VectorQuadratic quadratic:
                    Append(points, quadratic.P0);
                    for (int step = 1; step <= 32; step++)
                    {
                        double t = step / 32d;
                        double s = 1 - t;
                        Append(points, new VectorPoint(
                            (s * s * quadratic.P0.X) + (2 * s * t * quadratic.P1.X) + (t * t * quadratic.P2.X),
                            (s * s * quadratic.P0.Y) + (2 * s * t * quadratic.P1.Y) + (t * t * quadratic.P2.Y)));
                    }
                    break;
                case VectorCubic cubic:
                    Append(points, cubic.P0);
                    for (int step = 1; step <= 32; step++)
                    {
                        double t = step / 32d;
                        double s = 1 - t;
                        Append(points, new VectorPoint(
                            (s * s * s * cubic.P0.X) + (3 * s * s * t * cubic.P1.X) + (3 * s * t * t * cubic.P2.X) + (t * t * t * cubic.P3.X),
                            (s * s * s * cubic.P0.Y) + (3 * s * s * t * cubic.P1.Y) + (3 * s * t * t * cubic.P2.Y) + (t * t * t * cubic.P3.Y)));
                    }
                    break;
            }
        }
        if (points.Count > 1 && points[0] == points[^1])
        {
            points.RemoveAt(points.Count - 1);
        }
        return points;
    }

    private static void Append(List<VectorPoint> points, VectorPoint point)
    {
        if (points.Count == 0 || points[^1] != point)
        {
            points.Add(point);
        }
    }

    private static bool ContainsNonZero(IReadOnlyList<IReadOnlyList<VectorPoint>> contours, double x, double y)
    {
        int winding = 0;
        foreach (IReadOnlyList<VectorPoint> contour in contours)
        {
            for (int index = 0; index < contour.Count; index++)
            {
                VectorPoint start = contour[index];
                VectorPoint end = contour[(index + 1) % contour.Count];
                double side = ((end.X - start.X) * (y - start.Y)) - ((x - start.X) * (end.Y - start.Y));
                if (start.Y <= y && end.Y > y && side > 0)
                {
                    winding++;
                }
                else if (start.Y > y && end.Y <= y && side < 0)
                {
                    winding--;
                }
            }
        }
        return winding != 0;
    }

    private static float SampleMedian(VectorIconMsdfArtifact artifact, double u, double v)
    {
        double sourceX = Math.Clamp((Math.Clamp(u, 0, 1) * artifact.Width) - 0.5, 0, artifact.Width - 1);
        double sourceY = Math.Clamp((Math.Clamp(v, 0, 1) * artifact.Height) - 0.5, 0, artifact.Height - 1);
        int x0 = (int)Math.Floor(sourceX);
        int y0 = (int)Math.Floor(sourceY);
        int x1 = Math.Min(artifact.Width - 1, x0 + 1);
        int y1 = Math.Min(artifact.Height - 1, y0 + 1);
        double tx = sourceX - x0;
        double ty = sourceY - y0;
        float a = MedianAt(artifact, x0, y0);
        float b = MedianAt(artifact, x1, y0);
        float c = MedianAt(artifact, x0, y1);
        float d = MedianAt(artifact, x1, y1);
        return (float)(((a + ((b - a) * tx)) * (1 - ty)) + ((c + ((d - c) * tx)) * ty));
    }

    private static float MedianAt(VectorIconMsdfArtifact artifact, int x, int y)
    {
        int index = ((y * artifact.Width) + x) * 3;
        ReadOnlySpan<float> data = artifact.FieldPixels.Span;
        float a = data[index];
        float b = data[index + 1];
        float c = data[index + 2];
        return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
    }

    private static double SmoothStep(double low, double high, double value)
    {
        double t = Math.Clamp((value - low) / (high - low), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static List<(int X, int Y)> ExtractEdges(bool[] mask, int size)
    {
        List<(int X, int Y)> edges = [];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool value = mask[(y * size) + x];
                if ((x > 0 && mask[(y * size) + x - 1] != value)
                    || (x + 1 < size && mask[(y * size) + x + 1] != value)
                    || (y > 0 && mask[((y - 1) * size) + x] != value)
                    || (y + 1 < size && mask[((y + 1) * size) + x] != value))
                {
                    edges.Add((x, y));
                }
            }
        }
        return edges;
    }

    private static void AppendDistances(
        IReadOnlyList<(int X, int Y)> source,
        IReadOnlyList<(int X, int Y)> target,
        int size,
        List<double> result)
    {
        foreach ((int x, int y) in source)
        {
            double best = size * Math.Sqrt(2);
            foreach ((int targetX, int targetY) in target)
            {
                int dx = x - targetX;
                int dy = y - targetY;
                best = Math.Min(best, Math.Sqrt((dx * dx) + (dy * dy)));
            }
            result.Add(best);
        }
    }
}
