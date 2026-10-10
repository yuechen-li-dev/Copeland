using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelian.Assets.Models;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class StaticModelTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    [Fact]
    public void BlenderGlbRetainsIndexedPrimitivesTextureAndNamedMaterialSlots()
    {
        var result = GlbModelImporter.Load("crate", Asset("crate.glb"));
        Assert.True(result.Success, ModelAssetCatalog.Describe(result.Diagnostics));
        var model = result.Model!;
        Assert.Equal(2, model.Occurrences.Length);
        var panel = model.Occurrences.Single(item => item.Primitive.Material.Slot == "panel");
        Assert.Equal(24, panel.Primitive.Indices.Length);
        Assert.Equal(0.2f, panel.Primitive.Material.Metallic, 5);
        Assert.Equal(0.6f, panel.Primitive.Material.Roughness, 5);
        Assert.Equal(new Vector3(0, 0.75f, 0), panel.Transform.Translation);
        Assert.Equal(64, panel.Primitive.Material.BaseColorTexture!.Texture.Width);
        Assert.Contains(panel.Primitive.Vertices, item => item.Uv != Vector2.Zero);
        Assert.Contains(result.Diagnostics, item => item.Code == "AA3103" && !item.Fatal);
    }

    [Fact]
    public void BlenderNormalMapScaleTangentsAndEmissiveFactorsImportWithoutColorSpaceGuessing()
    {
        var result = GlbModelImporter.Load("crate", Asset("crate-channels.glb"));
        Assert.True(result.Success, ModelAssetCatalog.Describe(result.Diagnostics));
        var panel = result.Model!.Occurrences.Single(item => item.Primitive.Material.Slot == "panel").Primitive;
        Assert.True(panel.HasTangents);
        Assert.NotNull(panel.Material.NormalTexture);
        Assert.Equal(0.7f, panel.Material.NormalScale, 5);
        Assert.Equal(0.05f, panel.Material.Emissive.X, 5);
    }

    [Fact]
    public void EmissiveStrengthImportsHdrRadianceInsteadOfClampingToUnitColor()
    {
        using var files = new TemporaryAssets();
        byte[] model = Mutate(File.ReadAllBytes(Asset("crate.glb")), json =>
        {
            json["extensionsUsed"] = new JsonArray("KHR_materials_emissive_strength");
            json["materials"]![0]!["emissiveFactor"] = new JsonArray(.5, .25, .125);
            json["materials"]![0]!["extensions"] = new JsonObject
            {
                ["KHR_materials_emissive_strength"] = new JsonObject { ["emissiveStrength"] = 8 },
            };
        });
        File.WriteAllBytes(files.ModelPath, model);
        var result = GlbModelImporter.Load("crate", files.ModelPath);
        Assert.True(result.Success, ModelAssetCatalog.Describe(result.Diagnostics));
        Assert.Contains(result.Model!.Occurrences, occurrence => occurrence.Primitive.Material.Emissive == new Vector3(4, 2, 1));
    }

    [Fact]
    public void ContentIdentityIncludesImportSettingsAndIsIndependentOfPathAndStableId()
    {
        var first = GlbModelImporter.Load("first", Asset("crate.glb")).Model!;
        var second = GlbModelImporter.Load("second", Asset("crate.glb")).Model!;
        var scaled = GlbModelImporter.Load("first", Asset("crate.glb"), new(2)).Model!;
        Assert.Equal(first.ContentIdentity, second.ContentIdentity);
        Assert.NotEqual(first.ContentIdentity, scaled.ContentIdentity);
        Assert.Equal(1.5f, scaled.Occurrences[0].Transform.Translation.Y);
    }

    [Theory]
    [InlineData(0.001f)]
    [InlineData(0.01f)]
    public void SmallPositiveImportScalesRemainValidAffineFrames(float scale)
    {
        var result = GlbModelImporter.Load("crate", Asset("crate.glb"), new(scale));
        Assert.True(result.Success, ModelAssetCatalog.Describe(result.Diagnostics));
        Assert.Equal(0.75f * scale, result.Model!.Occurrences[0].Transform.Translation.Y, 6);
    }

    [Fact]
    public void BlendMaterialsRetainStandardBaseColorAlpha()
    {
        using var files = new TemporaryAssets();
        byte[] source = File.ReadAllBytes(Asset("crate.glb"));
        byte[] blend = Mutate(source, json =>
        {
            json["materials"]![0]!["alphaMode"] = "BLEND";
            json["materials"]![0]!["pbrMetallicRoughness"]!["baseColorFactor"] = new JsonArray(.2, .4, .8, .35);
        });
        File.WriteAllBytes(files.ModelPath, blend);
        var result = GlbModelImporter.Load("crate", files.ModelPath);
        Assert.True(result.Success, ModelAssetCatalog.Describe(result.Diagnostics));
        var material = result.Model!.Occurrences.Select(item => item.Primitive.Material).First(item => item.AlphaBlend);
        Assert.False(material.AlphaMask);
        Assert.Equal(.35f, material.BaseColor.W, 6);
    }

    [Theory]
    [InlineData("extension", "Unsupported GLB extension")]
    [InlineData("external", "embedded buffers and images")]
    [InlineData("animation", "animations")]
    [InlineData("normals", "NORMAL accessor")]
    [InlineData("uv", "TEXCOORD_0 accessor")]
    [InlineData("tangents", "TANGENT accessor")]
    [InlineData("primitive", "triangle primitives")]
    [InlineData("material-name", "Duplicate material name")]
    [InlineData("transform", "positive affine transform")]
    public void UnsupportedOrIncompleteAssetsFailWithActionableDiagnostics(string kind, string expected)
    {
        using var files = new TemporaryAssets();
        byte[] source = File.ReadAllBytes(Asset("crate.glb"));
        byte[] invalid = Mutate(source, json =>
        {
            JsonNode primitive = json["meshes"]![0]!["primitives"]![0]!;
            switch (kind)
            {
                case "extension": json["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression"); break;
                case "external": json["images"]![0]!["uri"] = "missing.png"; break;
                case "animation": json["animations"] = new JsonArray(new JsonObject()); break;
                case "normals": primitive["attributes"]!.AsObject().Remove("NORMAL"); break;
                case "uv": primitive["attributes"]!.AsObject().Remove("TEXCOORD_0"); break;
                case "tangents":
                    primitive["attributes"]!.AsObject().Remove("TANGENT");
                    json["materials"]![0]!["normalTexture"] = new JsonObject { ["index"] = 0 };
                    break;
                case "primitive": primitive["mode"] = 1; break;
                case "material-name": json["materials"]![1]!["name"] = "panel"; break;
                case "transform": json["nodes"]![0]!["scale"] = new JsonArray(-1, 1, 1); break;
            }
        });
        File.WriteAllBytes(files.ModelPath, invalid);
        var result = GlbModelImporter.Load("crate", files.ModelPath);
        Assert.False(result.Success);
        Assert.Null(result.Model);
        Assert.Contains(result.Diagnostics, item => item.Fatal && item.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ReimportPublishesOnlyAfterPreparationAndRetainsOldAssetOnFailure()
    {
        using var files = new TemporaryAssets();
        File.Copy(Asset("crate.glb"), files.ModelPath);
        var catalog = ModelAssetCatalog.Load(files.ManifestPath);
        var slot = catalog.Model("crate");
        var original = slot.Current;
        File.Copy(Asset("crate-replacement.glb"), files.ModelPath, true);
        var failed = catalog.Reimport("crate", candidate =>
        {
            Assert.Same(original, slot.Current);
            throw new InvalidOperationException("GPU preparation failed");
        });
        Assert.False(failed.Success);
        Assert.Same(original, slot.Current);
        Assert.Contains(failed.Diagnostics, item => item.Code == "AA3205" && item.Message.Contains("GPU preparation failed"));
        var accepted = catalog.Reimport("crate", candidate => Assert.Same(original, slot.Current));
        Assert.True(accepted.Success);
        Assert.Same(accepted.Model, slot.Current);
        File.Copy(Asset("crate-slot-loss.glb"), files.ModelPath, true);
        Assert.False(catalog.Reimport("crate").Success);
        Assert.Same(accepted.Model, slot.Current);
        File.WriteAllText(files.ModelPath, "broken candidate");
        Assert.False(catalog.Reimport("crate").Success);
        Assert.Same(accepted.Model, slot.Current);
    }

    [Fact]
    public void DeliveryBuildReimportsWithTheSameContentIdentityAndRecordsProvenance()
    {
        using var files = new TemporaryAssets();
        File.Copy(Asset("crate.glb"), files.ModelPath);
        var parsed = AssetManifestParser.Parse(File.ReadAllText(files.ManifestPath), files.ManifestPath);
        string output = Path.Combine(files.Root, "delivery");
        var built = ModelAssetBuilder.Build(parsed.Manifest!, files.ManifestPath, output);
        Assert.Single(built.Built);
        var delivery = ModelAssetCatalog.Load(Path.Combine(output, "models.toml"));
        Assert.Equal(built.Built[0].ContentIdentity, delivery.Model("crate").Current.ContentIdentity);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "models", "crate.import.json")));
        Assert.Equal(GlbModelImporter.Version, json.RootElement.GetProperty("Importer").GetString());
    }

    [Theory]
    [InlineData("../crate.glb", "crate")]
    [InlineData("crate.obj", "crate")]
    [InlineData("crate.glb", "bad id")]
    public void ManifestRejectsUnstableIdsAndUnsupportedPaths(string path, string id)
    {
        var manifest = new AssetManifest();
        manifest.Models.Add(new(id, path, new()));
        Assert.Contains(AssetManifestValidator.Validate(manifest, "assets.toml"), item => item.Fatal);
    }

    private static byte[] Mutate(byte[] glb, Action<JsonObject> change)
    {
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        JsonObject json = JsonNode.Parse(glb.AsSpan(20, length))!.AsObject();
        change(json);
        byte[] modified = System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());
        int padded = (modified.Length + 3) & ~3;
        byte[] result = new byte[20 + padded + glb.Length - 20 - length];
        glb.AsSpan(0, 20).CopyTo(result);
        result.AsSpan(20, padded).Fill(32);
        modified.CopyTo(result, 20);
        glb.AsSpan(20 + length).CopyTo(result.AsSpan(20 + padded));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)padded);
        return result;
    }

    private sealed class TemporaryAssets : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aurelian-model-tests-" + Guid.NewGuid().ToString("N"));
        public string ModelPath => Path.Combine(Root, "crate.glb");
        public string ManifestPath => Path.Combine(Root, "assets.toml");
        public TemporaryAssets()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ManifestPath, "[[models]]\nid = \"crate\"\npath = \"crate.glb\"\nscale = 1.0\n");
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
