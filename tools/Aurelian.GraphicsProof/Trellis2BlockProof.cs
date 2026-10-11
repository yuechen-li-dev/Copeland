using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2BlockProof
{
    public static void Run(string inputDirectory, string outputDirectory, int tokens = 16, float timestep = 500)
    {
        Require(tokens is 16 or 64 && float.IsFinite(timestep) && timestep is >= 0 and <= 1000,
            "This bounded reference proof admits 16 or 64 spatial tokens and timesteps 0–1000.");
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullGenerationQualified\":false}");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(inputDirectory, "weights.json")));
        var source = manifest.RootElement;
        Require(source.GetProperty("ModelRevision").GetString() == Trellis2BlockWeights.Revision,
            "Unexpected checkpoint revision.");
        string weightsPath = Path.Combine(inputDirectory, "block0.safetensors");
        using (var stream = File.OpenRead(weightsPath))
        {
            Require(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
                == source.GetProperty("SubsetSha256").GetString(), "Checkpoint subset hash mismatch.");
        }
        using var weights = new SafeTensorFile(weightsPath);
        const int contextTokens = 32;
        float[] inputValues = Fixture(tokens * 1536, 0.03f, 0.4f);
        float[] contextValues = Fixture(contextTokens * 1024, 0.017f, 0.3f);
        Console.WriteLine("Computing independent C# reference for the complete learned block...");
        var referenceWatch = Stopwatch.StartNew();
        float[] expected = new Trellis2BlockReference(weights).Run(inputValues, tokens, contextValues, contextTokens, timestep);
        referenceWatch.Stop();

        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS.2 learned block qualification"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        // Compile first: runtime measurements do not conflate shader compilation with inference.
        foreach (string shader in new[] { "TensorMap.v.ts", "HeadNormalize.v.ts", "Normalization.v.ts",
            "RowReduction.v.ts", "Sgemm.v.ts", "StreamingLinear.v.ts", "Activation.v.ts" })
        {
            Console.WriteLine("Compiling " + shader);
            _ = assets.ComputeShader(shader);
        }
        var primitiveMeasurements = Trellis2KernelProof.Run(plant, assets);
        using var math = new VulkanComputeMath(plant, assets.ComputeShader);
        using var input = math.CreateTensor(tokens, 1536);
        using var context = math.CreateTensor(contextTokens, 1024);
        math.Upload(input, inputValues);
        math.Upload(context, contextValues);
        var graph = new Trellis2BlockGraph(math, weights);
        var watch = Stopwatch.StartNew();
        using var output = graph.Run(input, context, timestep);
        Require(math.ReadbackCount == 0, "Learned graph performed an intermediate activation readback.");
        float[] actual = math.Read(output);
        watch.Stop();
        Require(actual.All(float.IsFinite), "Nonfinite learned block output.");
        double maximum = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
        double rms = Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow((double)a - b, 2)).Average());
        double referenceRms = Math.Sqrt(expected.Select(value => (double)value * value).Average());
        File.WriteAllBytes(Path.Combine(outputDirectory, "actual.f32"), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        File.WriteAllBytes(Path.Combine(outputDirectory, "reference.f32"), MemoryMarshal.AsBytes(expected.AsSpan()).ToArray());
        // BF16 torso rounding accumulates across the entire block. Cancellation in
        // residual additions makes a purely relative per-element limit inappropriate.
        double scaledMaximum = actual.Zip(expected,
            (a, b) => Math.Abs((double)a - b) / (0.125 + 0.02 * Math.Abs(b))).Max();
        bool accepted = scaledMaximum <= 1 && rms / Math.Max(referenceRms, 1e-12) <= 0.005;
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = accepted,
            FullGenerationQualified = false,
            Scope = "Entire learned block 0 with synthetic spatial/conditioning tokens; not image-to-mesh generation",
            NumericPolicy = "FP32 accumulation, BF16 round-to-nearest-even at upstream torso tensor boundaries",
            Tokens = tokens,
            Timestep = timestep,
            ContextTokens = contextTokens,
            MaximumAbsoluteError = maximum,
            RootMeanSquareError = rms,
            ReferenceRootMeanSquare = referenceRms,
            RelativeRootMeanSquareError = rms / Math.Max(referenceRms, 1e-12),
            ScaledMaximumError = scaledMaximum,
            PerElementErrorLimit = "0.125 + 0.02 * abs(reference), accounting for BF16 accumulation and residual cancellation",
            RelativeRootMeanSquareErrorLimit = 0.005,
            InferenceWallMilliseconds = watch.Elapsed.TotalMilliseconds,
            ReferenceWallMilliseconds = referenceWatch.Elapsed.TotalMilliseconds,
            FinalReadbacks = math.ReadbackCount,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            MemoryScope = "Actual owned Vulkan allocations including staging/parameters; excludes driver/pipelines/other plant allocations",
            Plant = plant.Facts,
            WeightProvenance = source,
            Operations = graph.Measurements,
            PrimitiveMeasurements = primitiveMeasurements,
            NextBlocker = "Full 4096-token/30-block sampling schedule, DINOv3 image encoder and sparse-structure/shape/material decoders are not implemented or qualified",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Learned block: maximum error {maximum:G6}, RMS {rms:G6}, relative RMS {rms / referenceRms:G6}, peak {math.MemoryTelemetry.HighWaterLiveBytes:N0} bytes, {watch.Elapsed.TotalMilliseconds:F1} ms");
        Require(accepted, "Learned Vulkan block disagrees with independent reference; see evidence.json and raw outputs.");
        Console.WriteLine("AURELIAN_TRELLIS2_BLOCK_PASSED");
    }

    private static float[] Fixture(int count, float frequency, float amplitude)
    {
        var values = new float[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = Trellis2BlockReference.Round(MathF.Sin(index * frequency) * amplitude
                + MathF.Cos(index * frequency * 1.7f) * amplitude * 0.5f);
        }
        return values;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
