using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class SceneLightingExpertTests
{
    [Fact]
    public void Loading_rejects_stale_scene_decoder_and_corrupt_weights()
    {
        byte[] valid = Artifact();
        var expert = SceneLightingExpert.Load(valid, "scene", "decoder");
        Assert.Throws<InvalidDataException>(() => SceneLightingExpert.Load(valid, "new-scene", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLightingExpert.Load(valid, "scene", "new-decoder"));
        string changed = System.Text.Encoding.UTF8.GetString(valid).Replace(expert.WeightsKey, new string('0', 64), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SceneLightingExpert.Load(System.Text.Encoding.UTF8.GetBytes(changed), "scene", "decoder"));
        Assert.Throws<InvalidDataException>(() => SceneLightingExpert.Load("{}"u8.ToArray(), "scene", "decoder"));
    }

    [Fact]
    public void Coefficients_are_immutable_and_runtime_light_coefficients_remain_linear()
    {
        var expert = SceneLightingExpert.Load(Artifact(), "scene", "decoder");
        float[] copy = expert.Coefficients("network");
        copy[0] = 99;
        Vector3 first = expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.Neural, 1, 0);
        Vector3 second = expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.Neural, 0, 1);
        Assert.Equal(new(.2f, .3f, .4f), first);
        Assert.Equal(first * 2 + second * .3f,
            expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.Neural, 2, .3f));
    }

    [Fact]
    public void Queries_outside_the_compiled_receiver_or_with_invalid_coefficients_fail()
    {
        var expert = SceneLightingExpert.Load(Artifact(), "scene", "decoder");
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(new(3, 0), LightingExpertRepresentation.Neural));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(new(float.NaN, 0), LightingExpertRepresentation.Neural));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.Neural, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => expert.Evaluate(Vector2.Zero, LightingExpertRepresentation.GeometryLocal));
    }

    [Fact]
    public void Expert_selection_applies_validity_and_measured_quality_before_cost()
    {
        LightingExpertQualification[] candidates =
        [
            new("stale", LightingExpertRepresentation.Neural, 0, .001),
            new("scene", LightingExpertRepresentation.CoarseGrid, .2, .01),
            new("scene", LightingExpertRepresentation.Hybrid, .01, .2),
            new("scene", LightingExpertRepresentation.Neural, .02, .1),
        ];
        Assert.Equal(LightingExpertRepresentation.Neural, SceneLightingExpert.Select(candidates, "scene", .03).Representation);
        Assert.Throws<InvalidOperationException>(() => SceneLightingExpert.Select(candidates, "different-scene", .03));
        Assert.Throws<InvalidOperationException>(() => SceneLightingExpert.Select(candidates, "scene", .001));
    }

    private static byte[] Artifact()
    {
        var arrays = new Dictionary<string, float[]>
        {
            ["features"] = new float[96],
            ["polynomial"] = new float[36],
            ["network"] = new float[192],
            ["grid"] = new float[216],
            ["residual"] = new float[192],
        };
        arrays["network"][0] = .2f;
        arrays["network"][1] = .3f;
        arrays["network"][2] = .4f;
        arrays["network"][3] = 1;
        arrays["network"][4] = .5f;
        arrays["network"][5] = .25f;
        using var stream = new MemoryStream();
        foreach (float[] values in arrays.Values)
        {
            stream.Write(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        var manifest = new Dictionary<string, object>
        {
            ["schema"] = "aurelian.scene-expert/1",
            ["sceneKey"] = "scene",
            ["decoderKey"] = "decoder",
            ["width"] = SceneLightingExpert.Width,
            ["gridSize"] = SceneLightingExpert.GridSize,
            ["colourSpace"] = "scene-linear Rec.709",
            ["domain"] = "visible floor, Y=0, X/Z in [-2.7,2.7] metres",
            ["basis"] = "unit sun indirect RGB; unit lamp direct-plus-indirect RGB",
            ["weightsSha256"] = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant(),
        };
        foreach (var item in arrays)
        {
            manifest.Add(item.Key, item.Value);
        }
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }
}
