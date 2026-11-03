using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record NativeMeshCluster3D(uint FirstIndex, uint IndexCount, Vector4 Sphere, Vector4 Cone);

/// <summary>Owned, deterministic contiguous clustering. Original triangle/index order is preserved.</summary>
public static class NativeMeshClusters3D
{
    public const int TrianglesPerCluster = 128;

    public static ImmutableArray<NativeMeshCluster3D> Build(NativeMesh3D mesh)
    {
        var clusters = ImmutableArray.CreateBuilder<NativeMeshCluster3D>();
        for (int first = 0; first < mesh.Indices.Length; first += TrianglesPerCluster * 3)
        {
            int count = Math.Min(TrianglesPerCluster * 3, mesh.Indices.Length - first);
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            var normals = new List<Vector3>();
            for (int index = first; index < first + count; index++)
            {
                Vector3 point = mesh.Vertices[(int)mesh.Indices[index]].Position;
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
            Vector3 center = (minimum + maximum) * .5f;
            float radius = 0;
            Vector3 axis = Vector3.Zero;
            for (int index = first; index < first + count; index++)
            {
                radius = Math.Max(radius, Vector3.Distance(center, mesh.Vertices[(int)mesh.Indices[index]].Position));
                if ((index - first) % 3 == 0)
                {
                    Vector3 a = mesh.Vertices[(int)mesh.Indices[index]].Position;
                    Vector3 b = mesh.Vertices[(int)mesh.Indices[index + 1]].Position;
                    Vector3 c = mesh.Vertices[(int)mesh.Indices[index + 2]].Position;
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.LengthSquared() > 1e-20f)
                    {
                        Vector3 normal = Vector3.Normalize(cross);
                        axis += normal;
                        normals.Add(normal);
                    }
                }
            }
            Vector4 cone = new(0, 0, 0, -2);
            if (axis.LengthSquared() > 1e-12f && normals.Count > 0)
            {
                axis = Vector3.Normalize(axis);
                float cosine = normals.Min(normal => Vector3.Dot(axis, normal));
                if (cosine > 0) cone = new(axis, -MathF.Sqrt(Math.Max(0, 1 - cosine * cosine)));
            }
            clusters.Add(new((uint)first, (uint)count, new(center, radius + Math.Max(1e-5f, radius * 1e-5f)), cone));
        }
        return clusters.ToImmutable();
    }

    internal static Vector4 TransformSphere(Vector4 sphere, Matrix4x4 matrix)
    {
        Vector3 center = Vector3.Transform(new(sphere.X, sphere.Y, sphere.Z), matrix);
        // sqrt(||A||1 ||A||infinity) bounds the spectral norm, including shear and nonuniform scale.
        float columns = Math.Max(Math.Abs(matrix.M11) + Math.Abs(matrix.M21) + Math.Abs(matrix.M31),
            Math.Max(Math.Abs(matrix.M12) + Math.Abs(matrix.M22) + Math.Abs(matrix.M32),
                Math.Abs(matrix.M13) + Math.Abs(matrix.M23) + Math.Abs(matrix.M33)));
        float rows = Math.Max(Math.Abs(matrix.M11) + Math.Abs(matrix.M12) + Math.Abs(matrix.M13),
            Math.Max(Math.Abs(matrix.M21) + Math.Abs(matrix.M22) + Math.Abs(matrix.M23),
                Math.Abs(matrix.M31) + Math.Abs(matrix.M32) + Math.Abs(matrix.M33)));
        return new(center, sphere.W * MathF.Sqrt(columns * rows) + 1e-5f);
    }
}
