using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelian.Assets.Lighting;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class StaticDiffuseLightingTests
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly float[] Vertices = [-1, -1, 1, -1, 1, 1, -1, 1, 0, 0];
    private static readonly int[] Triangles = [0, 1, 4, 0, 1, 2, 4, 0, 2, 3, 4, 0, 3, 0, 4, 0];

    [Fact]
    public void A_minimal_two_element_square_is_supported_and_degenerate_receiver_scales_are_rejected()
    {
        var mesh = new DiffuseReceiverMesh([-1, -1, 1, -1, 1, 1, -1, 1],
            [0, 1, 2, 0, 0, 2, 3, 0], new float[9 * 6]);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(Vector3.Zero, mesh.EvaluateNormalized(Vector2.Zero));
        Assert.Throws<ArgumentException>(() => new HorizontalDiffuseReceiver(0, 0, float.Epsilon, 1).Validate());
    }

    [Fact]
    public void Shared_quadratic_nodes_reproduce_constant_light_bases_across_the_whole_domain()
    {
        var asset = StaticDiffuseLightingAsset.Load(Envelope(Payload()), Key);
        for (int y = 0; y <= 16; y++)
        {
            for (int x = 0; x <= 16; x++)
            {
                Vector3 value = asset.Evaluate(new(x / 8f - 1, 0, y / 8f - 1), Vector3.UnitY,
                    new(.2f, .4f, .5f), sun: 2, emission: 4);
                Assert.True(Vector3.Distance(value, new(.8f, 2, 3.25f)) < .000001f);
            }
        }
        Assert.Equal(0, asset.Mesh.MaximumEdgeMismatch());
        Assert.Equal(4, asset.Mesh.TriangleCount);
        Assert.Equal(13, asset.Mesh.CoefficientCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.Evaluate(Vector3.Zero, Vector3.UnitZ, Vector3.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.Mesh.EvaluateNormalized(Vector2.Zero, -1));
    }

    [Theory]
    [InlineData("sceneKey", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("decoder", "future-decoder")]
    [InlineData("basis", "ambiguous-total")]
    [InlineData("colourSpace", "sRGB")]
    [InlineData("compilerKey", null)]
    public void Unsupported_or_stale_assets_have_a_named_diagnostic(string property, string? value)
    {
        JsonObject payload = Payload();
        payload[property] = value;
        var exception = Assert.Throws<InvalidDataException>(() => StaticDiffuseLightingAsset.Load(Envelope(payload), Key));
        Assert.StartsWith("AUR-LIGHT-001:", exception.Message);
    }

    [Fact]
    public void Corruption_reversed_elements_and_wrong_coefficient_counts_are_rejected_before_upload()
    {
        byte[] bytes = Envelope(Payload());
        var envelope = JsonNode.Parse(bytes)!.AsObject();
        envelope["sha256"] = "wrong";
        Assert.Throws<InvalidDataException>(() => StaticDiffuseLightingAsset.Load(Encoding.UTF8.GetBytes(envelope.ToJsonString()), Key));
        var reversed = Payload();
        reversed["triangles"]![0] = 1;
        reversed["triangles"]![1] = 0;
        Assert.Throws<InvalidDataException>(() => StaticDiffuseLightingAsset.Load(Envelope(reversed), Key));
        var missing = Payload();
        missing["weights"] = JsonSerializer.SerializeToNode(new float[6]);
        Assert.Throws<InvalidDataException>(() => StaticDiffuseLightingAsset.Load(Envelope(missing), Key));
    }

    [Fact]
    public void Runtime_mesh_copies_inputs_and_packs_shared_nodes_without_scene_shader_constants()
    {
        float[] positions = (float[])Vertices.Clone();
        float[] weights = Weights();
        var mesh = new DiffuseReceiverMesh(positions, Triangles, weights);
        positions[0] = 20;
        weights[0] = 20;
        Assert.Equal(-1, mesh.Vertices[0]);
        Assert.Equal(1, mesh.Weights[0]);
        using var provenance = JsonDocument.Parse("{}");
        var asset = new StaticDiffuseLighting(Key, new(-1, -1, 2, 2), Vector3.UnitY, mesh, Key, Key, Quality(), provenance.RootElement);
        float[] data = asset.TextureData();
        Assert.Equal(32 * 2 * 4, data.Length);
        Assert.Equal(4, data[5]);
        Assert.Equal(22, data[6]);
        Assert.Equal(2, data[7]);
        // The (0,4) interior edge has one global coefficient referenced by both incident elements.
        Assert.Equal(mesh.Nodes(0)[5], mesh.Nodes(3)[4]);
    }

    [Fact]
    public void Failed_quality_rank_and_budget_gates_cannot_be_loaded()
    {
        foreach (string property in new[] { "ValidationRmse", "ObservedRank", "NumericPayloadBytes" })
        {
            var payload = Payload();
            payload["quality"]![property] = property == "ObservedRank" ? 0 : 99999;
            Assert.Throws<InvalidDataException>(() => StaticDiffuseLightingAsset.Load(Envelope(payload), Key));
        }
    }

    [Fact]
    public void Scene_key_changes_for_static_geometry_materials_and_compilation_policy()
    {
        var body = new StaticDiffuseContributor("floor", [0, 0, 0, 1, 0, 0, 0, 0, 1], [.2f, .3f, .4f], [0, 0, 0]);
        var recipe = new StaticDiffuseLightingRecipe("scene", "floor", new(0, 0, 1, 1), [0, 1, 0], [body]);
        Assert.Equal(recipe.ContentKey, (recipe with { }).ContentKey);
        Assert.NotEqual(recipe.ContentKey, (recipe with { Contributors = [body with { Albedo = [.3f, .3f, .4f] }] }).ContentKey);
        Assert.NotEqual(recipe.ContentKey, (recipe with { Receiver = new(0, 0, 2, 1) }).ContentKey);
        Assert.NotEqual(recipe.ContentKey, (recipe with { MaximumRefinements = 0 }).ContentKey);
        Assert.Throws<ArgumentException>(() => (recipe with { SunDirection = [0, 2, 0] }).Validate());
    }

    private static float[] Weights()
    {
        var weights = new float[13 * 6];
        for (int index = 0; index < 13; index++)
            new float[] { 1, 2, 3, .5f, .25f, .125f }.CopyTo(weights, index * 6);
        return weights;
    }

    private static JsonObject Payload() => JsonSerializer.SerializeToNode(new
    {
        decoder = StaticDiffuseLighting.DecoderVersion, sceneKey = Key,
        receiver = new HorizontalDiffuseReceiver(-1, -1, 2, 2), sunDirection = new float[] { 0, 1, 0 },
        compilerKey = Key, trainingKey = Key, colourSpace = "scene-linear Rec.709",
        basis = "sun-indirect; emission-total; per-unit-receiver-albedo", provenance = new { },
        quality = Quality(), vertices = Vertices, triangles = Triangles, weights = Weights()
    })!.AsObject();

    private static StaticDiffuseLightingQuality Quality() => new(.001, .02, .0001, 556, 1024, 13, 13, 4096);

    private static byte[] Envelope(JsonObject payload)
    {
        string content = payload.ToJsonString();
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "aurelian.static-diffuse.asset/1", content,
            sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()
        });
    }
}
