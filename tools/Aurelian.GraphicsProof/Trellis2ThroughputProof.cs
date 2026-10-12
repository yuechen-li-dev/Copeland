using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2ThroughputProof
{
    public static void Run(string weightsDirectory, string featuresDirectory, string sampleDirectory, string output, bool cooperative = false)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        string path = Path.Combine(weightsDirectory, Trellis2ShapeWeights.FlowName);
        Trellis2OccupancyWeights.Validate(path, Trellis2ShapeWeights.FlowBytes, Trellis2ShapeWeights.FlowHash);
        using var weights = new SafeTensorFile(path);
        VoxelCoordinate[] positions = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(sampleDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing shape coordinates.");
        byte[] noiseBytes = File.ReadAllBytes(Path.Combine(sampleDirectory, "noise.f32"));
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(featuresDirectory, "reference-evidence.json")));
        string featureHash = Hash(featureBytes);
        if (!reference.RootElement.GetProperty("Accepted").GetBoolean()
            || featureHash != reference.RootElement.GetProperty("FeatureSha256").GetString())
        {
            throw new InvalidDataException("Throughput probe needs unchanged qualified DINO features.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS throughput qualification", EnableCooperativeMatrices: cooperative));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var coordinates = math.CreateTensor(positions.Length, 3);
        math.Upload(coordinates, positions.SelectMany(position => new float[] { position.X, position.Y, position.Z }).ToArray());
        using var context = math.CreateTensor(1029, 1024);
        math.Upload(context, MemoryMarshal.Cast<byte, float>(featureBytes));
        var rounding = math.CreateBatch();
        rounding.RoundBFloat16(context, context);
        math.Execute(rounding);
        using var input = math.CreateTensor(positions.Length, 32);
        math.Upload(input, MemoryMarshal.Cast<byte, float>(noiseBytes));
        var shared = new Trellis2BlockGraph(math, weights);
        using var hidden = shared.Affine(input, "input_layer", rounded: true);
        using var modulation = shared.Conditioning(1000);
        using var controller = new Trellis2ComputeFeedforward();
        // First run warms every pipeline on the exact motivating shape.
        using (var warm = new Trellis2BlockGraph(math, weights).Run(hidden, context, modulation, coordinates))
        {
        }
        math.StreamController = controller;
        math.UseCooperativeLinear = cooperative;
        using (var warm = new Trellis2BlockGraph(math, weights).Run(hidden, context, modulation, coordinates))
        {
        }
        float[]? expected = null;
        var measurements = new List<object>();
        var baselineTimes = new List<double>();
        var stagedTimes = new List<double>();
        double maximumRelativeRms = 0;
        bool bitExact = true;
        for (int trial = 0; trial < 3; trial++)
        {
            RunBlock(false, trial);
            RunBlock(true, trial);
        }
        controller.Dispose();
        double baseline = baselineTimes.Order().ElementAt(1);
        double staged = stagedTimes.Order().ElementAt(1);
        bool improved = staged < baseline;
        File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"), JsonSerializer.Serialize(controller.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = improved,
            Scope = $"Actual first shape transformer block, {positions.Length} irregular tokens; alternating warmed baseline and staged trials",
            WeightSha256 = Trellis2ShapeWeights.FlowHash,
            FeatureSha256 = featureHash,
            NoiseSha256 = Hash(noiseBytes),
            BitExact = bitExact,
            CooperativeRequested = cooperative,
            CooperativeEnabled = math.CooperativeMatrixEnabled,
            MaximumRelativeRms = maximumRelativeRms,
            NumericalBound = cooperative ? .005 : 0,
            BaselineMedianMilliseconds = baseline,
            StagedMedianMilliseconds = staged,
            Speedup = baseline / staged,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            Measurements = measurements,
            Feedforward = controller.Evidence,
            FullSamplerMeasured = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Throughput: {baseline:F1} -> {staged:F1} ms, {baseline / staged:F3}x, bit-exact={bitExact}, RMS={maximumRelativeRms:P5}");
        if (!improved)
        {
            throw new InvalidOperationException("AUR-TRELLIS-PERF-001: Staging did not improve the motivating block.");
        }

        void RunBlock(bool staged, int trial)
        {
            math.StreamController = staged ? controller : null;
            math.UseCooperativeLinear = staged && cooperative;
            var graph = new Trellis2BlockGraph(math, weights);
            var timer = Stopwatch.StartNew();
            using var result = graph.Run(hidden, context, modulation, coordinates);
            double milliseconds = timer.Elapsed.TotalMilliseconds;
            float[] actual = math.Read(result);
            expected ??= actual;
            bool identical = MemoryMarshal.AsBytes(actual.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(expected.AsSpan()));
            bitExact &= identical;
            double error = 0;
            double energy = 0;
            for (int index = 0; index < actual.Length; index++)
            {
                if (!float.IsFinite(actual[index]))
                {
                    throw new InvalidDataException("Nonfinite throughput output.");
                }
                double difference = (double)actual[index] - expected[index];
                error += difference * difference;
                energy += (double)expected[index] * expected[index];
            }
            double relativeRms = Math.Sqrt(error / Math.Max(energy, 1e-30));
            maximumRelativeRms = Math.Max(maximumRelativeRms, relativeRms);
            if (((!staged || !cooperative) && !identical) || relativeRms > .005)
            {
                throw new InvalidDataException($"AUR-TRELLIS-PERF-002: Transformer parity failed: identical={identical}, RMS={relativeRms:P5}.");
            }
            (staged ? stagedTimes : baselineTimes).Add(milliseconds);
            measurements.Add(new { Staged = staged, Trial = trial, Milliseconds = milliseconds, RelativeRms = relativeRms, BitExact = identical, Operations = graph.Measurements });
            Console.WriteLine($"Trial {trial}, staged={staged}: {milliseconds:F1} ms, RMS={relativeRms:P5}, identical={identical}");
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
