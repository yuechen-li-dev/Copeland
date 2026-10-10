using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;

/// <summary>Independent brute-force qualification oracle. Never used by the live GPU update path.</summary>
internal static class DiffuseProbeReference
{
    internal sealed record Hit(int Primitive, Vector3 Point, Vector3 Normal, float Distance, float B, float C);

    internal static Hit? Trace(IReadOnlyList<DiffuseProbeTriangle> scene, Vector3 origin, Vector3 direction, float range = 32)
    {
        Hit? nearest = null;
        for (int index = 0; index < scene.Count; index++)
        {
            var triangle = scene[index];
            Vector3 edge1 = triangle.B - triangle.A;
            Vector3 edge2 = triangle.C - triangle.A;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (MathF.Abs(determinant) < 1e-8f)
            {
                continue;
            }
            Vector3 offset = origin - triangle.A;
            float b = Vector3.Dot(offset, cross) / determinant;
            Vector3 other = Vector3.Cross(offset, edge1);
            float c = Vector3.Dot(direction, other) / determinant;
            float distance = Vector3.Dot(edge2, other) / determinant;
            if (b < 0 || c < 0 || b + c > 1 || distance < 0 || distance > range
                || nearest is not null && distance >= nearest.Distance - .00001f)
            {
                continue;
            }
            Vector3 normal = triangle.Normal;
            if (Vector3.Dot(normal, direction) > 0)
            {
                normal = -normal;
            }
            nearest = new(index, origin + direction * distance, normal, distance, b, c);
        }
        return nearest;
    }

    internal static Vector3 Radiance(IReadOnlyList<DiffuseProbeTriangle> scene, DiffuseProbeLight light, Hit hit)
    {
        Vector3 delta = light.Position - hit.Point;
        float squared = MathF.Max(delta.LengthSquared(), .01f);
        Vector3 direction = Vector3.Normalize(delta);
        float cosine = MathF.Max(Vector3.Dot(hit.Normal, direction), 0);
        var blocker = Trace(scene, hit.Point + hit.Normal * .002f, direction, delta.Length() - .002f);
        float attenuation = 0;
        if (blocker is null)
        {
            attenuation = cosine * light.Intensity / (MathF.PI * squared);
        }
        var material = scene[hit.Primitive];
        return material.Emission + material.Albedo * light.Color * attenuation;
    }

    internal static Vector3 At(IReadOnlyList<DiffuseProbeTriangle> scene, DiffuseProbeLight light,
        Vector3 point, Vector3 normal, int samples)
    {
        Vector3 sum = Vector3.Zero;
        for (int ray = 0; ray < samples; ray++)
        {
            Vector3 direction = VulkanDiffuseProbeVolume.Direction(ray, samples);
            Hit? hit = Trace(scene, point + normal * .003f, direction);
            if (hit is not null)
            {
                sum += Radiance(scene, light, hit) * MathF.Max(0, Vector3.Dot(normal, direction)) * (4f / samples);
            }
        }
        return sum;
    }

    internal static object CompareAtlas(IReadOnlyList<DiffuseProbeTriangle> scene, DiffuseProbeLight light,
        DiffuseProbeGrid grid, float[] atlas)
    {
        double squared = 0;
        double referenceSquared = 0;
        float maximum = 0;
        int count = 0;
        int hits = 0;
        var cells = new HashSet<(int Primitive, int X, int Y)>();
        for (int probe = 0; probe < grid.Count; probe++)
        {
            Vector3[] radiance = new Vector3[grid.RaysPerProbe];
            for (int ray = 0; ray < grid.RaysPerProbe; ray++)
            {
                var hit = Trace(scene, grid.Position(probe), VulkanDiffuseProbeVolume.Direction(ray, grid.RaysPerProbe));
                if (hit is not null)
                {
                    radiance[ray] = Radiance(scene, light, hit);
                    hits++;
                    cells.Add((hit.Primitive, (int)MathF.Round(hit.B * (grid.SurfaceResolution - 1)),
                        (int)MathF.Round(hit.C * (grid.SurfaceResolution - 1))));
                }
            }
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Vector3 normal = DecodeOcta((x + .5f) / 8, (y + .5f) / 8);
                    Vector3 reference = Vector3.Zero;
                    for (int ray = 0; ray < grid.RaysPerProbe; ray++)
                    {
                        Vector3 direction = VulkanDiffuseProbeVolume.Direction(ray, grid.RaysPerProbe);
                        reference += radiance[ray] * MathF.Max(Vector3.Dot(normal, direction), 0) * (4f / grid.RaysPerProbe);
                    }
                    int offset = (probe * 64 + y * 8 + x) * 4;
                    Vector3 actual = new(atlas[offset], atlas[offset + 1], atlas[offset + 2]);
                    Vector3 error = actual - reference;
                    squared += error.LengthSquared();
                    referenceSquared += reference.LengthSquared();
                    maximum = MathF.Max(maximum, MathF.Max(MathF.Abs(error.X), MathF.Max(MathF.Abs(error.Y), MathF.Abs(error.Z))));
                    count += 3;
                }
            }
        }
        return new
        {
            MatchedRayCount = grid.Count * grid.RaysPerProbe,
            HitRayCount = hits,
            ReferencedSurfaceCells = cells.Count,
            HitReuse = hits / (double)cells.Count,
            Rmse = Math.Sqrt(squared / count),
            RelativeRmse = Math.Sqrt(squared / referenceSquared),
            MaximumChannelError = maximum,
            Note = "Same finite spherical quadrature, exact per-hit Lambertian shading on the independent CPU oracle. Not a converged path trace or GPU speed comparison.",
        };
    }

    private static Vector3 DecodeOcta(float u, float v)
    {
        float x = u * 2 - 1;
        float z = v * 2 - 1;
        float y = 1 - MathF.Abs(x) - MathF.Abs(z);
        if (y < 0)
        {
            float old = x;
            x = (1 - MathF.Abs(z)) * (x < 0 ? -1 : 1);
            z = (1 - MathF.Abs(old)) * (z < 0 ? -1 : 1);
        }
        return Vector3.Normalize(new(x, y, z));
    }
}
