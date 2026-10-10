using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace Aurelian.World.Scenes;

public sealed record PlacedSceneBox(string Id, Matrix4x4 WorldTransform, Vector3 HalfSize,
    Vector4 Color, SceneCollision Collision)
{
    public ModelMaterial? Material { get; init; }
    public uint CollisionLayer { get; init; } = 1;
    public uint CollisionMask { get; init; } = uint.MaxValue;
}
public sealed record PlacedSceneMesh(string Id, Matrix4x4 WorldTransform, ImmutableArray<SceneVertex> Vertices)
{
    /// <summary>Retained vertex correspondence key, derived once from authored geometry.</summary>
    public string? GeometryIdentity { get; init; }
    public ImmutableArray<int> Indices { get; init; } = [];
    public SceneCollision Collision { get; init; }
    public ModelMaterial? Material { get; init; }
    public uint CollisionLayer { get; init; } = 1;
    public uint CollisionMask { get; init; } = uint.MaxValue;
    public bool ClosedCollision { get; init; }
    public string? SourceIdentity { get; init; }
    public ImmutableArray<string?> TriangleFaces { get; init; } = [];
}
public sealed record PlacedSceneModel(string Id, Matrix4x4 WorldTransform, ModelSlot Asset,
    string InitialContentIdentity, ImmutableDictionary<string, ModelMaterial> Materials);
public sealed record PlacedSceneAgent(SceneAgentNode Node, ScenePlacement Placement);

public sealed class ScenePlan
{
    private string? contentIdentity;
    internal ScenePlan(string id, IEnumerable<string> identities, IEnumerable<PlacedSceneBox> boxes,
        IEnumerable<PlacedSceneMesh> meshes, IEnumerable<PlacedSceneAgent> agents, IEnumerable<PlacedSceneModel> models)
    {
        Id = id;
        Identities = identities.Order(StringComparer.Ordinal).ToImmutableArray();
        Boxes = boxes.ToImmutableArray();
        Meshes = meshes.ToImmutableArray();
        Models = models.ToImmutableArray();
        Agents = agents.OrderBy(agent => agent.Placement.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    public string Id { get; }
    public string ContentIdentity => contentIdentity ??= ComputeIdentity();
    public ImmutableArray<string> Identities { get; }
    public ImmutableArray<PlacedSceneBox> Boxes { get; }
    public ImmutableArray<PlacedSceneMesh> Meshes { get; }
    public ImmutableArray<PlacedSceneModel> Models { get; }
    public ImmutableArray<PlacedSceneAgent> Agents { get; }
    public SceneInstance Mount() => SceneInstance.Mount(this);

    private string ComputeIdentity()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        bool primitiveMaterials = Boxes.Any(box => box.Material is not null) || Meshes.Any(mesh => mesh.Material is not null);
        string version = Models.IsEmpty ? "aurelian.scene.v2" : "aurelian.scene.v3";
        if (primitiveMaterials) version = "aurelian.scene.v4";
        writer.Write(version);
        writer.Write(Id);
        writer.Write(Identities.Length);
        foreach (string id in Identities)
        {
            writer.Write(id);
        }
        writer.Write(Boxes.Length);
        foreach (PlacedSceneBox box in Boxes)
        {
            writer.Write(box.Id);
            WriteMatrix(writer, box.WorldTransform);
            WriteVector(writer, box.HalfSize);
            WriteColor(writer, box.Color);
            if (primitiveMaterials) WriteOptionalMaterial(writer, box.Material);
            writer.Write((int)box.Collision);
            writer.Write(box.CollisionLayer);
            writer.Write(box.CollisionMask);
        }
        writer.Write(Meshes.Length);
        foreach (PlacedSceneMesh mesh in Meshes)
        {
            writer.Write(mesh.Id);
            if (primitiveMaterials) WriteOptionalMaterial(writer, mesh.Material);
            WriteMatrix(writer, mesh.WorldTransform);
            writer.Write((int)mesh.Collision);
            writer.Write(mesh.CollisionLayer);
            writer.Write(mesh.CollisionMask);
            writer.Write(mesh.ClosedCollision);
            writer.Write(mesh.SourceIdentity ?? "");
            writer.Write(mesh.Indices.Length);
            foreach (int index in mesh.Indices) writer.Write(index);
            writer.Write(mesh.TriangleFaces.Length);
            foreach (string? face in mesh.TriangleFaces) writer.Write(face ?? "");
            writer.Write(mesh.Vertices.Length);
            foreach (SceneVertex vertex in mesh.Vertices)
            {
                WriteVector(writer, vertex.Position);
                WriteVector(writer, vertex.Normal);
                WriteColor(writer, vertex.Color);
            }
        }
        if (!Models.IsEmpty)
        {
            writer.Write(Models.Length);
            foreach (PlacedSceneModel model in Models)
            {
                writer.Write(model.Id);
                WriteMatrix(writer, model.WorldTransform);
                writer.Write(model.Asset.Id);
                writer.Write(model.InitialContentIdentity);
                writer.Write(model.Materials.Count);
                foreach (var pair in model.Materials.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.Write(pair.Key);
                    ModelMaterial material = pair.Value;
                    WriteMaterial(writer, material);
                }
            }
        }
        writer.Write(Agents.Length);
        foreach (PlacedSceneAgent agent in Agents)
        {
            writer.Write(agent.Placement.Id);
            writer.Write(agent.Placement.Name);
            writer.Write(agent.Node.DefinitionIdentity);
            writer.Write(agent.Node.Template.Id);
            writer.Write((int)agent.Node.Template.Kind);
            writer.Write((int)agent.Node.Template.Control);
            WriteMatrix(writer, agent.Placement.WorldTransform);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteMaterial(BinaryWriter writer, ModelMaterial material)
    {
        WriteColor(writer, material.BaseColor);
        writer.Write(material.Metallic);
        writer.Write(material.Roughness);
        WriteVector(writer, material.Emissive);
        writer.Write(material.NormalScale);
        writer.Write(material.OcclusionStrength);
        writer.Write(material.Unlit);
        writer.Write(material.DoubleSided);
        writer.Write(material.AlphaMask);
        writer.Write(material.AlphaBlend);
        writer.Write(material.SubsurfaceStrength);
        writer.Write(material.SubsurfaceColor.X);
        writer.Write(material.SubsurfaceColor.Y);
        writer.Write(material.SubsurfaceColor.Z);
        writer.Write(material.SubsurfaceRadius);
        writer.Write(material.AlphaCutoff);
        foreach (ModelTextureBinding? binding in material.Textures())
        {
            writer.Write(binding is not null);
            if (binding is null) continue;
            writer.Write(binding.Texture.Identity);
            writer.Write(binding.Texture.Width);
            writer.Write(binding.Texture.Height);
            writer.Write(SHA256.HashData(binding.Texture.Rgba.AsSpan()));
            writer.Write(binding.Sampler.LinearMin);
            writer.Write(binding.Sampler.LinearMag);
            writer.Write((int)binding.Sampler.WrapU);
            writer.Write((int)binding.Sampler.WrapV);
        }
    }

    private static void WriteOptionalMaterial(BinaryWriter writer, ModelMaterial? material)
    {
        writer.Write(material is not null);
        if (material is null) return;
        writer.Write(material.Slot);
        WriteMaterial(writer, material);
    }

    private static void WriteMatrix(BinaryWriter writer, Matrix4x4 matrix)
    {
        float[] values = [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44];
        foreach (float value in values)
        {
            writer.Write(value);
        }
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteColor(BinaryWriter writer, Vector4 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }
}

/// <summary>Validates the complete document before calling any state or policy factory.</summary>
public static class SceneCompiler
{
    public static string ComputeGeometryIdentity(ImmutableArray<SceneVertex> vertices, ImmutableArray<int> indices)
    {
        string vertexKey = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(vertices.AsSpan())));
        string indexKey = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(indices.AsSpan())));
        return vertexKey + indexKey;
    }

