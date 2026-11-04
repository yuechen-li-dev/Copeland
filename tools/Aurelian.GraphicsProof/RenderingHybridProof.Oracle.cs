using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private readonly record struct D3(double X, double Y, double Z)
    {
        public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);
        public double Dot(D3 other) => X * other.X + Y * other.Y + Z * other.Z;
        public D3 Cross(D3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public static D3 From(Vector3 value) => new(value.X, value.Y, value.Z);
    }
    private sealed record OracleObject(SceneInstance3D Instance, uint Id, double[,] Inverse);
    private sealed record OracleHit(double T, D3 World, uint Instance, uint Identity, double Margin, D3 Normal);

    private static object CheckOracle(Native3DScene scene, Matrix4x4 camera, NativeVisibilityBufferReadback ids, float[] depth)
    {
        double[,] inverse = Inverse(camera);
        var objects = scene.Instances.GroupBy(instance => (instance.Mesh, instance.Material)).SelectMany(group => group)
            .Select((instance, index) => new OracleObject(instance, (uint)index + 1, Inverse(instance.Transform))).ToArray();
        int foreground = 0;
        int background = 0;
        int boundaries = 0;
        double maximumDepthError = 0;
        var kinds = new HashSet<AnalyticPrimitiveKind3D>();
        for (int y = 1; y < ids.Height; y += 3)
        {
            for (int x = 1; x < ids.Width; x += 3)
            {
                double nx = (x + .5) * 2 / ids.Width - 1;
                double ny = (y + .5) * 2 / ids.Height - 1;
                D3 start = Project(new(nx, ny, 0), inverse);
                D3 direction = Project(new(nx, ny, 1), inverse) - start;
                OracleHit[] hits = objects.SelectMany(item => Intersections(item, start, direction)).OrderBy(hit => hit.T).ToArray();
                int pixel = checked(y * (int)ids.Width + x);
                uint actualInstance = ids.Ids[pixel * 2];
                uint actualIdentity = ids.Ids[pixel * 2 + 1];
                if (hits.Length == 0)
                {
                    Require(actualInstance == 0, $"Oracle background {x},{y} received instance {actualInstance}.");
                    background++;
                    continue;
                }
                var expected = hits[0];
                if (expected.Margin < .012 || hits.Length > 1 && Math.Abs(expected.T - hits[1].T) < 1e-6)
                {
                    boundaries++;
                    continue;
                }
                Require(actualInstance == expected.Instance && actualIdentity == expected.Identity,
                    $"Oracle {x},{y}: expected {expected.Instance}/{expected.Identity}, received {actualInstance}/{actualIdentity}.");
                double z = expected.World.X * camera.M13 + expected.World.Y * camera.M23 + expected.World.Z * camera.M33 + camera.M43;
                double w = expected.World.X * camera.M14 + expected.World.Y * camera.M24 + expected.World.Z * camera.M34 + camera.M44;
                double error = Math.Abs(depth[pixel] - z / w);
                maximumDepthError = Math.Max(maximumDepthError, error);
                Require(error < 3e-5, $"Oracle depth error {error} at {x},{y}.");
                kinds.Add(objects[(int)expected.Instance - 1].Instance.Mesh.PrimitiveKind);
                foreground++;
            }
        }
        Require(foreground > 100, "Oracle checked too little visible geometry.");
        return new { InteriorHits = foreground, Background = background, BoundarySamples = boundaries,
            MaximumDepthError = maximumDepthError, Kinds = kinds.Select(kind => kind.ToString()).ToArray() };
    }

    private static IEnumerable<OracleHit> Intersections(OracleObject item, D3 worldStart, D3 worldDirection, double minimum = 0)
    {
        D3 origin = Project(worldStart, item.Inverse);
        D3 direction = Project(worldStart + worldDirection, item.Inverse) - origin;
        var candidates = new List<(double T, D3 Normal, uint Identity, double Margin)>();
        var kind = item.Instance.Mesh.PrimitiveKind;
        void Add(double t, D3 normal, uint identity, double margin)
        {
            if (t < minimum || t > 1) return;
            bool front = normal.Dot(direction) < 0;
            if (!front && !item.Instance.Material.DoubleSided) return;
            candidates.Add((t, normal, identity | (front ? 0u : 1u), margin));
        }
        void Sphere(double center, double radius, int hemisphere)
        {
            D3 relative = origin - new D3(0, center, 0);
            double a = direction.Dot(direction);
            double b = 2 * relative.Dot(direction);
            double c = relative.Dot(relative) - radius * radius;
            foreach (double t in Roots(a, b, c))
            {
                D3 normal = relative + direction * t;
                if (hemisphere < 0 && normal.Y > 0 || hemisphere > 0 && normal.Y < 0) continue;
                double margin = Math.Abs(normal.Dot(direction)) / Math.Sqrt(normal.Dot(normal) * direction.Dot(direction));
                Add(t, normal, 0x80000000, margin);
            }
        }
        if (kind == AnalyticPrimitiveKind3D.UnitSphere) Sphere(0, 1, 0);
        else if (kind == AnalyticPrimitiveKind3D.Capsule)
        {
            Sphere(-.5, .5, -1);
            Sphere(.5, .5, 1);
            foreach (double t in Roots(direction.X * direction.X + direction.Z * direction.Z,
                2 * (origin.X * direction.X + origin.Z * direction.Z), origin.X * origin.X + origin.Z * origin.Z - .25))
            {
                D3 point = origin + direction * t;
                if (Math.Abs(point.Y) > .5) continue;
                D3 normal = new(point.X, 0, point.Z);
                Add(t, normal, 0x80000000, Math.Abs(normal.Dot(direction)) / Math.Sqrt(normal.Dot(normal) * direction.Dot(direction)));
            }
        }
        else if (kind == AnalyticPrimitiveKind3D.UnitBox)
        {
            // Independent face-plane tests, rather than the GPU slab algorithm.
            foreach (D3 normal in new[] { new D3(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) })
            {
                double denominator = normal.Dot(direction);
                if (Math.Abs(denominator) < 1e-15) continue;
                double t = (1 - normal.Dot(origin)) / denominator;
                D3 point = origin + direction * t;
                if (Math.Max(Math.Abs(point.X), Math.Max(Math.Abs(point.Y), Math.Abs(point.Z))) > 1 + 1e-10) continue;
                Add(t, normal, 0x80000000, .1);
            }
        }
        else
        {
            var mesh = item.Instance.Mesh;
            for (int triangle = 0; triangle < mesh.Indices.Length / 3; triangle++)
            {
                D3 a = D3.From(mesh.Vertices[(int)mesh.Indices[triangle * 3]].Position);
                D3 b = D3.From(mesh.Vertices[(int)mesh.Indices[triangle * 3 + 1]].Position);
                D3 c = D3.From(mesh.Vertices[(int)mesh.Indices[triangle * 3 + 2]].Position);
                D3 edge1 = b - a;
                D3 edge2 = c - a;
                D3 p = direction.Cross(edge2);
                double determinant = edge1.Dot(p);
                if (Math.Abs(determinant) < 1e-15) continue;
                D3 relative = origin - a;
                double u = relative.Dot(p) / determinant;
                D3 q = relative.Cross(edge1);
                double v = direction.Dot(q) / determinant;
                if (u < 0 || v < 0 || u + v > 1) continue;
                Add(edge2.Dot(q) / determinant, edge1.Cross(edge2), (uint)triangle * 2, Math.Min(u, Math.Min(v, 1 - u - v)));
            }
        }
        return candidates.Select(hit =>
        {
            D3 normal = new(item.Inverse[0, 0] * hit.Normal.X + item.Inverse[0, 1] * hit.Normal.Y + item.Inverse[0, 2] * hit.Normal.Z,
                item.Inverse[1, 0] * hit.Normal.X + item.Inverse[1, 1] * hit.Normal.Y + item.Inverse[1, 2] * hit.Normal.Z,
                item.Inverse[2, 0] * hit.Normal.X + item.Inverse[2, 1] * hit.Normal.Y + item.Inverse[2, 2] * hit.Normal.Z);
            normal *= 1 / Math.Sqrt(normal.Dot(normal));
            return new OracleHit(hit.T, worldStart + worldDirection * hit.T, item.Id, hit.Identity, hit.Margin, normal);
        });
    }

    private static IEnumerable<double> Roots(double a, double b, double c)
    {
        if (Math.Abs(a) < 1e-20) yield break;
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0) yield break;
        double q = -.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
        double first = q / a;
        double second = q == 0 ? -b / (2 * a) : c / q;
        yield return Math.Min(first, second);
        yield return Math.Max(first, second);
    }

    private static D3 Project(D3 point, double[,] matrix)
    {
        double w = point.X * matrix[0, 3] + point.Y * matrix[1, 3] + point.Z * matrix[2, 3] + matrix[3, 3];
        return new((point.X * matrix[0, 0] + point.Y * matrix[1, 0] + point.Z * matrix[2, 0] + matrix[3, 0]) / w,
            (point.X * matrix[0, 1] + point.Y * matrix[1, 1] + point.Z * matrix[2, 1] + matrix[3, 1]) / w,
            (point.X * matrix[0, 2] + point.Y * matrix[1, 2] + point.Z * matrix[2, 2] + matrix[3, 2]) / w);
    }

    private static double[,] Inverse(Matrix4x4 matrix)
    {
        double[] values = [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];
        var work = new double[4, 8];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++) work[row, column] = values[row * 4 + column];
            work[row, row + 4] = 1;
        }
        for (int column = 0; column < 4; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < 4; row++)
                if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column])) pivot = row;
            for (int index = 0; index < 8; index++) (work[column, index], work[pivot, index]) = (work[pivot, index], work[column, index]);
            double divisor = work[column, column];
            Require(Math.Abs(divisor) > 1e-20, "Singular oracle matrix.");
            for (int index = 0; index < 8; index++) work[column, index] /= divisor;
            for (int row = 0; row < 4; row++)
            {
                if (row == column) continue;
                double factor = work[row, column];
                for (int index = 0; index < 8; index++) work[row, index] -= work[column, index] * factor;
            }
        }
        var result = new double[4, 4];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++) result[row, column] = work[row, column + 4];
        return result;
    }
}
