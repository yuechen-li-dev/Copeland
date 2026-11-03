using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;

internal static partial class RenderingVisibilityProof
{
    private sealed record OracleTriangle(Double3 A, Double3 B, Double3 C, uint Instance, uint Triangle);

    private static object CheckRayOracle(Native3DScene scene, Matrix4x4 camera,
        NativeVisibilityBufferReadback actual, int minimumHits = 50)
    {
        Require(Matrix4x4.Invert(camera, out var inverse), "Oracle camera is singular.");
        var triangles = new List<OracleTriangle>();
        uint instanceIndex = 0;
        foreach (var instance in scene.Instances.GroupBy(instance => (instance.Mesh, instance.Material)).SelectMany(group => group))
        {
            instanceIndex++;
            var mesh = instance.Mesh;
            for (int triangle = 0; triangle < mesh.Indices.Length / 3; triangle++)
            {
                Double3 Point(int corner) => Double3.From(Vector3.Transform(
                    mesh.Vertices[(int)mesh.Indices[triangle * 3 + corner]].Position, instance.Transform));
                triangles.Add(new(Point(0), Point(1), Point(2), instanceIndex, (uint)triangle));
            }
        }
        int hits = 0;
        int backgrounds = 0;
        int edgeSamples = 0;
        var seen = new HashSet<(uint Instance, uint Triangle, bool Front)>();
        for (int y = 2; y < Height; y += 5)
        {
            for (int x = 2; x < Width; x += 5)
            {
                float nx = (x + .5f) * 2 / Width - 1;
                float ny = (y + .5f) * 2 / Height - 1;
                Double3 origin = Unproject(nx, ny, 0, inverse);
                Double3 direction = Unproject(nx, ny, 1, inverse) - origin;
                OracleTriangle? nearest = null;
                double closest = double.PositiveInfinity;
                double margin = 0;
                bool front = false;
                foreach (var triangle in triangles)
                {
                    if (!Intersect(origin, direction, triangle, out double t, out double edgeMargin, out bool facing)) continue;
                    if (t < 0 || t > 1 || t >= closest) continue;
                    closest = t;
                    nearest = triangle;
                    margin = edgeMargin;
                    front = facing;
                }
                int offset = (y * Width + x) * 2;
                uint instanceId = actual.Ids[offset];
                uint primitive = actual.Ids[offset + 1];
                // Subpixel raster coverage and floating-point ties are separate from identity mapping.
                if (nearest is not null && margin < .025)
                {
                    edgeSamples++;
                    continue;
                }
                if (nearest is null)
                {
                    if (instanceId != 0)
                    {
                        var reported = triangles.Single(triangle => triangle.Instance == instanceId && triangle.Triangle == primitive / 2);
                        if (Intersect(origin, direction, reported, out _, out double edgeMargin, out _, tolerance: .025)
                            && edgeMargin < .025)
                        {
                            edgeSamples++;
                            continue;
                        }
                    }
                    Require(instanceId == 0, $"Ray oracle: unexpected native hit at {x},{y}.");
                    backgrounds++;
                    continue;
                }
                uint identity = nearest.Triangle * 2 + (front ? 0u : 1u);
                Require(instanceId == nearest.Instance && primitive == identity,
                    $"Ray oracle {x},{y}: expected ({nearest.Instance},{identity}), got ({instanceId},{primitive}).");
                seen.Add((instanceId, primitive / 2, front));
                hits++;
            }
        }
        Require(hits >= minimumHits, "Independent ray oracle checked too few triangle interiors.");
        if (triangles.Count > 128)
        {
            Require(seen.Max(hit => hit.Triangle) > 127 && seen.Select(hit => hit.Instance).Distinct().Count() > 1
                && seen.Any(hit => hit.Front) && seen.Any(hit => !hit.Front),
                "Oracle did not cover cluster offsets, multiple instances and both facing directions.");
        }
        return new { InteriorHits = hits, Backgrounds = backgrounds, EdgeSamples = edgeSamples,
            DistinctTriangles = seen.Count, MaximumTriangle = seen.Max(hit => hit.Triangle),
            FrontHits = seen.Count(hit => hit.Front), BackHits = seen.Count(hit => !hit.Front) };
    }

    private static Double3 Unproject(float x, float y, float z, Matrix4x4 inverse)
    {
        Vector4 point = Vector4.Transform(new Vector4(x, y, z, 1), inverse);
        return new(point.X / (double)point.W, point.Y / (double)point.W, point.Z / (double)point.W);
    }

    private static bool Intersect(Double3 origin, Double3 direction, OracleTriangle triangle,
        out double t, out double margin, out bool front, double tolerance = 0)
    {
        Double3 edge1 = triangle.B - triangle.A;
        Double3 edge2 = triangle.C - triangle.A;
        Double3 p = direction.Cross(edge2);
        double determinant = edge1.Dot(p);
        t = margin = 0;
        front = determinant > 0;
        if (Math.Abs(determinant) < 1e-12) return false;
        Double3 distance = origin - triangle.A;
        double u = distance.Dot(p) / determinant;
        Double3 q = distance.Cross(edge1);
        double v = direction.Dot(q) / determinant;
        margin = Math.Min(u, Math.Min(v, 1 - u - v));
        if (margin < -tolerance) return false;
        t = edge2.Dot(q) / determinant;
        return true;
    }

    private readonly record struct Double3(double X, double Y, double Z)
    {
        public static Double3 From(Vector3 value) => new(value.X, value.Y, value.Z);

        public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public double Dot(Double3 other) => X * other.X + Y * other.Y + Z * other.Z;

        public Double3 Cross(Double3 other) => new(Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z, X * other.Y - Y * other.X);
    }
}
