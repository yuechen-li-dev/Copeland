using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2TimestepProof
{
    private const string ModelRevision = "af44b45f2e35a493886929c6d786e563ec68364d";
    private const string SourceRevision = "75fbf0183001ed9876c8dbb35de6b68552ee08bd";

    public static void Run(string inputDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullGenerationQualified\":false}");
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(inputDirectory, "provenance.json")));
        var source = provenance.RootElement;
        Require(source.GetProperty("modelRevision").GetString() == ModelRevision
            && source.GetProperty("sourceRevision").GetString() == SourceRevision, "Unexpected model/source revision.");
        string weightsPath = CheckedFile("timestep.safetensors", "subsetSha256");
        string inputPath = CheckedFile("input.f32", "inputSha256");
        string expectedPath = CheckedFile("expected.f32", "expectedSha256");
        string modulationExpectedPath = CheckedFile("modulation-expected.f32", "modulationExpectedSha256");
        float[] inputValues = ReadFloats(inputPath);
        float[] expected = ReadFloats(expectedPath);
        float[] modulationExpected = ReadFloats(modulationExpectedPath);
        Require(inputValues.Length == 16 * 256 && expected.Length == 16 * 1536, "Unexpected oracle input/output shapes.");
        Require(modulationExpected.Length == 16 * 9216, "Unexpected conditioning oracle shape.");
        using var weights = new SafeTensorFile(weightsPath);
        foreach (var layer in new[] { (Index: 0, Width: 256), (Index: 2, Width: 1536) })
        {
            Require(weights.Tensors[$"t_embedder.mlp.{layer.Index}.weight"].Shape.SequenceEqual(new[] { 1536, layer.Width }),
                "Unexpected checkpoint affine shape.");
            Require(weights.Tensors[$"t_embedder.mlp.{layer.Index}.bias"].Shape.SequenceEqual(new[] { 1536 }),
                "Unexpected checkpoint bias shape.");
        }
        Require(weights.Tensors["adaLN_modulation.1.weight"].Shape.SequenceEqual(new[] { 9216, 1536 })
            && weights.Tensors["adaLN_modulation.1.bias"].Shape.SequenceEqual(new[] { 9216 }), "Unexpected shared modulation shape.");

        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS.2 learned-module experiment"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        var readiness = CaptureDenoiserBlockers(plant, assets);
        // Compile once before comparing schedules. Wall times below include file ingress,
        // conversion, Vulkan allocation/transfers/submissions, compute and final readback.
        _ = assets.ComputeShader("StreamingLinear.v.ts");
        _ = assets.ComputeShader("Activation.v.ts");
        var measurements = new List<object>();
        QualifyTailAndFailureContracts(plant, assets);
        foreach (int tileRows in new[] { 16, 64, 256 })
        {
            using var math = new VulkanComputeMath(plant, assets.ComputeShader);
            using var input = math.CreateTensor(16, 256);
            using var first = math.CreateTensor(16, 1536);
            using var hidden = math.CreateTensor(16, 1536);
            using var output = math.CreateTensor(16, 1536);
            using var modulationInput = math.CreateTensor(16, 1536);
            using var modulationOutput = math.CreateTensor(16, 9216);
            math.Upload(input, inputValues);
            var firstPlan = new StreamingLinearPlan(256, 1536, tileRows);
            var secondPlan = new StreamingLinearPlan(1536, 1536, tileRows);
            var watch = Stopwatch.StartNew();
            var firstResult = math.LinearStreamed(input, first, firstPlan, tile => LoadTile(weights, 0, firstPlan, tile));
            var activation = math.CreateBatch();
            activation.Activate(first, hidden, TensorActivation.Silu);
            var activationResult = math.Execute(activation);
            var secondResult = math.LinearStreamed(hidden, output, secondPlan, tile => LoadTile(weights, 2, secondPlan, tile));
            var modulationPlan = new StreamingLinearPlan(1536, 9216, tileRows);
            var modulationActivation = math.CreateBatch();
            modulationActivation.Activate(output, modulationInput, TensorActivation.Silu);
            var modulationActivationResult = math.Execute(modulationActivation);
            var modulationResult = math.LinearStreamed(modulationInput, modulationOutput, modulationPlan,
                tile => LoadTile(weights, "adaLN_modulation.1", modulationPlan, tile));
            Require(math.ReadbackCount == 0, "Module performed intermediate activation readback.");
            ulong peakBeforeReadback = math.MemoryTelemetry.HighWaterLiveBytes;
            float[] actual = math.Read(output);
            float[] actualModulation = math.Read(modulationOutput);
            watch.Stop();
            Require(actual.All(float.IsFinite), "Nonfinite Vulkan module output.");
            double maximum = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
            double rms = Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow((double)a - b, 2)).Average());
            Require(maximum <= 0.0003, $"Real learned module disagrees with NumPy FP64 oracle: {maximum:G6}.");
            double modulationMaximum = actualModulation.Zip(modulationExpected, (a, b) => Math.Abs((double)a - b)).Max();
            Require(actualModulation.All(float.IsFinite) && modulationMaximum <= 0.0003,
                $"Shared conditioning disagrees with oracle: {modulationMaximum:G6}.");
            var telemetry = math.MemoryTelemetry;
            measurements.Add(new
            {
                TileRows = tileRows,
                Values = actual.Length,
                MaximumAbsoluteError = maximum,
                RootMeanSquareError = rms,
                ConditioningValues = actualModulation.Length,
                ConditioningMaximumAbsoluteError = modulationMaximum,
                WeightUploads = firstResult.WeightUploads + secondResult.WeightUploads + modulationResult.WeightUploads,
                Dispatches = firstResult.Dispatches + activationResult.Dispatches + secondResult.Dispatches
                    + modulationResult.Dispatches + modulationActivationResult.Dispatches,
                GpuMilliseconds = SumTimings(firstResult.GpuMilliseconds, activationResult.GpuMilliseconds,
                    secondResult.GpuMilliseconds, modulationResult.GpuMilliseconds, modulationActivationResult.GpuMilliseconds),
                WallMilliseconds = watch.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = telemetry.HighWaterLiveBytes,
                PeakBeforeReadbackBytes = peakBeforeReadback,
                LargestWeightBufferBytes = secondPlan.WeightBufferBytes,
                ExpandedFullModuleWeightBytes = firstPlan.ExpandedFullWeightBytes + secondPlan.ExpandedFullWeightBytes
                    + modulationPlan.ExpandedFullWeightBytes,
                FinalReadbacks = math.ReadbackCount,
                MemoryScope = "Actual owned Vulkan allocation sizes, including staging and parameters; excludes driver/pipeline/query/other plant allocations",
            });
            File.WriteAllBytes(Path.Combine(outputDirectory, $"timestep-{tileRows}.f32"), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
            File.WriteAllBytes(Path.Combine(outputDirectory, $"conditioning-{tileRows}.f32"), MemoryMarshal.AsBytes(actualModulation.AsSpan()).ToArray());
            Console.WriteLine($"TRELLIS.2 conditioning, tile {tileRows}: timestep error {maximum:G5}, modulation error {modulationMaximum:G5}, owned peak {telemetry.HighWaterLiveBytes:N0} bytes, {watch.Elapsed.TotalMilliseconds:F1} ms wall");
        }
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            FullGenerationQualified = false,
            Scope = "Complete learned timestep MLP and shared AdaLN modulation (BF16 checkpoint values expanded exactly to FP32); output before final BF16 cast",
            FrequencyFeatures = "Upstream FP32 sinusoidal features supplied by the reference fixture; not a GPU feature-extractor qualification",
            ModelRevision,
            SourceRevision,
            Plant = plant.Facts,
            WeightProvenance = source,
            Measurements = measurements,
            RejectedDenoiserContracts = readiness,
            NextBlocker = "4096-token, 12-head attention with 1536-channel normalization, 8192-channel FFN, 3D RoPE and AdaLN; current pack is bounded to 1024-key attention/norm and K<=4096. Sparse 3D decoders and image conditioning are also absent.",
        }, new JsonSerializerOptions { WriteIndented = true }));

        string CheckedFile(string fileName, string hashProperty)
        {
            string path = Path.Combine(inputDirectory, fileName);
            using var stream = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Require(hash == source.GetProperty(hashProperty).GetString(), "Fixture hash mismatch: " + fileName);
            return path;
        }
    }

    private static IReadOnlyList<string> CaptureDenoiserBlockers(AurelianVulkanPlant plant, GameAssets assets)
    {
        using var math = new VulkanComputeMath(plant, assets.ComputeShader);
        var diagnostics = new List<string>();
        using var channels = math.CreateTensor(1, 1536);
        using var gammaBeta = math.CreateTensor(1, 3072);
        using var normalized = math.CreateTensor(1, 1536);
        Capture("1536-channel LayerNorm", () => math.CreateBatch().Normalize(channels, gammaBeta, normalized, Normalization.LayerNorm));
        using var query = math.CreateTensor(16, 128);
        using var key = math.CreateTensor(4096, 128);
        using var value = math.CreateTensor(4096, 128);
        using var scores = math.CreateTensor(16, 4096);
        using var probabilities = math.CreateTensor(16, 4096);
        using var attentionOutput = math.CreateTensor(16, 128);
        Capture("4096-key attention, one query tile/head", () => math.CreateBatch().Attention(query, key, value,
            scores, probabilities, attentionOutput, causal: false));
        Capture("Full 4096x4096 score matrix", () => math.CreateTensor(4096, 4096));
        Capture("8192-input FFN projection", () => new StreamingLinearPlan(8192, 1536, 16));
        return diagnostics.AsReadOnly();

        void Capture(string name, Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001:", StringComparison.Ordinal))
            {
                diagnostics.Add(name + ": " + exception.Message);
                return;
            }
            throw new InvalidOperationException("Readiness stop unexpectedly changed: " + name);
        }
    }

    private static float[] LoadTile(SafeTensorFile weights, int layer, StreamingLinearPlan plan, LinearWeightTile tile)
        => LoadTile(weights, $"t_embedder.mlp.{layer}", plan, tile);

    private static float[] LoadTile(SafeTensorFile weights, string prefix, StreamingLinearPlan plan, LinearWeightTile tile)
    {
        float[] values = weights.ReadRows(prefix + ".weight", tile.FirstOutput, tile.OutputCount);
        float[] bias = weights.ReadVector(prefix + ".bias");
        float[] packed = new float[plan.TileRows * (plan.InputWidth + 1)];
        for (int row = 0; row < tile.OutputCount; row++)
        {
            Array.Copy(values, row * plan.InputWidth, packed, row * (plan.InputWidth + 1), plan.InputWidth);
            packed[row * (plan.InputWidth + 1) + plan.InputWidth] = bias[tile.FirstOutput + row];
        }
        return packed;
    }

    private static void QualifyTailAndFailureContracts(AurelianVulkanPlant plant, GameAssets assets)
    {
        using var math = new VulkanComputeMath(plant, assets.ComputeShader);
        using var input = math.CreateTensor(3, 19);
        using var output = math.CreateTensor(3, 37);
        float[] a = Enumerable.Range(0, 57).Select(index => (index - 20) * 0.03f).ToArray();
        math.Upload(input, a);
        var plan = new StreamingLinearPlan(19, 37, 7);
        math.LinearStreamed(input, output, plan, tile =>
        {
            var block = new float[7 * 20];
            for (int row = 0; row < tile.OutputCount; row++)
            {
                for (int column = 0; column < 19; column++)
                {
                    block[row * 20 + column] = (tile.FirstOutput + row - column) * 0.01f;
                }
                block[row * 20 + 19] = (tile.FirstOutput + row) * 0.1f;
            }
            return block;
        });
        float[] actual = math.Read(output);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 37; column++)
            {
                double expected = column * 0.1f;
                for (int inner = 0; inner < 19; inner++)
                {
                    expected += (double)a[row * 19 + inner] * ((column - inner) * 0.01f);
                }
                Require(Math.Abs(actual[row * 37 + column] - expected) < 0.00001, "Streaming output/K/batch tail regression.");
            }
        }
        ulong before = math.MemoryTelemetry.LiveBytes;
        ExpectReject(() => math.LinearStreamed(input, output, plan, _ => new float[1]));
        Require(math.MemoryTelemetry.LiveBytes == before, "Failed weight provider leaked a GPU allocation.");
        ExpectReject(() => math.LinearStreamed(input, input, plan, _ => throw new Exception("Provider must not be called")));
        Console.WriteLine("Streaming linear tails, provider failure cleanup and shape rejection passed.");
    }

    private static void ExpectReject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001:", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("Expected a named shape/provider rejection.");
    }

    private static float[] ReadFloats(string path) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();

    private static double? SumTimings(params double?[] values)
    {
        if (values.Any(value => value is null))
        {
            return null;
        }
        return values.Sum(value => value!.Value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("AUR-TRELLIS-POC-001: " + message);
        }
    }
}
