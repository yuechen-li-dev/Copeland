using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class SceneLocalLightingExpertTests
{
    [Fact]
    public void Routing_cycles_invalid_indices_and_repeated_leaves_fail_even_with_valid_checksums()
    {
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(Artifact(children => children[0] = 0), "scene", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(Artifact(children => children[0] = 9), "scene", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(Artifact(children => children[7] = -1), "scene", "decoder"));
    }

    [Fact]
    public void Reloaded_routing_owns_coefficients_and_uses_explicit_plane_ties()
    {
        var expert = SceneLocalLightingExpert.Load(Artifact(), "scene", "decoder");
        Assert.Equal(0, expert.Route(new(-.5f, -.5f)));
        Assert.Equal(0, expert.Route(Vector2.Zero));
        Assert.Equal(7, expert.Route(new(.5f, .5f)));
        int[] children = expert.Children();
        children[0] = 0;
        float[] weights = expert.Coefficients("weights");
        weights[0] = 100;
        Assert.Equal(new(.2f, .3f, .4f), expert.Evaluate(new(-1, -1), LightingExpertRepresentation.GeometryLocal, 1, 0));
        Assert.Equal(0, expert.Route(new(-.5f, -.5f)));
    }

    [Fact]
    public void Local_model_identity_shape_and_checksum_are_required()
    {
        byte[] valid = Artifact();
        var expert = SceneLocalLightingExpert.Load(valid, "scene", "decoder");
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(valid, "changed", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(valid, "scene", "changed"));
        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(valid)!;
        manifest["weightsSha256"] = JsonSerializer.SerializeToElement(new string('0', 64));
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(JsonSerializer.SerializeToUtf8Bytes(manifest), "scene", "decoder"));
        manifest["grid"] = JsonSerializer.SerializeToElement(new float[3]);
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load(JsonSerializer.SerializeToUtf8Bytes(manifest), "scene", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLocalLightingExpert.Load("{}"u8.ToArray(), "scene", "decoder"));
        Assert.NotEmpty(expert.WeightsKey);
    }

    [Theory]
    [InlineData(LightingExpertRepresentation.GeometryLocal)]
    [InlineData(LightingExpertRepresentation.UniformLocal)]
    [InlineData(LightingExpertRepresentation.MatchedGrid)]
    public void Both_light_bases_are_linear_for_every_local_representation(LightingExpertRepresentation representation)
    {
        var expert = SceneLocalLightingExpert.Load(Artifact(), "scene", "decoder");
        Vector2 point = new(-1, -1);
        Vector3 sun = expert.Evaluate(point, representation, 1, 0);
        Vector3 lamp = expert.Evaluate(point, representation, 0, 1);
        Assert.True(sun.Length() > 0 && lamp.Length() > 0);
        Assert.Equal(sun * 2 + lamp * .3f, expert.Evaluate(point, representation, 2, .3f));
    }

    [Fact]
    public void Invalid_receiver_queries_and_cross_profile_modes_are_rejected()
    {
        var expert = SceneLocalLightingExpert.Load(Artifact(), "scene", "decoder");
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(new(3, 0), LightingExpertRepresentation.GeometryLocal));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.Hybrid));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.GeometryLocal, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Route(new(float.NaN, 0)));
    }

    private static byte[] Artifact(Action<int[]>? changeRouting = null)
    {
        int[] children = [1, 2, 3, 4, 5, 6, -1, -2, -3, -4, -5, -6, -7, -8];
        changeRouting?.Invoke(children);
        float[] planes = new float[21];
        for (int node = 0; node < 7; node++)
        {
            planes[node * 3 + 1] = 1;
        }
        var arrays = new Dictionary<string, float[]>
        {
            ["planes"] = planes,
            ["transforms"] = new float[32],
            ["weights"] = new float[480],
            ["uniformTransforms"] = new float[32],
            ["uniformWeights"] = new float[480],
            ["grid"] = new float[600],
        };
        for (int leaf = 0; leaf < 8; leaf++)
        {
            foreach (string name in new[] { "transforms", "uniformTransforms" })
            {
                arrays[name][leaf * 4 + 2] = 1;
                arrays[name][leaf * 4 + 3] = 1;
            }
            foreach (string name in new[] { "weights", "uniformWeights" })
            {
                float[] values = [.2f, .3f, .4f, .7f, .6f, .5f];
                Array.Copy(values, 0, arrays[name], leaf * 60, 6);
            }
        }
        Array.Fill(arrays["grid"], .2f);
        using var bytes = new MemoryStream();
        foreach (string name in new[] { "planes", "children", "transforms", "weights", "uniformTransforms", "uniformWeights", "grid" })
        {
            if (name == "children")
            {
                bytes.Write(MemoryMarshal.AsBytes(children.AsSpan()));
            }
            else
            {
                bytes.Write(MemoryMarshal.AsBytes(arrays[name].AsSpan()));
            }
        }
        var manifest = new Dictionary<string, object>
        {
            ["schema"] = "aurelian.local-expert/1",
            ["sceneKey"] = "scene",
            ["decoderKey"] = "decoder",
            ["leaves"] = 8,
            ["features"] = 10,
            ["gridSize"] = 10,
            ["basis"] = "unit sun indirect RGB; unit lamp direct-plus-indirect RGB",
            ["colourSpace"] = "scene-linear Rec.709",
            ["domain"] = "visible floor, Y=0, X/Z in [-2.7,2.7] metres",
            ["weightsSha256"] = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(),
            ["children"] = children,
        };
        foreach (var array in arrays)
        {
            manifest.Add(array.Key, array.Value);
        }
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }
}
