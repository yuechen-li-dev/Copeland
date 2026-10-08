using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.World.Scenes;

namespace Aurelian.Aetheris;

public sealed record AetherisMeshAsset(string Id, string Identity, float[] Positions, float[] Normals,
    int[] Indices, string Representation, string?[] FaceIds);
public sealed record AetherisOccurrenceAsset(string Path, string DefinitionId, float[] WorldTransform,
    float[] Color, bool Solid, uint Layer = 1, uint Mask = uint.MaxValue);
public sealed record AetherisSceneAsset(string Schema, string SourceDocument, string SourceSha256,
    AetherisMeshAsset[] Definitions, AetherisOccurrenceAsset[] Occurrences)
{
    public const string CurrentSchema = "aurelian.aetheris.scene.v1";

    public static AetherisSceneAsset Load(string path)
    {
        return JsonSerializer.Deserialize(File.ReadAllText(path), AetherisAssetJsonContext.Default.AetherisSceneAsset)
            ?? throw new InvalidDataException("Aetheris scene asset is empty.");
    }

    /// <summary>The bake owns mm/Z-up conversion. Runtime compilation performs no CAD tessellation.</summary>
    public SceneGroup Compose(string id = "aetheris-scene")
    {
        if (Schema != CurrentSchema || string.IsNullOrWhiteSpace(SourceDocument) || SourceSha256 is null
            || SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit) || Definitions is null || Occurrences is null)
        {
            throw new InvalidDataException("Unsupported Aetheris scene schema or source identity.");
        }
        var definitions = new Dictionary<string, (AetherisMeshAsset Asset, ImmutableArray<SceneVertex> Vertices)>(StringComparer.Ordinal);
        foreach (AetherisMeshAsset definition in Definitions)
        {
            if (definition.Positions is null || definition.Normals is null || definition.Indices is null || definition.FaceIds is null
                || definition.Positions.Length == 0 || definition.Positions.Length % 3 != 0
                || definition.Normals.Length != definition.Positions.Length
                || definition.Representation is not ("planar-exact" or "triangle-approximation"))
            {
                throw new InvalidDataException("Invalid Aetheris mesh definition or collision representation.");
            }
            var vertices = ImmutableArray.CreateBuilder<SceneVertex>();
            for (int index = 0; index < definition.Positions.Length; index += 3)
            {
                vertices.Add(new(new(definition.Positions[index], definition.Positions[index + 1], definition.Positions[index + 2]),
                    new(definition.Normals[index], definition.Normals[index + 1], definition.Normals[index + 2]), Vector4.One));
            }
            definitions.Add(definition.Id, (definition, vertices.ToImmutable()));
        }
        var leaves = new List<(string[] Parts, SceneMesh Mesh)>();
        foreach (AetherisOccurrenceAsset occurrence in Occurrences.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var definition = definitions[occurrence.DefinitionId];
            if (occurrence.WorldTransform is null || occurrence.Color is null
                || occurrence.WorldTransform.Length != 16 || occurrence.Color.Length != 4)
            {
                throw new InvalidDataException("Aetheris occurrences need explicit affine frames and RGBA colors.");
            }
            float[] m = occurrence.WorldTransform;
            Matrix4x4 transform = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            SceneTransform.ValidateMatrix(transform);
            Vector4 color = new(occurrence.Color[0], occurrence.Color[1], occurrence.Color[2], occurrence.Color[3]);
            // Affine frames can include shear, so bake the occurrence into vertices while
            // retaining each definition's indexed topology and source identity.
            Matrix4x4.Invert(transform, out Matrix4x4 inverse);
            Matrix4x4 normals = Matrix4x4.Transpose(inverse);
            var vertices = definition.Vertices.Select(vertex => new SceneVertex(Vector3.Transform(vertex.Position, transform),
                Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals)), color)).ToImmutableArray();
            string[] parts = occurrence.Path.Split('.');
            var mesh = new SceneMesh(parts[^1], vertices)
            {
                Indices = definition.Asset.Indices.ToImmutableArray(),
                Collision = occurrence.Solid ? SceneCollision.Solid : SceneCollision.None,
                ClosedCollision = definition.Asset.Representation == "planar-exact",
                CollisionLayer = occurrence.Layer,
                CollisionMask = occurrence.Mask,
                SourceIdentity = SourceDocument + ":" + SourceSha256 + "::" + definition.Asset.Identity,
                TriangleFaces = definition.Asset.FaceIds.ToImmutableArray(),
            };
            leaves.Add((parts, mesh));
        }
        SceneGroup result = Scene.Group(id, Build(leaves, 0));
        // Validate the complete authoring result before returning any usable scene.
        SceneCompiler.Compile(result);
        return result;
    }

    private static IEnumerable<SceneNode> Build(IEnumerable<(string[] Parts, SceneMesh Mesh)> leaves, int depth)
    {
        foreach (var group in leaves.GroupBy(item => item.Parts[depth]).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var terminal = group.Where(item => item.Parts.Length == depth + 1).ToArray();
            if (terminal.Length > 0)
            {
                if (terminal.Length != 1 || group.Count() != 1) throw new InvalidDataException("Conflicting Aetheris occurrence paths.");
                yield return terminal[0].Mesh;
            }
            else
            {
                yield return Scene.Group(group.Key, Build(group, depth + 1));
            }
        }
    }
}

[JsonSerializable(typeof(AetherisSceneAsset))]
[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
public partial class AetherisAssetJsonContext : JsonSerializerContext;
