using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private sealed record CpuSecondary(Vector2 Uv, Vector3 Color, Vector3 Normal, Vector3 Emission,
        float Roughness, float Metallic, float Occlusion);

    // Independent qualification math. Runtime shading never calls this sampler,
    // interpolator or BRDF. Visibility comes from the qualified closest-hit path.
    private static CpuSecondary SecondaryOracle(SceneInstance3D instance, Vector3 world, Vector3 geometric, bool frontFace)
    {
        Matrix4x4.Invert(instance.Transform, out var inverse);
        Vector3 point = Vector3.Transform(world, inverse);
        Matrix4x4 normals = Matrix4x4.Transpose(inverse);
        var mesh = instance.Mesh;
        if (mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
        {
            Vector3 localNormal;
            if (mesh.PrimitiveKind == AnalyticPrimitiveKind3D.UnitSphere)
            {
                localNormal = Vector3.Normalize(point);
            }
            else
            {
                Vector3 absolute = Vector3.Abs(point);
                if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
                {
                    localNormal = new(MathF.Sign(point.X), 0, 0);
                }
                else if (absolute.Y >= absolute.Z)
                {
                    localNormal = new(0, MathF.Sign(point.Y), 0);
                }
                else
                {
                    localNormal = new(0, 0, MathF.Sign(point.Z));
                }
            }
            Vector2 uv = new(point.X, point.Y);
            Vector3 localTangent = Vector3.Cross(Vector3.UnitY, localNormal);
            float sign = 1;
            if (localNormal.Z < 0)
            {
                localTangent = -localTangent;
                sign = -1;
            }
            if (mesh.PrimitiveKind == AnalyticPrimitiveKind3D.UnitBox)
            {
                localTangent = Vector3.UnitX;
                if (MathF.Abs(localNormal.X) > .5f)
                {
                    localTangent = -Vector3.UnitZ;
                    uv = new(-point.Z, point.Y);
                    sign = localNormal.X;
                }
                if (MathF.Abs(localNormal.Y) > .5f)
                {
                    uv = new(point.X, -point.Z);
                    sign = localNormal.Y;
                }
            }
            if (localTangent.LengthSquared() < 1e-12f) localTangent = Vector3.UnitX;
            Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, normals));
            Vector3 tangent = Vector3.TransformNormal(localTangent, instance.Transform);
            tangent = Vector3.Normalize(tangent - normal * Vector3.Dot(normal, tangent));
            return EvaluateSecondarySamples(instance, uv * .5f + new Vector2(.5f), normal, tangent,
                sign * MathF.Sign(instance.Transform.GetDeterminant()), Vector4.One, geometric, frontFace);
        }
        for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
        {
            var a = mesh.Vertices[(int)mesh.Indices[triangle]];
            var b = mesh.Vertices[(int)mesh.Indices[triangle + 1]];
            var c = mesh.Vertices[(int)mesh.Indices[triangle + 2]];
            Vector3 ab = b.Position - a.Position;
            Vector3 ac = c.Position - a.Position;
            Vector3 delta = point - a.Position;
            float aa = Vector3.Dot(ab, ab);
            float bb = Vector3.Dot(ac, ac);
            float mixed = Vector3.Dot(ab, ac);
            float denominator = aa * bb - mixed * mixed;
            float u = (bb * Vector3.Dot(delta, ab) - mixed * Vector3.Dot(delta, ac)) / denominator;
            float v = (aa * Vector3.Dot(delta, ac) - mixed * Vector3.Dot(delta, ab)) / denominator;
            float w = 1 - u - v;
            if (u < -.0001f || v < -.0001f || w < -.0001f
                || MathF.Abs(Vector3.Dot(Vector3.Normalize(Vector3.Cross(ab, ac)), delta)) > .001f) continue;
            Vector3 face = Vector3.Normalize(Vector3.TransformNormal(Vector3.Cross(ab, ac), normals));
            if (MathF.Abs(Vector3.Dot(face, geometric)) < .999f) continue;
            Vector2 uv = a.Uv * w + b.Uv * u + c.Uv * v;
            Vector3 na = Vector3.Normalize(Vector3.TransformNormal(a.Normal, normals));
            Vector3 nb = Vector3.Normalize(Vector3.TransformNormal(b.Normal, normals));
            Vector3 nc = Vector3.Normalize(Vector3.TransformNormal(c.Normal, normals));
            Vector3 n = Vector3.Normalize(na * w + nb * u + nc * v);
            Vector3 Tangent(NativeModel3DVertex vertex, Vector3 normal)
            {
                Vector3 t = Vector3.TransformNormal(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z), instance.Transform);
                Vector3 projected = t - normal * Vector3.Dot(normal, t);
                return projected / MathF.Sqrt(MathF.Max(projected.LengthSquared(), .000001f));
            }
            Vector3 blendedTangent = Tangent(a, na) * w + Tangent(b, nb) * u + Tangent(c, nc) * v;
            Vector3 tangent = blendedTangent / MathF.Sqrt(MathF.Max(blendedTangent.LengthSquared(), .000001f));
            float sign = (a.Tangent.W * w + b.Tangent.W * u + c.Tangent.W * v)
                * MathF.Sign(instance.Transform.GetDeterminant());
            return EvaluateSecondarySamples(instance, uv, n, tangent, sign,
                a.Color * w + b.Color * u + c.Color * v, geometric, frontFace);
        }
        throw new InvalidOperationException("Secondary hit is not on its identified source triangle.");
    }

    private static CpuSecondary EvaluateSecondarySamples(SceneInstance3D instance, Vector2 uv, Vector3 n,
        Vector3 tangent, float sign, Vector4 vertexColor, Vector3 geometric, bool frontFace)
    {
        var material = instance.Material;
        Vector4 color = SampleSecondaryTexture(material.BaseColorTexture, uv, true, Vector4.One)
            * material.BaseColor * vertexColor;
        Vector4 map = SampleSecondaryTexture(material.NormalTexture, uv, false, new(128f / 255, 128f / 255, 1, 1));
        float scale = material.NormalTexture is null ? 0 : material.NormalScale;
        Vector3 shading = Vector3.Normalize(tangent * ((map.X * 2 - 1) * scale)
            + Vector3.Cross(n, tangent) * (sign * (map.Y * 2 - 1) * scale) + n * (map.Z * 2 - 1));
        if (!frontFace) shading = -shading;
        if (Vector3.Dot(shading, geometric) <= 0) shading = geometric;
        Vector4 mr = SampleSecondaryTexture(material.MetallicRoughnessTexture, uv, false, Vector4.One);
        Vector4 emission = SampleSecondaryTexture(material.EmissiveTexture, uv, true, Vector4.One);
        Vector4 ao = SampleSecondaryTexture(material.OcclusionTexture, uv, false, Vector4.One);
        return new(uv, new(color.X, color.Y, color.Z), shading,
            new Vector3(emission.X, emission.Y, emission.Z) * material.Emissive,
            Math.Clamp(mr.Y * material.Roughness, .045f, 1), Math.Clamp(mr.Z * material.Metallic, 0, 1),
            1 - material.OcclusionStrength + ao.X * material.OcclusionStrength);
    }

    private static Vector4 SampleSecondaryTexture(ModelTextureBinding? binding, Vector2 uv, bool srgb, Vector4 fallback)
    {
        if (binding is null) return fallback;
        var image = binding.Texture;
        int Wrap(int value, int size, TextureWrap mode)
        {
            if (mode == TextureWrap.Clamp) return Math.Clamp(value, 0, size - 1);
            int period = mode == TextureWrap.Mirror ? size * 2 : size;
            int coordinate = (value % period + period) % period;
            if (coordinate >= size) coordinate = period - coordinate - 1;
            return coordinate;
        }
        Vector4 Texel(int x, int y)
        {
            int offset = (Wrap(x, image.Width, binding.Sampler.WrapU) + Wrap(y, image.Height, binding.Sampler.WrapV) * image.Width) * 4;
            Vector4 value = new(image.Rgba[offset] / 255f, image.Rgba[offset + 1] / 255f, image.Rgba[offset + 2] / 255f, image.Rgba[offset + 3] / 255f);
            static float Decode(float channel) => channel <= .04045f ? channel / 12.92f : MathF.Pow((channel + .055f) / 1.055f, 2.4f);
            if (srgb) value = new(Decode(value.X), Decode(value.Y), Decode(value.Z), value.W);
            return value;
        }
        if (!binding.Sampler.LinearMag) return Texel((int)MathF.Floor(uv.X * image.Width), (int)MathF.Floor(uv.Y * image.Height));
        float x = uv.X * image.Width - .5f;
        float y = uv.Y * image.Height - .5f;
        int left = (int)MathF.Floor(x);
        int top = (int)MathF.Floor(y);
        return Vector4.Lerp(Vector4.Lerp(Texel(left, top), Texel(left + 1, top), x - left),
            Vector4.Lerp(Texel(left, top + 1), Texel(left + 1, top + 1), x - left), y - top);
    }

    private static Vector3 SecondaryPointResponse(CpuSecondary surface, Vector3 point, Vector3 view, Vector3 geometric, LocalLight3D[] lights)
    {
        Vector3 result = surface.Emission;
        foreach (var light in lights)
        {
            Vector3 delta = light.Position - point;
            float squared = delta.LengthSquared();
            Vector3 incoming = Vector3.Normalize(delta);
            if (Vector3.Dot(geometric, incoming) <= 0) continue;
            Vector3 halfway = Vector3.Normalize(incoming + view);
            float nl = MathF.Max(Vector3.Dot(surface.Normal, incoming), 0);
            float nv = MathF.Max(Vector3.Dot(surface.Normal, view), .0001f);
            float nh = MathF.Max(Vector3.Dot(surface.Normal, halfway), 0);
            float vh = MathF.Max(Vector3.Dot(view, halfway), 0);
            float alphaSquared = MathF.Pow(surface.Roughness, 4);
            float denominator = nh * nh * (alphaSquared - 1) + 1;
            float distribution = alphaSquared / MathF.Max(MathF.PI * denominator * denominator, .000001f);
            float k = (surface.Roughness + 1) * (surface.Roughness + 1) / 8;
            float geometry = nv / (nv * (1 - k) + k) * nl / (nl * (1 - k) + k);
            float specular = distribution * geometry / MathF.Max(4 * nv * nl, .0001f);
            Vector3 f0 = new Vector3(.04f * (1 - surface.Metallic)) + surface.Color * surface.Metallic;
            Vector3 fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1 - vh, 5);
            Vector3 response = ((Vector3.One - fresnel) * surface.Color * ((1 - surface.Metallic) / MathF.PI) + fresnel * specular) * nl;
            float ratio = squared / (light.Range * light.Range);
            float fade = MathF.Max(1 - ratio * ratio, 0);
            result += response * light.Color * (light.Intensity * fade * fade / MathF.Max(squared, .01f));
        }
        return result;
    }

    private static Vector3 SecondaryDirection(int index, int count)
    {
        float y = 1 - 2 * (index + .5f) / count;
        float radius = MathF.Sqrt(MathF.Max(1 - y * y, 0));
        float angle = index * 2.39996323f;
        return new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
    }

    private static Vector3 SecondaryOctahedral(int texel)
    {
        float x = texel % 8 / 7f * 2 - 1;
        float z = texel / 8 / 7f * 2 - 1;
        float y = 1 - MathF.Abs(x) - MathF.Abs(z);
        if (y < 0)
        {
            float old = x;
            x = (1 - MathF.Abs(z)) * (x < 0 ? -1 : 1);
            z = (1 - MathF.Abs(old)) * (z < 0 ? -1 : 1);
        }
        return Vector3.Normalize(new(x, y, z));
    }

    private static (CompiledDiffuseProbeField3D Field, object Witness) SecondaryOracleField(NativeInstanceSet3D scene,
        DiffuseProbeGrid grid, float[] witness, LocalLight3D[] lights)
    {
        float[] data = new float[grid.Count * (grid.RaysPerProbe + 64) * 4];
        double maximumNormal = 0;
        double maximumMaterial = 0;
        double maximumRelativeMaterial = 0;
        string largestDifference = "";
        double maximumUv = 0;
        int compared = 0;
        bool linearFiltering = scene.SelectMany(instance => instance.Material.Textures())
            .Any(binding => binding is not null && binding.Sampler.LinearMag);
        double normalTolerance = linearFiltering ? .006 : .00001;
        double materialTolerance = linearFiltering ? .006 : .003;
        Require(witness.Length == grid.Count * grid.RaysPerProbe * 24,
            "Secondary witness contains invalid dimensions.");
        for (int ray = 0; ray < grid.Count * grid.RaysPerProbe; ray++)
        {
            int offset = ray * 24;
            // Misses only define their kind word. The remaining payload has no authority.
            if (witness[offset + 19] == 0) continue;
            for (int word = 0; word < 24; word++)
            {
                Require(float.IsFinite(witness[offset + word]), "Secondary hit witness contains nonfinite values.");
            }
            var instance = scene.PackedInstances[(int)witness[offset + 3]];
            Vector3 point = new(witness[offset + 20], witness[offset + 21], witness[offset + 22]);
            Vector3 geometric = new(witness[offset + 16], witness[offset + 17], witness[offset + 18]);
            var surface = SecondaryOracle(instance, point, geometric, witness[offset + 2] > 0);
            if (instance.Identity == "secondary-source")
            {
                compared++;
                maximumUv = Math.Max(maximumUv, Vector2.Distance(surface.Uv, new(witness[offset], witness[offset + 1])));
                maximumNormal = Math.Max(maximumNormal, Vector3.Distance(surface.Normal, new(witness[offset + 8], witness[offset + 9], witness[offset + 10])));
                float[] expected = [surface.Color.X, surface.Color.Y, surface.Color.Z, surface.Roughness,
                    surface.Normal.X, surface.Normal.Y, surface.Normal.Z, surface.Metallic,
                    surface.Emission.X, surface.Emission.Y, surface.Emission.Z, surface.Occlusion];
                for (int channel = 0; channel < expected.Length; channel++)
                {
                    double difference = Math.Abs(expected[channel] - witness[offset + 4 + channel]);
                    if (difference > maximumMaterial)
                    {
                        maximumMaterial = difference;
                        largestDifference = $"channel {channel}: GPU {witness[offset + 4 + channel]:F7}, CPU {expected[channel]:F7}";
                    }
                    maximumRelativeMaterial = Math.Max(maximumRelativeMaterial, difference / Math.Max(1, Math.Abs(expected[channel])));
                }
            }
            Vector3 value = Vector3.Zero;
            bool backface = witness[offset + 2] < 0;
            if (!backface) value = SecondaryPointResponse(surface, point, -SecondaryDirection(ray % grid.RaysPerProbe, grid.RaysPerProbe), geometric, lights);
            data[ray * 4] = value.X;
            data[ray * 4 + 1] = value.Y;
            data[ray * 4 + 2] = value.Z;
            data[ray * 4 + 3] = backface ? 1 : 0;
        }
        for (int probe = 0; probe < grid.Count; probe++)
        {
            for (int texel = 0; texel < 64; texel++)
            {
                Vector3 sum = Vector3.Zero;
                float backs = 0;
                Vector3 normal = SecondaryOctahedral(texel);
                for (int ray = 0; ray < grid.RaysPerProbe; ray++)
                {
                    int offset = (probe * grid.RaysPerProbe + ray) * 4;
                    Vector3 value = new(data[offset], data[offset + 1], data[offset + 2]);
                    sum += value * (4 * MathF.Max(Vector3.Dot(normal, SecondaryDirection(ray, grid.RaysPerProbe)), 0) / grid.RaysPerProbe);
                    backs += data[offset + 3];
                }
                int target = grid.Count * grid.RaysPerProbe * 4 + (probe * 64 + texel) * 4;
                data[target] = sum.X;
                data[target + 1] = sum.Y;
                data[target + 2] = sum.Z;
                data[target + 3] = backs / grid.RaysPerProbe;
            }
        }
        Require(compared > 100, "Secondary witness did not cover its authored source.");
        Console.WriteLine($"Secondary witness: {compared} samples, UV {maximumUv:E3}, normal {maximumNormal:E3}, material {maximumMaterial:E3}, normalized {maximumRelativeMaterial:E3} ({largestDifference})");
        Require(maximumUv < .001 && maximumNormal < normalTolerance && maximumRelativeMaterial < materialTolerance, "Secondary attributes disagree with independent CPU interpolation/sampling.");
        return (new(1, grid, data), new { Samples = compared, MaximumUvError = maximumUv, MaximumNormalError = maximumNormal, MaximumMaterialError = maximumMaterial, MaximumNormalizedMaterialError = maximumRelativeMaterial, LargestDifference = largestDifference, NormalTolerance = normalTolerance, NormalizedMaterialTolerance = materialTolerance });
    }
}
