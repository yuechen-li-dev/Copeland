using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2ShapeDecoderProof
{
    public static void Run(string weightsDirectory, string sampleDirectory, string output, string? probeLatent = null)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "decoder-stage-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"MeshDecoded\":false}");
        string weightsPath = Path.Combine(weightsDirectory, Trellis2ShapeWeights.DecoderName);
        Trellis2OccupancyWeights.Validate(weightsPath, Trellis2ShapeWeights.DecoderBytes, Trellis2ShapeWeights.DecoderHash);
        VoxelCoordinate[] coordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(sampleDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing sparse coordinate rows.");
        var topology = SparseVoxelTopology.Create(32, coordinates);
        byte[] bytes = File.ReadAllBytes(probeLatent ?? Path.Combine(sampleDirectory, "decoder-latent.f32"));
        string latentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (probeLatent is null)
        {
            using var sampling = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleDirectory, "sampling-evidence.json")));
            if (!sampling.RootElement.GetProperty("Accepted").GetBoolean()
                || sampling.RootElement.GetProperty("DecoderLatentSha256").GetString() != latentHash
                || sampling.RootElement.GetProperty("ModelEvaluations").GetInt32() != 21)
            {
                throw new InvalidDataException("Decoder stage needs completed, hash-matched learned shape sampling.");
            }
        }
        using var weights = new SafeTensorFile(weightsPath);
        if (weights.Tensors.Count != 292)
        {
            throw new InvalidDataException("Unexpected learned sparse mesh decoder inventory.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian learned sparse decoder stage"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        var graph = new Trellis2SparseDecoderStage(math, weights);
        var schedule = new Trellis2ComputeSchedule("trellis.shape.decoder");
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(graph.Decode(MemoryMarshal.Cast<byte, float>(bytes).ToArray(), topology));
            float[] features = graph.Features ?? throw new InvalidDataException("No decoder stage output.");
            float[] logits = graph.SubdivisionLogits ?? throw new InvalidDataException("No subdivision prediction.");
            if (!features.All(float.IsFinite) || !logits.All(float.IsFinite) || math.ReadbackCount != 2)
            {
                throw new InvalidDataException("Decoder stage produced nonfinite values or unexpected readbacks.");
            }
            var children = topology.Subdivide(logits);
            File.WriteAllBytes(Path.Combine(output, "features.f32"), MemoryMarshal.AsBytes(features.AsSpan()).ToArray());
            File.WriteAllBytes(Path.Combine(output, "subdivision-logits.f32"), MemoryMarshal.AsBytes(logits.AsSpan()).ToArray());
            File.WriteAllText(Path.Combine(output, "children.json"), JsonSerializer.Serialize(children.Topology.Coordinates));
            File.WriteAllText(Path.Combine(output, "parents.json"), JsonSerializer.Serialize(children.ParentRows));
            File.WriteAllText(Path.Combine(output, "child-slots.json"), JsonSerializer.Serialize(children.ChildSlots));
            double milliseconds = timer.Elapsed.TotalMilliseconds;
            // Only a debug projection of the first learned subdivision, not a
            // substitute for the remaining decoder or dual-grid mesh extraction.
            if (children.Topology.Coordinates.Length > 0)
            {
                float[] occupied = Enumerable.Repeat(-1f, 64 * 64 * 64).ToArray();
                foreach (VoxelCoordinate coordinate in children.Topology.Coordinates)
                {
                    occupied[(coordinate.X * 64 + coordinate.Y) * 64 + coordinate.Z] = 1;
                }
                Trellis2OccupancyPreview.Render(plant, assets, SparseVoxelGrid.FromLogits(occupied, 64), output);
            }
            string nextBlocker = children.Topology.Coordinates.Length > 8192
                ? $"AUR-TRELLIS-DECODER-002: First learned subdivision needs {children.Topology.Coordinates.Length} feature rows; current matrix owner supports 8192. Implement paged sparse features/gathers before the next decoder stage."
                : "Later decoder stages, sparse channel-to-spatial feature gather and dual-grid mesh extraction remain unimplemented.";
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, MeshDecoded = false, FullGenerationQualified = false,
                Scope = "Actual from_latent, four learned 1024-channel sparse ConvNeXt blocks and first learned subdivision prediction",
                CompleteSamplingInput = probeLatent is null, ProbeLatent = probeLatent,
                WeightSha256 = Trellis2ShapeWeights.DecoderHash, LatentSha256 = latentHash,
                InputRows = topology.Coordinates.Length, InputResolution = 32,
                ChildRows = children.Topology.Coordinates.Length, ChildResolution = 64,
                ChildOrder = "Parent row, then child slot; X least significant bit, then Y, Z",
                InferenceWallMilliseconds = milliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                Readbacks = math.ReadbackCount,
                ReadbackScope = "Subdivision logits and explicit final-stage features for offline qualification; no internal layer readbacks",
                IndependentStageReferenceQualified = false,
                NextBlocker = nextBlocker, Stages = schedule.Measurements, Convolutions = graph.Measurements,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Decoder prefix: {topology.Coordinates.Length} parents -> {children.Topology.Coordinates.Length} learned children, {milliseconds / 1000:F1} s");
            Console.WriteLine(nextBlocker);
            Console.WriteLine("AURELIAN_TRELLIS2_DECODER_STAGE_EXECUTED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"),
                JsonSerializer.Serialize(schedule.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
