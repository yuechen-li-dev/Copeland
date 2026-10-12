using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2SamplingProof
{
    public static void Run(string weightsDirectory, string featuresDirectory, string outputDirectory, bool cooperative = false)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "sampling-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"OccupancyDecoded\":false}");
        string flowPath = Path.Combine(weightsDirectory, Trellis2OccupancyWeights.FlowName);
        Trellis2OccupancyWeights.Validate(flowPath, 2584426920, Trellis2OccupancyWeights.FlowHash);
        string referencePath = Path.Combine(featuresDirectory, "reference-evidence.json");
        bool independentEncoderReference = File.Exists(referencePath);
        using var reference = JsonDocument.Parse(File.ReadAllText(independentEncoderReference
            ? referencePath : Path.Combine(featuresDirectory, "evidence.json")));
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        if (!reference.RootElement.GetProperty("Accepted").GetBoolean() || featureBytes.Length != 1029 * 1024 * 4
            || Convert.ToHexString(SHA256.HashData(featureBytes)).ToLowerInvariant()
                != reference.RootElement.GetProperty("FeatureSha256").GetString())
        {
            throw new InvalidDataException("Sampling requires hash-matched accepted DINO conditioning.");
        }
        using var weights = new SafeTensorFile(flowPath);
        if (weights.Tensors.Count != 640)
        {
            throw new InvalidDataException("Unexpected complete sparse-structure flow inventory.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS learned occupancy sampler", EnableCooperativeMatrices: cooperative));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        foreach (string shader in new[] { "TensorMap.v.ts", "HeadNormalize.v.ts", "Normalization.v.ts",
            "RowReduction.v.ts", "Sgemm.v.ts", "StreamingLinear.v.ts", "Activation.v.ts" })
        {
            _ = assets.ComputeShader(shader);
        }
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var feedforward = new Trellis2ComputeFeedforward();
        math.StreamController = feedforward;
        math.UseCooperativeLinear = cooperative;
        using var positive = math.CreateTensor(1029, 1024);
        using var negative = math.CreateTensor(1029, 1024);
        math.Upload(positive, MemoryMarshal.Cast<byte, float>(featureBytes));
        math.Upload(negative, new float[1029 * 1024]);
        var batch = math.CreateBatch();
        batch.RoundBFloat16(positive, positive);
        math.Execute(batch);
        var model = new Trellis2FlowModel(math, weights);
        var sampler = new Trellis2EulerSampler(model);
        var schedule = new Trellis2ComputeSchedule();
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(sampler.Sample(positive, negative, outputDirectory, seed: 90421));
            float[] result = sampler.Result ?? throw new InvalidOperationException("Sampler did not finish.");
            if (sampler.ModelEvaluations != 22 || result.Length != 4096 * 8 || !result.All(float.IsFinite))
            {
                throw new InvalidDataException("Full sampler result/evaluation count is invalid.");
            }
            File.WriteAllBytes(Path.Combine(outputDirectory, "latent.f32"), MemoryMarshal.AsBytes(result.AsSpan()).ToArray());
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true,
                OccupancyDecoded = false,
                FullGenerationQualified = false,
                Scope = "Real complete 30-block sparse-structure flow, actual image conditioning, 12 upstream Euler steps",
                WeightSha256 = Trellis2OccupancyWeights.FlowHash,
                FeatureSha256 = reference.RootElement.GetProperty("FeatureSha256").GetString(),
                IndependentEncoderReferenceQualified = independentEncoderReference,
                LatentSha256 = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(result.AsSpan()))).ToLowerInvariant(),
                Steps = 12,
                ModelEvaluations = sampler.ModelEvaluations,
                Seed = 90421,
                LatentLayout = "4096 XYZ lattice tokens, 8 channels, X/Y/Z axes follow upstream meshgrid",
                InferenceWallMilliseconds = timer.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                Readbacks = math.ReadbackCount,
                Feedforward = feedforward.Evidence,
                CooperativeRequested = cooperative,
                CooperativeEnabled = math.CooperativeMatrixEnabled,
                ReadbackScope = "Final 4096x8 velocity per model evaluation; C# Euler/CFG arithmetic, no transformer activation readbacks",
                Scheduler = "AurelianAgentRuntime / Dominatus coroutine and inspector; sequential single Vulkan plant",
                Sampler = sampler.Measurements,
                Stages = schedule.Measurements,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Full sampler completed in {timer.Elapsed.TotalSeconds:F1} s, {sampler.ModelEvaluations} complete model evaluations");
            Console.WriteLine("AURELIAN_TRELLIS2_STRUCTURE_SAMPLED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(outputDirectory, "dominatus-inspection.json"),
                JsonSerializer.Serialize(schedule.Inspection, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outputDirectory, "feedforward-inspection.json"),
                JsonSerializer.Serialize(feedforward.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
