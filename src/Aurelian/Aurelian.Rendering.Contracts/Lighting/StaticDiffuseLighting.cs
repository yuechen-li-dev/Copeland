using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Aurelian.Rendering.Contracts.Lighting;

/// <summary>One explicit horizontal +Y receiver in game metres.</summary>
public sealed record HorizontalDiffuseReceiver(float MinimumX, float MinimumZ, float Width, float Depth, float Height = 0)
{
    public void Validate()
    {
        float[] values = [MinimumX, MinimumZ, Width, Depth, Height];
        if (values.Any(value => !float.IsFinite(value)) || Width < .001f || Depth < .001f
            || Width > 1000 || Depth > 1000)
        {
            throw new ArgumentException("Lighting receiver requires a finite positive bounded horizontal rectangle.");
        }
    }

    public bool Contains(Vector3 point, Vector3 normal) => normal.Y > .999f
        && Math.Abs(point.Y - Height) < .001f && point.X >= MinimumX && point.X <= MinimumX + Width
        && point.Z >= MinimumZ && point.Z <= MinimumZ + Depth;

    public Vector2 Normalize(Vector3 point) => new((point.X - MinimumX) * 2 / Width - 1,
        (point.Z - MinimumZ) * 2 / Depth - 1);
}

/// <summary>Opaque Lambertian triangles. Per-face albedo supports static colored receiver meshes.</summary>
public sealed record StaticDiffuseContributor(string Id, float[] Positions, float[] Albedo, float[] Emission)
{
    public float[] FaceAlbedos { get; init; } = [];
}

/// <summary>Authoring input, never a runtime scene oracle. The emitter basis scales all declared emission together.</summary>
public sealed record StaticDiffuseLightingRecipe(string Id, string ReceiverId, HorizontalDiffuseReceiver Receiver,
    float[] SunDirection, StaticDiffuseContributor[] Contributors)
{
    public int NumericBudgetBytes { get; init; } = 3772;
    public int MaximumRefinements { get; init; } = 6;
    public double Ridge { get; init; } = .000001;
    public double MaximumValidationRmse { get; init; } = .02;

    public string ContentKey
    {
        get
        {
            Validate();
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this,
                new JsonSerializerOptions { IgnoreReadOnlyProperties = true }))).ToLowerInvariant();
        }
    }

    public void Validate()
    {
        Receiver.Validate();
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(ReceiverId)
            || SunDirection.Length != 3 || SunDirection.Any(value => !float.IsFinite(value))
            || Math.Abs(new Vector3(SunDirection[0], SunDirection[1], SunDirection[2]).Length() - 1) > .00001
            || NumericBudgetBytes is < 1024 or > 65536 || MaximumRefinements is < 0 or > 12
            || !double.IsFinite(Ridge) || Ridge <= 0 || !double.IsFinite(MaximumValidationRmse) || MaximumValidationRmse <= 0
            || Contributors.Length is < 1 or > 128 || Contributors.Select(body => body.Id).Distinct().Count() != Contributors.Length
            || Contributors.Count(body => body.Id == ReceiverId) != 1)
        {
            throw new ArgumentException("Invalid static diffuse lighting recipe or budget.");
        }
        foreach (var body in Contributors)
        {
            if (string.IsNullOrWhiteSpace(body.Id) || body.Positions.Length is < 9 or > 196608 || body.Positions.Length % 9 != 0
                || body.Positions.Any(value => !float.IsFinite(value)) || body.Albedo.Length != 3 || body.Emission.Length != 3
                || body.Albedo.Any(value => !float.IsFinite(value) || value < 0 || value > 1)
                || body.Emission.Any(value => !float.IsFinite(value) || value < 0)
                || (body.FaceAlbedos.Length != 0 && body.FaceAlbedos.Length != body.Positions.Length / 3)
                || body.FaceAlbedos.Any(value => !float.IsFinite(value) || value <= 0 || value > 1))
            {
                throw new ArgumentException("Unsupported Lambertian contributor: " + body.Id);
            }
        }
    }
}

