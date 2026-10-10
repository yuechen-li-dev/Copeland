using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Models;
using SharpGLTF.Schema2;
using SkiaSharp;

namespace Aurelian.Assets.Models;

public sealed record ModelImportSettings(float Scale = 1);
public sealed record ModelImportResult(StaticModel? Model, IReadOnlyList<AssetDiagnostic> Diagnostics)
{
    public bool Success => Model is not null && !Diagnostics.Any(item => item.Fatal);
}

/// <summary>SharpGLTF owns accessors, sparse data and container validation. This adapter owns Aurelian's static profile.</summary>
public static class GlbModelImporter
{
    public const string Version = "aurelian.glb.static.v1/sharpgltf-1.0.7";
    private const int MaximumBytes = 128 * 1024 * 1024;

    public static ModelImportResult Load(string id, string path, ModelImportSettings? settings = null)
    {
        var diagnostics = new List<AssetDiagnostic>();
        try
        {
            settings ??= new();
            if (!float.IsFinite(settings.Scale) || settings.Scale <= 0)
                throw new InvalidDataException("Import scale must be finite and positive.");
            if (!File.Exists(path)) return Failed("AA3101", "GLB file does not exist.", path);
            if (new FileInfo(path).Length > MaximumBytes)
                throw new InvalidDataException("GLB exceeds the 128 MiB static import budget.");
            byte[] bytes = File.ReadAllBytes(path);
            Preflight(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            ModelRoot root = ModelRoot.ReadGLB(stream);
            if (root.DefaultScene is null) throw new InvalidDataException("GLB needs an explicit default scene.");
            var materials = new Dictionary<Material, ModelMaterial>();
            var images = new Dictionary<int, ModelTexture>();
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (Material material in root.LogicalMaterials)
            {
                string slot = material.Name ?? $"material-{material.LogicalIndex}";
                if (!slots.Add(slot)) throw new InvalidDataException($"Duplicate material name '{slot}'; use unique stable names.");
                materials.Add(material, ReadMaterial(material, slot, images, diagnostics, path));
            }
            var primitives = new Dictionary<MeshPrimitive, ModelPrimitive>();
            var occurrences = new List<ModelOccurrence>();
            void Visit(Node node, int depth)
            {
                if (depth > 128) throw new InvalidDataException("GLB scene nesting exceeds 128 levels.");
                if (node.Mesh is not null)
                {
                    foreach (MeshPrimitive primitive in node.Mesh.Primitives)
                    {
                        if (!primitives.TryGetValue(primitive, out ModelPrimitive? imported))
                        {
                            ModelMaterial material = primitive.Material is null
                                ? new ModelMaterial("$default") : materials[primitive.Material];
                            imported = ReadPrimitive(primitive, material);
                            primitives.Add(primitive, imported);
                        }
                        occurrences.Add(new($"node-{node.LogicalIndex}/{imported.Id}", imported,
                            node.WorldMatrix * Matrix4x4.CreateScale(settings.Scale)));
                    }
                }
                foreach (Node child in node.VisualChildren) Visit(child, depth + 1);
            }
            foreach (Node node in root.DefaultScene.VisualChildren) Visit(node, 0);
            if (occurrences.Sum(item => (long)item.Primitive.Indices.Length) > 1_000_000)
                throw new InvalidDataException("Static model exceeds one million triangle-list vertices.");
            string identity = ContentIdentity(bytes, settings);
            return new(new StaticModel(id, identity, occurrences), diagnostics);
        }
        catch (Exception error) when (error is not OutOfMemoryException && error is not StackOverflowException)
        {
            diagnostics.Add(new("AA3102", "error", error.Message, path));
            return new(null, diagnostics);
        }
    }

    internal static string ContentIdentity(byte[] bytes, ModelImportSettings settings)
    {
        byte[] settingsBytes = System.Text.Encoding.UTF8.GetBytes(Version + ":" + settings.Scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData([.. bytes, .. settingsBytes])).ToLowerInvariant();
    }

    private static void Preflight(byte[] bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546C67
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != bytes.Length
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) != 0x4E4F534A)
            throw new InvalidDataException("Expected a complete glTF 2.0 GLB container.");
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (jsonLength > bytes.Length - 20) throw new InvalidDataException("Invalid GLB JSON chunk length.");
        using JsonDocument json = JsonDocument.Parse(bytes.AsMemory(20, (int)jsonLength));
        JsonElement root = json.RootElement;
        foreach (string collection in new[] { "buffers", "images" })
        {
            if (!root.TryGetProperty(collection, out JsonElement array)) continue;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.TryGetProperty("uri", out _))
                    throw new InvalidDataException("The default GLB profile requires embedded buffers and images; external/data URIs are unsupported.");
            }
        }
        foreach (string collection in new[] { "animations", "skins" })
        {
            if (root.TryGetProperty(collection, out JsonElement array) && array.GetArrayLength() > 0)
                throw new InvalidDataException($"Static import does not support '{collection}'; export a static scene.");
        }
        foreach (string collection in new[] { "extensionsRequired", "extensionsUsed" })
        {
            if (!root.TryGetProperty(collection, out JsonElement extensions)) continue;
            foreach (JsonElement extension in extensions.EnumerateArray())
            {
                if (extension.GetString() is not ("KHR_materials_unlit" or "KHR_materials_emissive_strength"))
                    throw new InvalidDataException($"Unsupported GLB extension '{extension.GetString()}' in {collection}.");
            }
        }
    }

    private static ModelMaterial ReadMaterial(Material source, string slot, Dictionary<int, ModelTexture> images,
        List<AssetDiagnostic> diagnostics, string path)
    {
        if (source.Alpha == AlphaMode.BLEND)
            throw new InvalidDataException($"Slot '{slot}' uses BLEND; this profile supports OPAQUE and MASK.");
        MaterialChannel? color = source.FindChannel("BaseColor");
        MaterialChannel? metal = source.FindChannel("MetallicRoughness");
        MaterialChannel? emissive = source.FindChannel("Emissive");
        return new ModelMaterial(slot)
        {
            BaseColor = color?.Color ?? Vector4.One,
            Metallic = metal?.GetFactor("MetallicFactor") ?? 1,
            Roughness = metal?.GetFactor("RoughnessFactor") ?? 1,
            Emissive = emissive is { } emission
                ? new Vector3(emission.Color.X, emission.Color.Y, emission.Color.Z) * emission.GetFactor("EmissiveStrength") : Vector3.Zero,
            NormalScale = source.FindChannel("Normal")?.GetFactor("NormalScale") ?? 1,
            OcclusionStrength = source.FindChannel("Occlusion")?.GetFactor("OcclusionStrength") ?? 1,
            Unlit = source.Unlit,
            DoubleSided = source.DoubleSided,
            AlphaMask = source.Alpha == AlphaMode.MASK,
            AlphaCutoff = source.AlphaCutoff,
            BaseColorTexture = ReadTexture(color, images, diagnostics, path),
            MetallicRoughnessTexture = ReadTexture(metal, images, diagnostics, path),
            NormalTexture = ReadTexture(source.FindChannel("Normal"), images, diagnostics, path),
            OcclusionTexture = ReadTexture(source.FindChannel("Occlusion"), images, diagnostics, path),
            EmissiveTexture = ReadTexture(emissive, images, diagnostics, path),
        };
    }

    private static ModelTextureBinding? ReadTexture(MaterialChannel? channel, Dictionary<int, ModelTexture> images,
        List<AssetDiagnostic> diagnostics, string path)
    {
        if (channel?.Texture is not { } texture) return null;
        if (channel.Value.TextureCoordinate != 0 || channel.Value.TextureTransform is not null)
            throw new InvalidDataException("The static profile requires TEXCOORD_0 with no texture transform.");
        Image image = texture.PrimaryImage;
        if (!images.TryGetValue(image.LogicalIndex, out ModelTexture? decoded))
        {
            byte[] encoded = image.Content.Content.ToArray();
            using SKCodec? codec = SKCodec.Create(new SKMemoryStream(encoded));
            if (codec is null || codec.EncodedFormat is not SKEncodedImageFormat.Png and not SKEncodedImageFormat.Jpeg)
                throw new InvalidDataException("Embedded textures must be PNG or JPEG.");
            if (codec.Info.Width > 8192 || codec.Info.Height > 8192)
                throw new InvalidDataException("Texture exceeds the 8192 pixel dimension budget.");
            if (images.Values.Sum(item => (long)item.Rgba.Length) + (long)codec.Info.Width * codec.Info.Height * 4 > 256 * 1024 * 1024)
                throw new InvalidDataException("Decoded textures exceed the 256 MiB static import budget.");
            using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("Embedded texture decoding failed.");
            byte[] pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            string hash = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();
            decoded = new(hash, bitmap.Width, bitmap.Height, pixels.ToImmutableArray());
            images.Add(image.LogicalIndex, decoded);
        }
        TextureSampler? sampler = texture.Sampler;
        int minFilter = (int)(sampler?.MinFilter ?? TextureMipMapFilter.DEFAULT);
        if (minFilter is >= 9984 and <= 9987)
            diagnostics.Add(new("AA3103", "warning", "Mipmapped sampler uses the base level in static.v1; mip generation is not yet qualified.", path, Fatal: false));
        return new(decoded, new ModelSampler(
            LinearMin: minFilter is not 9728 and not 9984 and not 9986,
            LinearMag: (int)(sampler?.MagFilter ?? TextureInterpolationFilter.DEFAULT) != 9728,
            WrapU: Wrap((int)(sampler?.WrapS ?? TextureWrapMode.REPEAT)),
            WrapV: Wrap((int)(sampler?.WrapT ?? TextureWrapMode.REPEAT))));
    }

    private static TextureWrap Wrap(int wrap) => wrap switch
    {
        10497 => TextureWrap.Repeat,
        33071 => TextureWrap.Clamp,
        33648 => TextureWrap.Mirror,
        _ => throw new InvalidDataException($"Unsupported texture wrap value {wrap}."),
    };

    private static ModelPrimitive ReadPrimitive(MeshPrimitive source, ModelMaterial material)
    {
        if (source.DrawPrimitiveType != PrimitiveType.TRIANGLES || source.MorphTargetsCount != 0)
            throw new InvalidDataException("Static import requires triangle primitives without morph targets.");
        string[] attributes = ["POSITION", "NORMAL", "TEXCOORD_0", "TANGENT", "COLOR_0"];
        if (source.VertexAccessors.Keys.Any(key => !attributes.Contains(key)))
            throw new InvalidDataException("Static import supports POSITION, NORMAL, TEXCOORD_0, TANGENT and COLOR_0 only.");
        if (source.GetVertexAccessor("POSITION") is { Count: > 1_000_000 }
            || source.IndexAccessor is { Count: > 1_000_000 })
            throw new InvalidDataException("Primitive exceeds the one-million vertex/index static import budget.");
        var positions = source.GetVertexAccessor("POSITION")?.AsVector3Array()
            ?? throw new InvalidDataException("Primitive has no POSITION accessor.");
        var normals = source.GetVertexAccessor("NORMAL")?.AsVector3Array()
            ?? throw new InvalidDataException("Primitive has no NORMAL accessor; export normals from the authoring tool.");
        var uv = source.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var tangents = source.GetVertexAccessor("TANGENT")?.AsVector4Array();
        var colors = source.GetVertexAccessor("COLOR_0")?.AsColorArray();
        if (material.Textures().Any(item => item is not null) && uv is null)
            throw new InvalidDataException($"Textured primitive in '{material.Slot}' has no TEXCOORD_0 accessor.");
        if (material.NormalTexture is not null && tangents is null)
            throw new InvalidDataException($"Normal-mapped primitive in '{material.Slot}' has no TANGENT accessor; enable tangent export.");
        var vertices = ImmutableArray.CreateBuilder<ModelVertex>(positions.Count);
        for (int index = 0; index < positions.Count; index++)
        {
            Vector3 normal = normals[index];
            Vector3 axis = MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
            Vector4 tangent = tangents?[index] ?? new(Vector3.Normalize(Vector3.Cross(axis, normal)), 1);
            if (MathF.Abs(new Vector3(tangent.X, tangent.Y, tangent.Z).LengthSquared() - 1) > 0.001f
                || MathF.Abs(MathF.Abs(tangent.W) - 1) > 0.001f
                || MathF.Abs(Vector3.Dot(normal, new(tangent.X, tangent.Y, tangent.Z))) > 0.001f)
                throw new InvalidDataException("Tangents must be unit, orthogonal to normals, with handedness +1 or -1.");
            vertices.Add(new(positions[index], normal, uv?[index] ?? Vector2.Zero, tangent, colors?[index] ?? Vector4.One));
        }
        var indices = ImmutableArray.CreateBuilder<int>();
        foreach (var triangle in source.GetTriangleIndices())
        {
            indices.Add(triangle.A);
            indices.Add(triangle.B);
            indices.Add(triangle.C);
        }
        return new($"mesh-{source.LogicalParent.LogicalIndex}-primitive-{source.LogicalIndex}", vertices.ToImmutable(), indices.ToImmutable(), material)
        {
            HasUv = uv is not null,
            HasTangents = tangents is not null,
        };
    }

    private static ModelImportResult Failed(string code, string message, string path) =>
        new(null, [new(code, "error", message, path)]);
}
