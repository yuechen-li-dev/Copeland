using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class DinoV3EncoderProof
{
    public static void Run(string weightsDirectory, string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullGenerationQualified\":false}");
        DinoV3PatchProof.ValidateCheckpoint(weightsDirectory);
        float[] patches = Trellis2ImageInput.Prepare(imagePath, outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "patches.f32"), MemoryMarshal.AsBytes(patches.AsSpan()).ToArray());
        using var weights = new SafeTensorFile(Path.Combine(weightsDirectory, "model.safetensors"));
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS DINOv3 image encoder"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        foreach (string shader in new[] { "StreamingLinear.v.ts", "TensorMap.v.ts", "Sgemm.v.ts", "RowReduction.v.ts", "Normalization.v.ts", "Activation.v.ts" })
        {
            _ = assets.ComputeShader(shader);
        }
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 8388608);
        var graph = new DinoV3Encoder(math, weights);
        var timer = Stopwatch.StartNew();
        using var features = graph.Encode(patches, 32);
        if (math.ReadbackCount != 0)
        {
            throw new InvalidOperationException("DINO encoder performed an intermediate activation readback.");
        }
        float[] actual = math.Read(features);
        timer.Stop();
        double meanError = 0;
        double varianceError = 0;
        for (int row = 0; row < features.Rows; row++)
        {
            double mean = actual.AsSpan(row * 1024, 1024).ToArray().Average(value => (double)value);
            double variance = actual.AsSpan(row * 1024, 1024).ToArray().Average(value => Math.Pow(value - mean, 2));
            meanError = Math.Max(meanError, Math.Abs(mean));
            varianceError = Math.Max(varianceError, Math.Abs(variance - 1));
        }
        bool valid = actual.All(float.IsFinite) && meanError < 1e-5 && varianceError < 0.001;
        if (valid)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, "features.f32"), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        }
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = valid,
            FullEncoderExecuted = true,
            IndependentReferenceQualified = false,
            FullGenerationQualified = false,
            Model = "facebook/dinov3-vitl16-pretrain-lvd1689m",
            CheckpointRevision = "ea8dc2863c51be0a264bab82070e3e8836b02d51",
            ImageSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).ToLowerInvariant(),
            ImageSize = 512,
            Tokens = features.Rows,
            Channels = features.Columns,
            PrefixTokens = 5,
            Layers = 24,
            Precision = "FP32 storage/accumulation; erf GELU approximation, not tanh GELU",
            FinalNormalization = "TRELLIS non-affine LayerNorm epsilon 1e-5; retain CLS and four registers",
            Preprocessing = "C# alpha-aware Lanczos-3, foreground threshold >204, tight square crop, black composite, 512 Lanczos-3, ImageNet RGB normalization; Pillow byte parity measured separately",
            MaximumTokenMeanError = meanError,
            MaximumTokenVarianceError = varianceError,
            InferenceWallMilliseconds = timer.Elapsed.TotalMilliseconds,
            FinalReadbacks = math.ReadbackCount,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            MemoryScope = "Owned Vulkan buffers including staging/parameters; not total process VRAM",
            Measurements = graph.Measurements,
            NextBlocker = "Complete 30-block 4096-token sparse-structure flow model, Euler/CFG sampler and dense Conv3D occupancy decoder",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"DINO full encoder: {features.Rows}x{features.Columns}, {timer.Elapsed.TotalSeconds:F2} s, peak {math.MemoryTelemetry.HighWaterLiveBytes:N0} bytes");
        if (!valid)
        {
            throw new InvalidOperationException("Complete encoder produced invalid or incorrectly normalized features.");
        }
        Console.WriteLine("AURELIAN_DINOV3_ENCODER_EXECUTED");
    }
}