    public static ScenePlan Compile(SceneGroup document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compiler = new Compilation();
        ValidateSegment(document.Id);
        compiler.Children(document.Children, "", document.Transform.Matrix(), 0);
        return new ScenePlan(document.Id, compiler.Identities, compiler.Boxes, compiler.Meshes, compiler.Agents, compiler.Models);
    }

    internal static void ValidateSegment(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException("Scene IDs are path segments: use ASCII letters, digits, hyphens or underscores.");
        }
    }

    private sealed class Compilation
    {
        public HashSet<string> Identities { get; } = new(StringComparer.Ordinal);
        public List<PlacedSceneBox> Boxes { get; } = [];
        public List<PlacedSceneMesh> Meshes { get; } = [];
        public List<PlacedSceneModel> Models { get; } = [];
        public List<PlacedSceneAgent> Agents { get; } = [];

        public void Children(ImmutableArray<SceneNode> children, string prefix, Matrix4x4 parent, int depth)
        {
            if (children.IsDefault || depth > 128)
            {
                throw new InvalidDataException("Scene children must be initialized and nesting may not exceed 128 levels.");
            }
            foreach (SceneNode node in children)
            {
                ArgumentNullException.ThrowIfNull(node);
                ValidateSegment(node.Id);
                string id = prefix.Length == 0 ? node.Id : prefix + "." + node.Id;
                if (!Identities.Add(id))
                {
                    throw new InvalidDataException($"Duplicate scene instance ID '{id}'.");
                }
                Matrix4x4 world = node.Transform.Matrix() * parent;
                SceneTransform.ValidateMatrix(world);
                switch (node)
                {
                    case SceneGroup group:
                        Children(group.Children, id, world, depth + 1);
                        break;
                    case SceneInstanceNode instance:
                        ArgumentNullException.ThrowIfNull(instance.Fragment);
                        ValidateSegment(instance.Fragment.Id);
                        Matrix4x4 fragmentWorld = instance.Fragment.Transform.Matrix() * world;
                        SceneTransform.ValidateMatrix(fragmentWorld);
                        Children(instance.Fragment.Children, id, fragmentWorld, depth + 1);
                        break;
                    case SceneBox box:
                        if (!SceneTransform.Finite(box.HalfSize) || box.HalfSize.X <= 0
                            || box.HalfSize.Y <= 0 || box.HalfSize.Z <= 0 || !Enum.IsDefined(box.Collision)
                            || box.CollisionLayer == 0)
                        {
                            throw new InvalidDataException($"Invalid box geometry or collision declaration at '{id}'.");
                        }
                        ValidateColor(box.Color);
                        ValidatePrimitiveMaterial(box.Material);
                        Boxes.Add(new(id, world, box.HalfSize, box.Color, box.Collision)
                        {
                            Material = box.Material,
                            CollisionLayer = box.CollisionLayer,
                            CollisionMask = box.CollisionMask,
                        });
                        break;
                    case SceneMesh mesh:
                        if (mesh.Indices.IsDefault || mesh.TriangleFaces.IsDefault)
                        {
                            throw new InvalidDataException($"Mesh '{id}' has uninitialized index or face collections.");
                        }
                        ValidatePrimitiveMaterial(mesh.Material);
                        int triangleWords = mesh.Indices.IsEmpty ? mesh.Vertices.Length : mesh.Indices.Length;
                        if (mesh.Vertices.IsDefaultOrEmpty || triangleWords == 0 || triangleWords % 3 != 0
                            || mesh.Indices.Any(index => index < 0 || index >= mesh.Vertices.Length)
                            || !Enum.IsDefined(mesh.Collision) || mesh.CollisionLayer == 0
                            || (!mesh.TriangleFaces.IsEmpty && mesh.TriangleFaces.Length != triangleWords / 3))
                        {
                            throw new InvalidDataException($"Mesh '{id}' needs complete triangles.");
                        }
                        foreach (SceneVertex vertex in mesh.Vertices)
                        {
                            if (!SceneTransform.Finite(vertex.Position) || !SceneTransform.Finite(vertex.Normal)
                                || MathF.Abs(vertex.Normal.LengthSquared() - 1) > 0.0001f)
                            {
                                throw new InvalidDataException($"Mesh '{id}' needs finite vertices and unit normals.");
                            }
                            ValidateColor(vertex.Color);
                        }
                        Meshes.Add(new(id, world, mesh.Vertices)
                        {
                            GeometryIdentity = ComputeGeometryIdentity(mesh.Vertices, mesh.Indices),
                            Material = mesh.Material,
                            Indices = mesh.Indices,
                            Collision = mesh.Collision,
                            CollisionLayer = mesh.CollisionLayer,
                            CollisionMask = mesh.CollisionMask,
                            ClosedCollision = mesh.ClosedCollision,
                            SourceIdentity = mesh.SourceIdentity,
                            TriangleFaces = mesh.TriangleFaces,
                        });
                        break;
                    case SceneModel model:
                        ArgumentNullException.ThrowIfNull(model.Asset);
                        StaticModel asset = model.Asset.Current;
                        var slots = asset.Occurrences.Select(item => item.Primitive.Material.Slot).ToHashSet(StringComparer.Ordinal);
                        foreach (var pair in model.Materials)
                        {
                            if (!slots.Contains(pair.Key) || pair.Value.Slot != pair.Key)
                                throw new InvalidDataException($"Model '{id}' has an unknown or mismatched material override '{pair.Key}'.");
                            foreach (ModelPrimitive primitive in asset.Occurrences.Select(item => item.Primitive)
                                .Where(item => item.Material.Slot == pair.Key))
                            {
                                primitive.ValidateMaterial(pair.Value);
                            }
                        }
                        Models.Add(new(id, world, model.Asset, asset.ContentIdentity, model.Materials));
                        break;
                    case SceneAgentNode agent:
                        var placement = new ScenePlacement(id, agent.Name, world);
                        agent.Validate(placement);
                        Agents.Add(new(agent, placement));
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported scene node at '{id}'.");
                }
            }
        }

        private static void ValidatePrimitiveMaterial(ModelMaterial? material)
        {
            material?.Validate();
            if (material?.NormalTexture is not null)
                throw new InvalidDataException("Primitive normal maps require explicit UV/tangent geometry through Scene.Model.");
        }

        private static void ValidateColor(Vector4 color)
        {
            if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z)
                || !float.IsFinite(color.W))
            {
                throw new InvalidDataException("Scene colors must be finite.");
            }
        }
    }
}
