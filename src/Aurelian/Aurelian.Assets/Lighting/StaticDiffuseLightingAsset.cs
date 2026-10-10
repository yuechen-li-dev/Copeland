using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Lighting;

namespace Aurelian.Assets.Lighting;

public static class StaticDiffuseLightingAsset
{
    public static StaticDiffuseLighting Load(string path, string expectedSceneKey)
    {
        if (new FileInfo(path).Length > 2_000_000)
            throw new InvalidDataException("AUR-LIGHT-001: Lighting asset exceeds the bounded runtime format.");
        return Load(File.ReadAllBytes(path), expectedSceneKey);
    }

    public static StaticDiffuseLighting Load(ReadOnlyMemory<byte> bytes, string expectedSceneKey)
    {
        try
        {
            if (bytes.Length > 2_000_000) throw new InvalidDataException("Lighting asset exceeds the bounded runtime format.");
            using var envelope = JsonDocument.Parse(bytes);
            var root = envelope.RootElement;
            if (root.GetProperty("schema").GetString() != "aurelian.static-diffuse.asset/1")
                throw new InvalidDataException("Unsupported lighting asset schema.");
            string content = Text(root, "content");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            if (hash != root.GetProperty("sha256").GetString()) throw new InvalidDataException("Lighting payload checksum mismatch.");
            using var payload = JsonDocument.Parse(content);
            var data = payload.RootElement;
            if (data.GetProperty("decoder").GetString() != StaticDiffuseLighting.DecoderVersion
                || data.GetProperty("colourSpace").GetString() != "scene-linear Rec.709"
                || data.GetProperty("basis").GetString() != "sun-indirect; emission-total; per-unit-receiver-albedo"
                || data.GetProperty("sceneKey").GetString() != expectedSceneKey)
                throw new InvalidDataException("Stale scene or unsupported decoder/colour/basis identity.");
            var receiver = data.GetProperty("receiver");
            var domain = new HorizontalDiffuseReceiver(receiver.GetProperty("MinimumX").GetSingle(),
                receiver.GetProperty("MinimumZ").GetSingle(), receiver.GetProperty("Width").GetSingle(),
                receiver.GetProperty("Depth").GetSingle(), receiver.GetProperty("Height").GetSingle());
            float[] sun = Floats(data, "sunDirection");
            if (sun.Length != 3) throw new InvalidDataException("Sun direction shape mismatch.");
            var mesh = new DiffuseReceiverMesh(Floats(data, "vertices"),
                data.GetProperty("triangles").EnumerateArray().Select(value => value.GetInt32()), Floats(data, "weights"));
            var measurement = data.GetProperty("quality");
            var quality = new StaticDiffuseLightingQuality(measurement.GetProperty("ValidationRmse").GetDouble(),
                measurement.GetProperty("MaximumValidationRmse").GetDouble(), measurement.GetProperty("SamplingNoise").GetDouble(),
                measurement.GetProperty("NumericPayloadBytes").GetInt32(), measurement.GetProperty("NumericBudgetBytes").GetInt32(),
                measurement.GetProperty("ObservedRank").GetInt32(), measurement.GetProperty("CoefficientCount").GetInt32(),
                measurement.GetProperty("ValidationReceivers").GetInt32());
            return new StaticDiffuseLighting(expectedSceneKey, domain, new(sun[0], sun[1], sun[2]), mesh,
                Text(data, "compilerKey"), Text(data, "trainingKey"), quality,
                data.GetProperty("provenance"));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException
            or InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("AUR-LIGHT-001: " + exception.Message, exception);
        }
    }

    private static float[] Floats(JsonElement data, string name) =>
        data.GetProperty(name).EnumerateArray().Select(value => value.GetSingle()).ToArray();

    private static string Text(JsonElement data, string name) => data.GetProperty(name).GetString()
        ?? throw new InvalidDataException("Lighting asset requires a non-null " + name + ".");
}
