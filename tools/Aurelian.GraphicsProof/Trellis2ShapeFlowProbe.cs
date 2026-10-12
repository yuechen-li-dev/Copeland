using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

/// <summary>Explicit diagnostic readbacks at selected learned-block boundaries.
/// This command is separate from the no-intermediate-readback inference path.</summary>
internal static class Trellis2ShapeFlowProbe
{
    public static void Run(string weightsDirectory, string featuresDirectory, string sampleDirectory, string output,
        bool negative = false, bool staged = false, bool cooperative = false)
    {
        Directory.CreateDirectory(output);
        string path = Path.Combine(weightsDirectory, Trellis2ShapeWeights.FlowName);
        Trellis2OccupancyWeights.Validate(path, Trellis2ShapeWeights.FlowBytes, Trellis2ShapeWeights.FlowHash);
        using var weights = new SafeTensorFile(path);
        VoxelCoordinate[] positions = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(sampleDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing shape coordinates.");
        byte[] sampleBytes = File.ReadAllBytes(Path.Combine(sampleDirectory, "noise.f32"));
        float[] sample = MemoryMarshal.Cast<byte, float>(sampleBytes).ToArray();
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian shape flow block diagnostics", EnableCooperativeMatrices: cooperative));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var feedforward = new DominatusComputeFeedforward();
        staged |= cooperative;
        math.StreamController = staged ? feedforward : null;
        math.UseCooperativeLinear = cooperative;
        using var coordinates = math.CreateTensor(positions.Length, 3);
        math.Upload(coordinates, positions.SelectMany(position => new float[] { position.X, position.Y, position.Z }).ToArray());
        using var context = math.CreateTensor(1029, 1024);
        byte[] featureBytes = File.ReadAllBytes(Path.Combine(featuresDirectory, "features.f32"));
        if (negative)
        {
            math.Upload(context, new float[context.Elements]);
        }
        else
        {
            math.Upload(context, MemoryMarshal.Cast<byte, float>(featureBytes));
        }
        var batch = math.CreateBatch();
        batch.RoundBFloat16(context, context);
        math.Execute(batch);
        var selected = new HashSet<int> { 0, 1, 2, 5, 10, 20, 29 };
        string prefix = negative ? "negative" : "positive";
        using (var modulation = new Trellis2BlockGraph(math, weights).Conditioning(1000))
        {
            File.WriteAllBytes(Path.Combine(output, "shared-modulation.f32"),
                MemoryMarshal.AsBytes(math.Read(modulation).AsSpan()).ToArray());
        }
        var graph = new Trellis2FlowModel(math, weights, coordinates, (index, tensor) =>
        {
            if (selected.Contains(index))
            {
                float[] values = math.Read(tensor);
                File.WriteAllBytes(Path.Combine(output, $"{prefix}-block-{index:D2}.f32"), MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
            }
        });
        float[]? velocity = null;
        var schedule = new DominatusComputeSchedule("trellis.shape.diagnostic") { Observe = InferenceProofLog.Write };
        schedule.Run(graph.Evaluate(sample, context, 1000, "shape.diagnostic", result => velocity = result));
        File.WriteAllBytes(Path.Combine(output, prefix + "-velocity.f32"), MemoryMarshal.AsBytes(velocity!.AsSpan()).ToArray());
        File.WriteAllText(Path.Combine(output, "diagnostic-evidence.json"), JsonSerializer.Serialize(new
        {
            Executed = true, AcceptedAsFullSampling = false,
            Scope = $"One actual {prefix} model evaluation with seven explicit block readbacks for numerical diagnosis",
            WeightSha256 = Trellis2ShapeWeights.FlowHash,
            InputSha256 = Convert.ToHexString(SHA256.HashData(sampleBytes)).ToLowerInvariant(),
            FeatureSha256 = Convert.ToHexString(SHA256.HashData(featureBytes)).ToLowerInvariant(),
            Readbacks = math.ReadbackCount, Blocks = selected.Order().ToArray(), Stages = schedule.Measurements,
            NegativeConditioning = negative,
            Staged = staged,
            CooperativeRequested = cooperative,
            Feedforward = feedforward.Evidence,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TRELLIS2_SHAPE_FLOW_DIAGNOSTIC_EXECUTED");
    }
}