/// <summary>Measured acceptance against limits declared before fitting.</summary>
public sealed record StaticDiffuseLightingQuality(double ValidationRmse, double MaximumValidationRmse,
    double SamplingNoise, int NumericPayloadBytes, int NumericBudgetBytes, int ObservedRank,
    int CoefficientCount, int ValidationReceivers)
{
    public void Validate(DiffuseReceiverMesh mesh)
    {
        int minimumPayload = 4 * (mesh.Vertices.Length + mesh.Triangles.Length + mesh.Weights.Length);
        if (!double.IsFinite(ValidationRmse) || ValidationRmse < 0
            || !double.IsFinite(MaximumValidationRmse) || MaximumValidationRmse <= 0 || ValidationRmse > MaximumValidationRmse
            || !double.IsFinite(SamplingNoise) || SamplingNoise < 0 || NumericPayloadBytes < minimumPayload
            || NumericPayloadBytes > NumericBudgetBytes || NumericBudgetBytes is < 1024 or > 65536
            || CoefficientCount != mesh.CoefficientCount || ObservedRank != CoefficientCount
            || ValidationReceivers is < 1 or > 4096)
        {
            throw new InvalidDataException("Compiled diffuse quality, rank or storage gate failed.");
        }
    }
}

/// <summary>Validated scene-linear radiance per unit receiver albedo: sun indirect and static-emitter total.</summary>
public sealed class StaticDiffuseLighting
{
    public const string DecoderVersion = "aurelian.static-diffuse.p2/1";
    public StaticDiffuseLighting(string sceneKey, HorizontalDiffuseReceiver receiver, Vector3 sunDirection,
        DiffuseReceiverMesh mesh, string compilerKey, string trainingKey, StaticDiffuseLightingQuality quality,
        JsonElement provenance)
    {
        receiver.Validate();
        foreach (string key in new[] { sceneKey, compilerKey, trainingKey })
        {
            if (key.Length != 64 || !key.All(Uri.IsHexDigit))
                throw new InvalidDataException("Compiled diffuse content identities must be SHA-256.");
        }
        if (mesh.Degree != 2 || mesh.Bubble || !float.IsFinite(sunDirection.LengthSquared())
            || Math.Abs(sunDirection.Length() - 1) > .00001)
            throw new InvalidDataException("Static diffuse runtime supports quadratic C0 elements and a unit sun direction.");
        quality.Validate(mesh);
        SceneKey = sceneKey;
        Receiver = receiver;
        SunDirection = sunDirection;
        Mesh = mesh;
        CompilerKey = compilerKey;
        TrainingKey = trainingKey;
        Quality = quality;
        Provenance = provenance.Clone();
    }

    public string SceneKey { get; }
    public HorizontalDiffuseReceiver Receiver { get; }
    public Vector3 SunDirection { get; }
    public DiffuseReceiverMesh Mesh { get; }
    public string CompilerKey { get; }
    public string TrainingKey { get; }
    public StaticDiffuseLightingQuality Quality { get; }
    public JsonElement Provenance { get; }

    public Vector3 Evaluate(Vector3 point, Vector3 normal, Vector3 albedo, float sun = 1, float emission = 1)
    {
        if (!Receiver.Contains(point, normal)) throw new ArgumentOutOfRangeException(nameof(point));
        if (!float.IsFinite(albedo.LengthSquared()) || albedo.X < 0 || albedo.Y < 0 || albedo.Z < 0)
            throw new ArgumentOutOfRangeException(nameof(albedo));
        return Mesh.EvaluateNormalized(Receiver.Normalize(point), sun, emission) * albedo;
    }

    /// <summary>RGBA32F data ABI: two header pixels, five pixels per triangle, two per shared coefficient.</summary>
    public float[] TextureData()
    {
        const int width = 32;
        int coefficientStart = 2 + Mesh.TriangleCount * 5;
        int height = (coefficientStart + Mesh.CoefficientCount * 2 + width - 1) / width;
        var pixels = new float[width * height * 4];
        float[] header = [Receiver.MinimumX, Receiver.MinimumZ, 2 / Receiver.Width, 2 / Receiver.Depth,
            Receiver.Height, Mesh.TriangleCount, coefficientStart, height];
        header.CopyTo(pixels, 0);
        for (int triangle = 0; triangle < Mesh.TriangleCount; triangle++)
        {
            var matrix = Mesh.Matrix(triangle);
            var nodes = Mesh.Nodes(triangle);
            int start = (2 + triangle * 5) * 4;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    pixels[start + row * 4 + column] = (float)matrix[row * 3 + column];
            for (int node = 0; node < 6; node++) pixels[start + 12 + node] = nodes[node];
        }
        var weights = Mesh.Weights;
        for (int coefficient = 0; coefficient < Mesh.CoefficientCount; coefficient++)
            for (int basis = 0; basis < 2; basis++)
                for (int channel = 0; channel < 3; channel++)
                    pixels[(coefficientStart + coefficient * 2 + basis) * 4 + channel]
                        = weights[coefficient * 6 + basis * 3 + channel];
        return pixels;
    }
}
