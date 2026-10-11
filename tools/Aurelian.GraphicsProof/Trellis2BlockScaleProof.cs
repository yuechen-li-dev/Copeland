using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

/// <summary>Full lattice integration/performance probe, not a sampler or decoder.
/// Spatial activations remain a named synthetic fixture. Conditioning is the
/// independently qualified real-image DINO feature artifact.</summary>
internal static class Trellis2BlockScaleProof
{
    public static void Run(string weightsDirectory, string featuresDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullGenerationQualified\":false}");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(weightsDirectory, "weights.json")));
        string weightsPath = Path.Combine(weightsDirectory, "block0.safetensors");
        using (var stream = File.OpenRead(weightsPath))
        {
            Require(manifest.RootElement.GetProperty("ModelRevision").GetString() == Trellis2BlockWeights.Revision
                && Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() == manifest.RootElement.GetProperty("SubsetSha256").GetString(),
                "Unexpected learned block checkpoint provenance.");
        }
        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(featuresDirectory, "reference-evidence.json")));
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        Require(qualification.RootElement.GetProperty("Accepted").GetBoolean()
            && qualification.RootElement.GetProperty("FullEncoderQualified").GetBoolean()
            && featureBytes.Length == 1029 * 1024 * 4
            && Convert.ToHexString(SHA256.HashData(featureBytes)).ToLowerInvariant()
                == qualification.RootElement.GetProperty("FeatureSha256").GetString(),
            "Full-lattice probe requires the independently qualified DINO features with unchanged hash.");
        float[] features = MemoryMarshal.Cast<byte, float>(featureBytes).ToArray();
        float[] spatial = Enumerable.Range(0, 4096 * 1536).Select(index => MathF.Sin(index * 0.03f) * 0.4f).ToArray();
        using var weights = new SafeTensorFile(weightsPath);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS full lattice learned block probe"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        foreach (string shader in new[] { "TensorMap.v.ts", "HeadNormalize.v.ts", "Normalization.v.ts",
            "RowReduction.v.ts", "Sgemm.v.ts", "StreamingLinear.v.ts", "Activation.v.ts" })
        {
            _ = assets.ComputeShader(shader);
        }
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var input = math.CreateTensor(4096, 1536);
        using var context = math.CreateTensor(1029, 1024);
        math.Upload(input, spatial);
        math.Upload(context, features);
        var rounding = math.CreateBatch();
        rounding.RoundBFloat16(input, input);
        rounding.RoundBFloat16(context, context);
        math.Execute(rounding);
        var graph = new Trellis2BlockGraph(math, weights);
        var timer = Stopwatch.StartNew();
        using var output = graph.Run(input, context, 500);
        Require(math.ReadbackCount == 0, "Full learned block performed an intermediate activation readback.");
        float[] actual = math.Read(output);
        timer.Stop();
        bool accepted = actual.All(float.IsFinite);
        File.WriteAllBytes(Path.Combine(outputDirectory, "output.f32"), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = accepted,
            FullGenerationQualified = false,
            FullLatticeNumericalParityQualified = false,
            Scope = "Real learned block 0, 4096 synthetic spatial tokens, actual independently qualified 1029-token DINO conditioning; integration/performance probe only",
            SpatialInput = "BF16-rounded sin(index*0.03)*0.4; not learned noise projection or denoised occupancy",
            Tokens = 4096,
            ContextTokens = 1029,
            InferenceWallMilliseconds = timer.Elapsed.TotalMilliseconds,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            FinalReadbacks = math.ReadbackCount,
            MemoryScope = "Owned Vulkan buffers including staging/parameters, not total process VRAM",
            OutputRootMeanSquare = Math.Sqrt(actual.Average(value => (double)value * value)),
            Measurements = graph.Measurements,
            NextBlocker = new
            {
                MissingFlowTensors = "blocks.1 through blocks.29, input_layer and out_layer; current local subset has block 0 and shared conditioning only",
                FullFlowCheckpointBytes = Trellis2BlockWeights.CheckpointBytes,
                MissingSampler = "12-step FlowEulerGuidanceIntervalSampler, sigma_min 1e-5, guidance 7.5, rescale .7, interval [.6,1], rescale_t 5",
                MissingDecoder = "ss_dec_conv3d_16l8_fp16, 147591972-byte checkpoint; dense 3D convolution/upsampling graph and rank-5 weights",
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Require(accepted, "Nonfinite output in full-lattice block probe.");
        Console.WriteLine($"Full lattice block: {timer.Elapsed.TotalSeconds:F2} s, peak {math.MemoryTelemetry.HighWaterLiveBytes:N0} bytes");
        Console.WriteLine("AURELIAN_TRELLIS2_FULL_LATTICE_BLOCK_EXECUTED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
