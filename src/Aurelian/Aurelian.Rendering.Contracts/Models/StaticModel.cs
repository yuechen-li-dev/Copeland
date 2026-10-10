using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public enum TextureWrap
{
    Repeat,
    Clamp,
    Mirror,
}
public sealed record ModelSampler(bool LinearMin = true, bool LinearMag = true,
    TextureWrap WrapU = TextureWrap.Repeat, TextureWrap WrapV = TextureWrap.Repeat);

/// <summary>Decoded RGBA8 pixels. Color space belongs to the material channel, not the image.</summary>
public sealed record ModelTexture(string Identity, int Width, int Height, ImmutableArray<byte> Rgba);
public sealed record ModelTextureBinding(ModelTexture Texture, ModelSampler Sampler);

/// <summary>Linear factors and glTF channel conventions. Customize instances using ordinary with expressions.</summary>
public sealed record ModelMaterial(string Slot)
{
    public Vector4 BaseColor { get; init; } = Vector4.One;
    public float Metallic { get; init; } = 1;
    public float Roughness { get; init; } = 1;
    public Vector3 Emissive { get; init; }
    public float NormalScale { get; init; } = 1;
    public float OcclusionStrength { get; init; } = 1;
    public bool Unlit { get; init; }
    public bool DoubleSided { get; init; }
    public bool AlphaMask { get; init; }
    public bool AlphaBlend { get; init; }
    public float SubsurfaceStrength { get; init; }
    public Vector3 SubsurfaceColor { get; init; } = new(1, .35f, .15f);
    public float SubsurfaceRadius { get; init; } = .02f;
    public float AlphaCutoff { get; init; } = 0.5f;
    public ModelTextureBinding? BaseColorTexture { get; init; }
    public ModelTextureBinding? MetallicRoughnessTexture { get; init; }
    public ModelTextureBinding? NormalTexture { get; init; }
    public ModelTextureBinding? OcclusionTexture { get; init; }
    public ModelTextureBinding? EmissiveTexture { get; init; }

    public void Validate()
    {
        float[] unit = [BaseColor.X, BaseColor.Y, BaseColor.Z, BaseColor.W, Metallic,
            Roughness, OcclusionStrength, AlphaCutoff, SubsurfaceStrength,
            SubsurfaceColor.X, SubsurfaceColor.Y, SubsurfaceColor.Z];
        if (string.IsNullOrWhiteSpace(Slot) || unit.Any(value => !float.IsFinite(value) || value < 0 || value > 1)
            || new[] { Emissive.X, Emissive.Y, Emissive.Z }.Any(value => !float.IsFinite(value) || value < 0 || value > 60000)
            || !float.IsFinite(NormalScale) || NormalScale < 0
            || AlphaBlend && AlphaMask
            || !float.IsFinite(SubsurfaceRadius) || SubsurfaceRadius < .0001f || SubsurfaceRadius > 1
            || SubsurfaceStrength > 0 && (Metallic != 0 || AlphaBlend || Unlit))
        {
            throw new InvalidDataException($"Invalid material factors for slot '{Slot}'.");
        }
        foreach (ModelTextureBinding? binding in Textures())
        {
            if (binding is null) continue;
            ModelTexture texture = binding.Texture;
            if (texture.Width <= 0 || texture.Height <= 0 || texture.Width > 8192 || texture.Height > 8192
                || texture.Rgba.IsDefault || texture.Rgba.Length != (long)texture.Width * texture.Height * 4
                || string.IsNullOrWhiteSpace(texture.Identity)
                || !Enum.IsDefined(binding.Sampler.WrapU) || !Enum.IsDefined(binding.Sampler.WrapV))
            {
                throw new InvalidDataException($"Invalid texture in slot '{Slot}'.");
            }
        }
    }

    public IEnumerable<ModelTextureBinding?> Textures()
    {
        yield return BaseColorTexture;
        yield return MetallicRoughnessTexture;
        yield return NormalTexture;
        yield return OcclusionTexture;
        yield return EmissiveTexture;
    }
}

