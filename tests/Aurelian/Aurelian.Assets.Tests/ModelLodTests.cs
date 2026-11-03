using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class ModelLodTests
{
    [Fact]
    public void DeliveryManifestRoundTripsAndFailedReimportKeepsThePublishedModel()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-lod-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "crate.glb"), Path.Combine(directory, "crate.glb"));
            string manifestPath = Path.Combine(directory, "assets.toml");
            string text = "[[models]]\nid = \"crate\"\npath = \"crate.glb\"\nlod = true\n";
            File.WriteAllText(manifestPath, text);
            var parsed = AssetManifestParser.Parse(text, manifestPath);
            Assert.Empty(parsed.Diagnostics);
            string delivery = Path.Combine(directory, "delivery");
            var built = ModelAssetBuilder.Build(parsed.Manifest!, manifestPath, delivery);
            Assert.DoesNotContain(built.Diagnostics, diagnostic => diagnostic.Fatal);
            Assert.Equal(ModelLodCompiler.Version, built.Built.Single().LodCompiler);
            var catalog = ModelAssetCatalog.Load(Path.Combine(delivery, "models.toml"));
            var slot = catalog.Model("crate");
            var previous = slot.Current;
            Assert.Equal(built.Built.Single().ContentIdentity, previous.ContentIdentity);
            File.WriteAllBytes(Path.Combine(delivery, "models", "crate.lod.bin"), new byte[40]);
            Assert.False(catalog.Reimport("crate").Success);
            Assert.Same(previous, slot.Current);
            var invalid = AssetManifestParser.Parse(text + "lod_file = \"../bad.lod.bin\"\n", manifestPath);
            Assert.Contains(ModelAssetCatalog.Validate(invalid.Manifest!, manifestPath), diagnostic => diagnostic.Code == "AA3207");
            Assert.Contains(AssetManifestParser.Parse(text.Replace("lod = true", "lod = 1"), manifestPath).Diagnostics,
                diagnostic => diagnostic.Fatal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompilationIsDeterministicAndRetainsOpenBordersAndAttributes()
    {
        var source = Grid();
        var first = ModelLodCompiler.Compile(source);
        var second = ModelLodCompiler.Compile(source);
        Assert.NotEmpty(first.Lods);
        Assert.True(first.Lods[^1].Indices.Length < source.Indices.Length / 2);
        Assert.Equal(source.Vertices, first.Vertices);
        Assert.Equal(source.Indices, first.Indices);
        for (int level = 0; level < first.Lods.Length; level++)
        {
            Assert.Equal(first.Lods[level].Indices.ToArray(), second.Lods[level].Indices.ToArray());
            Assert.Equal(first.Lods[level].Error, second.Lods[level].Error);
            var referenced = first.Lods[level].Indices.ToHashSet();
            for (int vertex = 0; vertex < source.Vertices.Length; vertex++)
            {
                var position = source.Vertices[vertex].Position;
                if (position.X is 0 or 16 || position.Z is 0 or 16) Assert.Contains(vertex, referenced);
            }
        }
    }

    [Fact]
    public void DeliveryChecksIdentityChecksumAndTopologyWithoutRecompiling()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-lod-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var primitive = ModelLodCompiler.Compile(Grid());
            var model = new StaticModel("grid", "source-identity", [new("grid", primitive, Matrix4x4.Identity)]);
            string path = Path.Combine(directory, "grid.lod.bin");
            ModelLodArtifact.Write(path, model);
            byte[] first = File.ReadAllBytes(path);
            ModelLodArtifact.Write(path, model);
            Assert.Equal(first, File.ReadAllBytes(path));
            var raw = new StaticModel("grid", "source-identity", [new("grid", primitive with { Lods = [] }, Matrix4x4.Identity)]);
            var delivered = ModelLodArtifact.Read(path, raw);
            Assert.Equal(primitive.Lods[^1].Indices.ToArray(), delivered.Occurrences[0].Primitive.Lods[^1].Indices.ToArray());
            var stale = new StaticModel("grid", "different-source", raw.Occurrences);
            Assert.Contains("AUR-LOD-006", Assert.Throws<InvalidDataException>(() => ModelLodArtifact.Read(path, stale)).Message);
            first[20] ^= 1;
            File.WriteAllBytes(path, first);
            Assert.Contains("AUR-LOD-005", Assert.Throws<InvalidDataException>(() => ModelLodArtifact.Read(path, raw)).Message);
            var invalid = primitive with { Lods = [new([0, 1, 999999], .1f)] };
            Assert.Throws<InvalidDataException>(() => new StaticModel("bad", "bad", [new("bad", invalid, Matrix4x4.Identity)]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpticsAndMalformedInputsHaveExplicitBoundaries()
    {
        var source = Grid();
        Assert.Empty(ModelLodCompiler.Compile(source with { Material = source.Material with { AlphaBlend = true } }).Lods);
        Assert.Throws<InvalidDataException>(() => ModelLodCompiler.Compile(source with { Indices = [0, 1, -1] }));
        Assert.Throws<InvalidDataException>(() => ModelLodCompiler.Compile(source with
        {
            Vertices = source.Vertices.SetItem(0, source.Vertices[0] with { Position = new(float.NaN, 0, 0) }),
        }));
    }

    private static ModelPrimitive Grid()
    {
        var vertices = ImmutableArray.CreateBuilder<ModelVertex>();
        var indices = ImmutableArray.CreateBuilder<int>();
        for (int z = 0; z <= 16; z++)
        {
            for (int x = 0; x <= 16; x++)
            {
                vertices.Add(new(new(x, 0, z), Vector3.UnitY, new(x / 16f, z / 16f),
                    new(1, 0, 0, 1), new(x / 16f, z / 16f, 0, 1)));
                if (x < 16 && z < 16)
                {
                    int start = z * 17 + x;
                    indices.AddRange(new[] { start, start + 17, start + 1, start + 1, start + 17, start + 18 });
                }
            }
        }
        return new("grid", vertices.ToImmutable(), indices.ToImmutable(), new("grid") { Metallic = 0 });
    }
}
