using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Aurelian.World.Scenes;

public sealed record PlacedSceneBox(string Id, Matrix4x4 WorldTransform, Vector3 HalfSize,
    Vector4 Color, SceneCollision Collision);
public sealed record PlacedSceneMesh(string Id, Matrix4x4 WorldTransform, ImmutableArray<SceneVertex> Vertices);
public sealed record PlacedSceneAgent(SceneAgentNode Node, ScenePlacement Placement);

public sealed class ScenePlan
{
    private string? contentIdentity;
    internal ScenePlan(string id, IEnumerable<string> identities, IEnumerable<PlacedSceneBox> boxes,
        IEnumerable<PlacedSceneMesh> meshes, IEnumerable<PlacedSceneAgent> agents)
    {
        Id = id;
        Identities = identities.Order(StringComparer.Ordinal).ToImmutableArray();
        Boxes = boxes.ToImmutableArray();
        Meshes = meshes.ToImmutableArray();
        Agents = agents.OrderBy(agent => agent.Placement.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    public string Id { get; }
    public string ContentIdentity => contentIdentity ??= ComputeIdentity();
    public ImmutableArray<string> Identities { get; }
    public ImmutableArray<PlacedSceneBox> Boxes { get; }
    public ImmutableArray<PlacedSceneMesh> Meshes { get; }
    public ImmutableArray<PlacedSceneAgent> Agents { get; }
    public SceneInstance Mount() => SceneInstance.Mount(this);

    private string ComputeIdentity()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("aurelian.scene.v1");
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
            writer.Write((int)box.Collision);
        }
        writer.Write(Meshes.Length);
        foreach (PlacedSceneMesh mesh in Meshes)
        {
            writer.Write(mesh.Id);
            WriteMatrix(writer, mesh.WorldTransform);
            writer.Write(mesh.Vertices.Length);
            foreach (SceneVertex vertex in mesh.Vertices)
            {
                WriteVector(writer, vertex.Position);
                WriteVector(writer, vertex.Normal);
                WriteColor(writer, vertex.Color);
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
    public static ScenePlan Compile(SceneGroup document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compiler = new Compilation();
        ValidateSegment(document.Id);
        compiler.Children(document.Children, "", document.Transform.Matrix(), 0);
        return new ScenePlan(document.Id, compiler.Identities, compiler.Boxes, compiler.Meshes, compiler.Agents);
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
                            || box.HalfSize.Y <= 0 || box.HalfSize.Z <= 0 || !Enum.IsDefined(box.Collision))
                        {
                            throw new InvalidDataException($"Invalid box geometry or collision declaration at '{id}'.");
                        }
                        ValidateColor(box.Color);
                        Boxes.Add(new(id, world, box.HalfSize, box.Color, box.Collision));
                        break;
                    case SceneMesh mesh:
                        if (mesh.Vertices.IsDefaultOrEmpty || mesh.Vertices.Length % 3 != 0)
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
                        Meshes.Add(new(id, world, mesh.Vertices));
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