public readonly record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Vector4 Tangent, Vector4 Color);
public sealed record ModelPrimitive(string Id, ImmutableArray<ModelVertex> Vertices,
    ImmutableArray<int> Indices, ModelMaterial Material)
{
    public bool HasUv { get; init; } = true;
    public bool HasTangents { get; init; } = true;

    public void ValidateMaterial(ModelMaterial material)
    {
        material.Validate();
        if (!HasUv && material.Textures().Any(item => item is not null))
            throw new InvalidDataException($"Textured primitive '{Id}' needs TEXCOORD_0.");
        if (!HasTangents && material.NormalTexture is not null)
            throw new InvalidDataException($"Normal-mapped primitive '{Id}' needs TANGENT.");
    }
}
public sealed record ModelOccurrence(string Id, ModelPrimitive Primitive, Matrix4x4 Transform);

/// <summary>Renderer-neutral static presentation. Collision and CAD topology remain separately authored.</summary>
public sealed class StaticModel
{
    public StaticModel(string id, string contentIdentity, IEnumerable<ModelOccurrence> occurrences)
    {
        Id = id;
        ContentIdentity = contentIdentity;
        Occurrences = occurrences.ToImmutableArray();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(contentIdentity) || Occurrences.IsEmpty)
            throw new InvalidDataException("A static model needs an ID, content identity and geometry.");
        foreach (ModelOccurrence occurrence in Occurrences)
        {
            ModelPrimitive primitive = occurrence.Primitive;
            primitive.ValidateMaterial(primitive.Material);
            if (primitive.Vertices.IsDefaultOrEmpty || primitive.Indices.IsDefaultOrEmpty || primitive.Indices.Length % 3 != 0
                || primitive.Indices.Any(index => index < 0 || index >= primitive.Vertices.Length))
                throw new InvalidDataException($"Invalid triangles in '{primitive.Id}'.");
            foreach (ModelVertex vertex in primitive.Vertices)
            {
                if (!Finite(vertex.Position) || !Finite(vertex.Normal) || !float.IsFinite(vertex.Uv.X) || !float.IsFinite(vertex.Uv.Y)
                    || !Finite(vertex.Tangent) || !Finite(vertex.Color) || MathF.Abs(vertex.Normal.LengthSquared() - 1) > 0.001f)
                    throw new InvalidDataException($"Invalid vertex in '{primitive.Id}'.");
            }
            Matrix4x4 matrix = occurrence.Transform;
            float[] entries = [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];
            if (entries.Any(value => !float.IsFinite(value)) || matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0
                || matrix.M44 != 1 || !float.IsFinite(matrix.GetDeterminant()) || matrix.GetDeterminant() <= 0
                || !Matrix4x4.Invert(matrix, out _))
                throw new InvalidDataException($"Static model occurrence '{occurrence.Id}' needs an invertible positive affine transform.");
        }
    }

    public string Id { get; }
    public string ContentIdentity { get; }
    public ImmutableArray<ModelOccurrence> Occurrences { get; }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>Explicit, synchronous replacement boundary. Candidate validation and GPU preparation precede publication.</summary>
public sealed class ModelSlot
{
    private StaticModel current;
    public ModelSlot(StaticModel initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        current = initial;
    }
    public StaticModel Current => Volatile.Read(ref current);
    public string Id => Current.Id;

    public void Replace(StaticModel candidate, Action<StaticModel>? prepare = null)
    {
        StaticModel previous = Current;
        if (candidate.Id != previous.Id) throw new InvalidDataException("Replacement must retain the stable asset ID.");
        var slots = candidate.Occurrences.Select(item => item.Primitive.Material.Slot).ToHashSet(StringComparer.Ordinal);
        foreach (string slot in previous.Occurrences.Select(item => item.Primitive.Material.Slot).Distinct())
        {
            if (!slots.Contains(slot)) throw new InvalidDataException($"Replacement loses material slot '{slot}'; rename or remap explicitly.");
            var before = previous.Occurrences.Where(item => item.Primitive.Material.Slot == slot).Select(item => item.Primitive);
            var after = candidate.Occurrences.Where(item => item.Primitive.Material.Slot == slot).Select(item => item.Primitive);
            if ((before.All(item => item.HasUv) && after.Any(item => !item.HasUv))
                || (before.All(item => item.HasTangents) && after.Any(item => !item.HasTangents)))
                throw new InvalidDataException($"Replacement loses UV/tangent capability in slot '{slot}'; existing material overrides would be unsafe.");
        }
        prepare?.Invoke(candidate);
        if (!ReferenceEquals(Interlocked.CompareExchange(ref current, candidate, previous), previous))
            throw new InvalidOperationException("Concurrent model replacement; retry at the application frame boundary.");
    }
}
