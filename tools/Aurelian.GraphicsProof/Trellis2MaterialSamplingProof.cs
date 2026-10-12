using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2MaterialSamplingProof
{
    public static void Run(string weightsDirectory, string featuresDirectory, string shapeDirectory, string output,
        int attentionQueryTileRows = 256)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "sampling-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"MaterialsDecoded\":false}");
        string flowPath = Path.Combine(weightsDirectory, Trellis2MaterialWeights.FlowName);
        Trellis2OccupancyWeights.Validate(flowPath, Trellis2MaterialWeights.FlowBytes, Trellis2MaterialWeights.FlowHash);
        string pipelinePath = Path.Combine(weightsDirectory, "pipeline.json");
        Trellis2OccupancyWeights.Validate(pipelinePath, 4186, Trellis2ShapeWeights.PipelineHash);
        using var pipeline = JsonDocument.Parse(File.ReadAllText(pipelinePath));
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        using var featureEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(featuresDirectory, "reference-evidence.json")));
        if (!featureEvidence.RootElement.GetProperty("Accepted").GetBoolean()
            || featureEvidence.RootElement.GetProperty("FeatureSha256").GetString() != Hash(featureBytes)
            || featureBytes.Length != 1029 * 1024 * 4)
        {
            throw new InvalidDataException("Material flow requires qualified hash-matched DINO features.");
        }
        byte[] shapeBytes = File.ReadAllBytes(Path.Combine(shapeDirectory, "decoder-latent.f32"));
        using var shapeEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(shapeDirectory, "sampling-evidence.json")));
        if (!shapeEvidence.RootElement.GetProperty("Accepted").GetBoolean()
            || shapeEvidence.RootElement.GetProperty("DecoderLatentSha256").GetString() != Hash(shapeBytes)
            || shapeEvidence.RootElement.GetProperty("ModelEvaluations").GetInt32() != 21)
        {
            throw new InvalidDataException("Material flow requires completed hash-matched shape generation.");
        }
        var coordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(shapeDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing shape coordinates.");
        _ = SparseVoxelTopology.Create(32, coordinates);
        if (coordinates.Length is < 1 or > 8192 || shapeBytes.Length != coordinates.Length * 32 * 4)
        {
            throw new InvalidDataException("Material flow requires matching bounded shape feature/coordinate rows.");
        }
        var arguments = pipeline.RootElement.GetProperty("args");
        var shapeNormalization = arguments.GetProperty("shape_slat_normalization");
        var textureNormalization = arguments.GetProperty("tex_slat_normalization");
        float[] shapeMean = Vector(shapeNormalization, "mean");
        float[] shapeStd = Vector(shapeNormalization, "std", positive: true);
        float[] textureMean = Vector(textureNormalization, "mean");
        float[] textureStd = Vector(textureNormalization, "std", positive: true);
        float[] condition = MemoryMarshal.Cast<byte, float>(shapeBytes).ToArray();
        for (int index = 0; index < condition.Length; index++)
        {
            condition[index] = (condition[index] - shapeMean[index % 32]) / shapeStd[index % 32];
        }
        File.WriteAllBytes(Path.Combine(output, "shape-condition.f32"), MemoryMarshal.AsBytes(condition.AsSpan()).ToArray());
        File.WriteAllText(Path.Combine(output, "coordinates.json"), JsonSerializer.Serialize(coordinates));
        using var weights = new SafeTensorFile(flowPath);
        if (weights.Tensors.Count != 640 || !weights.Tensors["input_layer.weight"].Shape.SequenceEqual(new[] { 1536, 64 })
            || !weights.Tensors["out_layer.weight"].Shape.SequenceEqual(new[] { 32, 1536 }))
        {
            throw new InvalidDataException("Unexpected shape-conditioned texture flow inventory.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS material sampler"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        if (attentionQueryTileRows is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(attentionQueryTileRows), "Material query tile must be 1..256 rows.");
        }
        math.AttentionQueryTileRows = attentionQueryTileRows;
        using var feedforward = new DominatusComputeFeedforward();
        math.StreamController = feedforward;
        using var position = math.CreateTensor(coordinates.Length, 3);
        math.Upload(position, coordinates.SelectMany(value => new float[] { value.X, value.Y, value.Z }).ToArray());
        using var context = math.CreateTensor(1029, 1024);
        math.Upload(context, MemoryMarshal.Cast<byte, float>(featureBytes));
        var batch = math.CreateBatch();
        batch.RoundBFloat16(context, context);
        math.Execute(batch);
        var model = new Trellis2FlowModel(math, weights, position, concatenatedConditioning: condition);
        var sampler = new Trellis2EulerSampler(model, timeRescale: 3, guidanceRescale: 0, unityGuidance: true);
        var schedule = new DominatusComputeSchedule("trellis.material") { Observe = InferenceProofLog.Write };
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(sampler.Sample(context, context, output, seed: 73120));
            float[] latent = sampler.Result ?? throw new InvalidDataException("No material latent.");
            if (sampler.ModelEvaluations != 12 || !latent.All(float.IsFinite))
            {
                throw new InvalidDataException("Material sampling did not complete twelve finite predictions.");
            }
            File.WriteAllBytes(Path.Combine(output, "latent.f32"), MemoryMarshal.AsBytes(latent.AsSpan()).ToArray());
            var decoderLatent = new float[latent.Length];
            for (int index = 0; index < latent.Length; index++)
            {
                decoderLatent[index] = latent[index] * textureStd[index % 32] + textureMean[index % 32];
            }
            File.WriteAllBytes(Path.Combine(output, "decoder-latent.f32"), MemoryMarshal.AsBytes(decoderLatent.AsSpan()).ToArray());
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, MaterialsDecoded = false, FullGenerationQualified = false,
                WeightSha256 = Trellis2MaterialWeights.FlowHash, FeatureSha256 = Hash(featureBytes),
                ShapeDecoderLatentSha256 = Hash(shapeBytes),
                DecoderLatentSha256 = Hash(MemoryMarshal.AsBytes(decoderLatent.AsSpan())),
                CoordinateSha256 = Hash(File.ReadAllBytes(Path.Combine(output, "coordinates.json"))),
                Steps = 12, ModelEvaluations = sampler.ModelEvaluations,
                GuidanceStrength = 1, GuidanceRescale = 0, TimeRescale = 3, Seed = 73120,
                Arithmetic = "FP32 products with pinned BF16 operation boundaries; cooperative affine path disabled",
                AttentionQueryTileRows = attentionQueryTileRows,
                InferenceWallMilliseconds = timer.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                Readbacks = math.ReadbackCount, IndependentFullSamplerQualified = false,
                Stages = schedule.Measurements, Feedforward = feedforward.Evidence,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Material sampling: {timer.Elapsed.TotalSeconds:F1}s, twelve shape-conditioned predictions");
            Console.WriteLine("AURELIAN_TRELLIS2_MATERIAL_SAMPLED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"), JsonSerializer.Serialize(schedule.Inspection));
        }
    }

    private static float[] Vector(JsonElement normalization, string name, bool positive = false)
    {
        float[] values = normalization.GetProperty(name).EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 32 || values.Any(value => !float.IsFinite(value) || positive && value <= 0))
        {
            throw new InvalidDataException("Invalid material/shape normalization vector: " + name);
        }
        return values;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
