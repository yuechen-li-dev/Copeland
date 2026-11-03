using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Aurelian.Assets;
using Aurelian.Assets.Models;
using Aurelian.Graphics.Vulkan.Native3D;

internal static partial class RenderingLodProof
{
    private static NativeMesh3D CompileAsset(string output, out object evidence)
    {
        string source = Path.Combine(output, "source");
        string delivery = Path.Combine(output, "delivery");
        Directory.CreateDirectory(source);
        WriteGlb(Path.Combine(source, "tree.glb"), RenderingR2Proof.Tree());
        string manifestPath = Path.Combine(source, "assets.toml");
        string text = "[[models]]\nid = \"forest.tree\"\npath = \"tree.glb\"\nlod = true\n";
        File.WriteAllText(manifestPath, text);
        var manifest = AssetManifestParser.Parse(text, manifestPath).Manifest!;
        var result = ModelAssetBuilder.Build(manifest, manifestPath, delivery);
        Require(!result.Diagnostics.Any(item => item.Fatal), ModelAssetCatalog.Describe(result.Diagnostics));
        string artifactPath = Path.Combine(delivery, "models", "forest.tree.lod.bin");
        byte[] first = File.ReadAllBytes(artifactPath);
        var repeated = ModelAssetBuilder.Build(manifest, manifestPath, delivery);
        Require(!repeated.Diagnostics.Any(item => item.Fatal) && first.SequenceEqual(File.ReadAllBytes(artifactPath)),
            "Offline delivery was not deterministic.");
        var model = ModelAssetCatalog.Load(Path.Combine(delivery, "models.toml")).Model("forest.tree").Current;
        var primitive = model.Occurrences.Single().Primitive;
        Require(primitive.Lods.Length >= 2 && primitive.Lods[^1].Indices.Length < primitive.Indices.Length * .4,
            "Offline tree did not produce materially cheaper prepared geometry.");
        var raw = GlbModelImporter.Load("forest.tree", Path.Combine(source, "tree.glb"));
        Require(raw.Success && raw.Model!.Occurrences.Single().Primitive.Lods.IsEmpty,
            "LOD generation was not opt-in.");
        Require(raw.Model!.ContentIdentity != model.ContentIdentity, "LOD settings did not affect asset identity.");
        evidence = new { Build = result.Built, model.ContentIdentity, DeterministicBytes = first.Length,
            ArtifactSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first)) };
        return NativeMesh3D.FromPrimitive(primitive);
    }

    private static void WriteGlb(string path, NativeMesh3D mesh)
    {
        byte[] vertices = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()).ToArray();
        byte[] indices = MemoryMarshal.AsBytes(mesh.Indices.AsSpan()).ToArray();
        Vector3 minimum = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Min);
        Vector3 maximum = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Max);
        object Accessor(int offset, string type) => new
        {
            bufferView = 0, byteOffset = offset, componentType = 5126, count = mesh.Vertices.Length, type,
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0", generator = "Aurelian LOD proof source" }, scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } }, nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new
                { POSITION = 0, NORMAL = 1, COLOR_0 = 2, TEXCOORD_0 = 3, TANGENT = 4 }, indices = 5, material = 0 } } } },
            materials = new[] { new { name = "tree", pbrMetallicRoughness = new { metallicFactor = 0, roughnessFactor = .8 } } },
            buffers = new[] { new { byteLength = vertices.Length + indices.Length } },
            bufferViews = new object[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = vertices.Length, byteStride = 64, target = 34962 },
                new { buffer = 0, byteOffset = vertices.Length, byteLength = indices.Length, target = 34963 },
            },
            accessors = new object[]
            {
                new { bufferView = 0, byteOffset = 0, componentType = 5126, count = mesh.Vertices.Length, type = "VEC3",
                    min = new[] { minimum.X, minimum.Y, minimum.Z }, max = new[] { maximum.X, maximum.Y, maximum.Z } },
                Accessor(12, "VEC3"), Accessor(24, "VEC4"), Accessor(40, "VEC2"), Accessor(48, "VEC4"),
                new { bufferView = 1, byteOffset = 0, componentType = 5125, count = mesh.Indices.Length, type = "SCALAR" },
            },
        });
        int jsonLength = (json.Length + 3) & ~3;
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonLength + 8 + vertices.Length + indices.Length));
        writer.Write(jsonLength);
        writer.Write(0x4E4F534Au);
        writer.Write(json);
        for (int pad = json.Length; pad < jsonLength; pad++) writer.Write((byte)32);
        writer.Write(vertices.Length + indices.Length);
        writer.Write(0x004E4942u);
        writer.Write(vertices);
        writer.Write(indices);
    }
}
