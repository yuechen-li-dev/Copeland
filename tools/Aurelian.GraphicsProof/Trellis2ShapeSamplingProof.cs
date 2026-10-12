using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2ShapeSamplingProof
{
    public static void Run(string weightsDirectory, string featuresDirectory, string occupancyDirectory, string output, bool cooperative = false)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "sampling-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"MeshDecoded\":false}");
        string flowPath = Path.Combine(weightsDirectory, Trellis2ShapeWeights.FlowName);
        Trellis2OccupancyWeights.Validate(flowPath, Trellis2ShapeWeights.FlowBytes, Trellis2ShapeWeights.FlowHash);
        string pipelinePath = Path.Combine(weightsDirectory, "pipeline.json");
        Trellis2OccupancyWeights.Validate(pipelinePath, 4186, Trellis2ShapeWeights.PipelineHash);
        using var pipeline = JsonDocument.Parse(File.ReadAllText(pipelinePath));
        var normalization = pipeline.RootElement.GetProperty("args").GetProperty("shape_slat_normalization");
        float[] mean = normalization.GetProperty("mean").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        float[] std = normalization.GetProperty("std").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (mean.Length != 32 || std.Length != 32 || !mean.All(float.IsFinite) || !std.All(value => float.IsFinite(value) && value > 0))
        {
            throw new InvalidDataException("Invalid pinned shape latent normalization vectors.");
        }
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(featuresDirectory, "reference-evidence.json")));
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        string featureHash = Hash(featureBytes);
        if (!reference.RootElement.GetProperty("Accepted").GetBoolean() || featureBytes.Length != 1029 * 1024 * 4
            || featureHash != reference.RootElement.GetProperty("FeatureSha256").GetString())
        {
            throw new InvalidDataException("Shape sampling requires hash-matched accepted DINO conditioning.");
        }
        using var occupancyEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(occupancyDirectory, "decoder-evidence.json")));
        if (!occupancyEvidence.RootElement.GetProperty("Accepted").GetBoolean()
            || !occupancyEvidence.RootElement.GetProperty("CompleteSamplingInput").GetBoolean())
        {
            throw new InvalidDataException("Shape sampling requires a completed occupancy generation input.");
        }
        byte[] logitBytes = File.ReadAllBytes(Path.Combine(occupancyDirectory, "logits.f32"));
        using var decoderReference = JsonDocument.Parse(File.ReadAllText(Path.Combine(occupancyDirectory, "decoder-reference-evidence.json")));
        if (!decoderReference.RootElement.GetProperty("Accepted").GetBoolean()
            || Hash(logitBytes) != decoderReference.RootElement.GetProperty("LogitSha256").GetString())
        {
            throw new InvalidDataException("Occupancy logits do not match their independent decoder evidence.");
        }
        var grid = SparseVoxelGrid.FromLogits(MemoryMarshal.Cast<byte, float>(logitBytes), 64, 2);
        int tokens = grid.Coordinates.Length;
        if (tokens is < 1 or > 8192)
        {
            throw new InvalidDataException($"AUR-TRELLIS-SHAPE-001: Sparse flow supports 1..8192 tokens; input has {tokens}.");
        }
        File.WriteAllText(Path.Combine(output, "coordinates.json"), JsonSerializer.Serialize(grid.Coordinates));
        using var weights = new SafeTensorFile(flowPath);
        if (weights.Tensors.Count != 640 || !weights.Tensors["input_layer.weight"].Shape.SequenceEqual(new[] { 1536, 32 }))
        {
            throw new InvalidDataException("Unexpected complete 512 shape flow inventory.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS learned shape sampler", EnableCooperativeMatrices: cooperative));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var feedforward = new DominatusComputeFeedforward();
        math.StreamController = feedforward;
        math.UseCooperativeLinear = cooperative;
        using var coordinates = math.CreateTensor(tokens, 3);
        math.Upload(coordinates, grid.Coordinates.SelectMany(coordinate => new float[] { coordinate.X, coordinate.Y, coordinate.Z }).ToArray());
        using var positive = math.CreateTensor(1029, 1024);
        using var negative = math.CreateTensor(1029, 1024);
        math.Upload(positive, MemoryMarshal.Cast<byte, float>(featureBytes));
        math.Upload(negative, new float[negative.Elements]);
        var batch = math.CreateBatch();
        batch.RoundBFloat16(positive, positive);
        math.Execute(batch);
        var model = new Trellis2FlowModel(math, weights, coordinates);
        var sampler = new Trellis2EulerSampler(model, timeRescale: 3, guidanceRescale: .5f);
        var schedule = new DominatusComputeSchedule("trellis.shape") { Observe = InferenceProofLog.Write };
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(sampler.Sample(positive, negative, output, seed: 73119));
            float[] latent = sampler.Result ?? throw new InvalidDataException("Shape sampler did not complete.");
            if (sampler.ModelEvaluations != 21 || latent.Length != tokens * 32 || !latent.All(float.IsFinite))
            {
                throw new InvalidDataException("Invalid completed sparse shape latent/evaluation count.");
            }
            File.WriteAllBytes(Path.Combine(output, "latent.f32"), MemoryMarshal.AsBytes(latent.AsSpan()).ToArray());
            var decoderLatent = new float[latent.Length];
            for (int index = 0; index < latent.Length; index++)
            {
                decoderLatent[index] = latent[index] * std[index % 32] + mean[index % 32];
            }
            File.WriteAllBytes(Path.Combine(output, "decoder-latent.f32"), MemoryMarshal.AsBytes(decoderLatent.AsSpan()).ToArray());
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, MeshDecoded = false, FullGenerationQualified = false,
                Scope = "Complete learned 512 shape SLat flow on actual occupancy coordinates, 12 upstream Euler steps",
                WeightSha256 = Trellis2ShapeWeights.FlowHash, FeatureSha256 = featureHash,
                PipelineSha256 = Trellis2ShapeWeights.PipelineHash,
                OccupancyLogitSha256 = Hash(logitBytes), LatentSha256 = Hash(MemoryMarshal.AsBytes(latent.AsSpan())),
                DecoderLatentSha256 = Hash(MemoryMarshal.AsBytes(decoderLatent.AsSpan())),
                Tokens = tokens, Channels = 32, Steps = 12, ModelEvaluations = sampler.ModelEvaluations,
                Seed = 73119, TimeRescale = 3, GuidanceRescale = .5, GuidanceStrength = 7.5,
                InferenceWallMilliseconds = timer.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                FinalVelocityReadbacks = math.ReadbackCount,
                Feedforward = feedforward.Evidence,
                CooperativeRequested = cooperative,
                CooperativeEnabled = math.CooperativeMatrixEnabled,
                IndependentFullSamplerQualified = false,
                InheritedQualificationLimit = "Occupancy final-step velocity comparison was 1.130% RMS versus the retained 1% bound; occupancy topology unchanged",
                Stages = schedule.Measurements, Sampler = sampler.Measurements,
                NextCommand = "--trellis2-finalize executes the learned mesh decoder, material flow, guided material decoder and GLB import/render proof",
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Full shape sampler completed in {timer.Elapsed.TotalSeconds:F1} s: {tokens}x32, {sampler.ModelEvaluations} model evaluations");
            Console.WriteLine("AURELIAN_TRELLIS2_SHAPE_SAMPLED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"),
                JsonSerializer.Serialize(schedule.Inspection, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "feedforward-inspection.json"),
                JsonSerializer.Serialize(feedforward.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
