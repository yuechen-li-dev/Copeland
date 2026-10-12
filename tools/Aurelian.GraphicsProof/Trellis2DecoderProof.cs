using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2DecoderProof
{
    public static void Run(string weightsDirectory, string sampleDirectory, string outputDirectory, string? probeLatent = null)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "decoder-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullGenerationQualified\":false}");
        string path = Path.Combine(weightsDirectory, Trellis2OccupancyWeights.DecoderName);
        Trellis2OccupancyWeights.Validate(path, 147591972, Trellis2OccupancyWeights.DecoderHash);
        if (probeLatent is null)
        {
            using var sampleEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleDirectory, "sampling-evidence.json")));
            if (!sampleEvidence.RootElement.GetProperty("Accepted").GetBoolean()
                || sampleEvidence.RootElement.GetProperty("ModelEvaluations").GetInt32() != 22
                || sampleEvidence.RootElement.GetProperty("WeightSha256").GetString() != Trellis2OccupancyWeights.FlowHash)
            {
                throw new InvalidDataException("Decoder requires completed 12-step learned flow sampling evidence.");
            }
        }
        byte[] latentBytes = File.ReadAllBytes(probeLatent ?? Path.Combine(sampleDirectory, "latent.f32"));
        if (latentBytes.Length != 4096 * 8 * 4)
        {
            throw new InvalidDataException("Sampled latent shape mismatch.");
        }
        float[] latent = MemoryMarshal.Cast<byte, float>(latentBytes).ToArray();
        using var weights = new SafeTensorFile(path);
        if (weights.Tensors.Count != 74)
        {
            throw new InvalidDataException("Unexpected complete occupancy decoder tensor inventory.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian learned occupancy decoder"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        var graph = new Trellis2OccupancyDecoder(math, weights);
        var schedule = new DominatusComputeSchedule() { Observe = InferenceProofLog.Write };
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(graph.Decode(latent));
            float[] logits = graph.Result ?? throw new InvalidDataException("Decoder did not complete.");
            var full = SparseVoxelGrid.FromLogits(logits, 64);
            var pooled = SparseVoxelGrid.FromLogits(logits, 64, 2);
            File.WriteAllBytes(Path.Combine(outputDirectory, "logits.f32"), MemoryMarshal.AsBytes(logits.AsSpan()).ToArray());
            if (math.ReadbackCount != 1 || pooled.Coordinates.Length == 0 || pooled.Coordinates.Length == 32768)
            {
                File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
                {
                    Accepted = false, FullGenerationQualified = false, ProbeLatent = probeLatent,
                    ActiveVoxels64 = full.Coordinates.Length, ActiveVoxels32 = pooled.Coordinates.Length,
                    LogitMinimum = logits.Min(), LogitMaximum = logits.Max(), Readbacks = math.ReadbackCount,
                    LatentSha256 = Convert.ToHexString(SHA256.HashData(latentBytes)).ToLowerInvariant(),
                }, new JsonSerializerOptions { WriteIndented = true }));
                throw new InvalidDataException($"Decoder output rejected: {pooled.Coordinates.Length} active cells at 32^3, {math.ReadbackCount} readbacks.");
            }
            File.WriteAllBytes(Path.Combine(outputDirectory, "occupancy64.bin"), full.ActiveIndex.Select(index => index >= 0 ? (byte)1 : (byte)0).ToArray());
            File.WriteAllBytes(Path.Combine(outputDirectory, "occupancy32.bin"), pooled.ActiveIndex.Select(index => index >= 0 ? (byte)1 : (byte)0).ToArray());
            File.WriteAllText(Path.Combine(outputDirectory, "coordinates.json"), JsonSerializer.Serialize(pooled.Coordinates));
            File.WriteAllBytes(Path.Combine(outputDirectory, "neighbors27.i32"), MemoryMarshal.AsBytes(pooled.Neighbors27.AsSpan()).ToArray());
            double inferenceMilliseconds = timer.Elapsed.TotalMilliseconds;
            Trellis2OccupancyPreview.Render(plant, assets, pooled, outputDirectory);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, OccupancyDecoded = true, FullGenerationQualified = false,
                CompleteSamplingInput = probeLatent is null,
                IndependentFullPipelineQualified = false,
                ProbeLatent = probeLatent,
                Scope = "Complete learned dense occupancy decoder and CPU max-pool/sparse topology; occupancy visualization only",
                Device = plant.Facts.PhysicalDeviceName,
                WeightSha256 = Trellis2OccupancyWeights.DecoderHash,
                LatentSha256 = Convert.ToHexString(SHA256.HashData(latentBytes)).ToLowerInvariant(),
                GridResolution = 64, ActiveVoxels64 = full.Coordinates.Length,
                PooledResolution = 32, ActiveVoxels32 = pooled.Coordinates.Length,
                NeighborhoodRows = pooled.Coordinates.Length, NeighborhoodWidth = 27,
                TopologyBasis = "Upstream XYZ/Z-up", PreviewBasis = "Aurelian Y-up: (X,Z,-Y)",
                LogitMinimum = logits.Min(), LogitMaximum = logits.Max(),
                InferenceWallMilliseconds = inferenceMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                FinalReadbacks = math.ReadbackCount,
                Precision = "FP32 storage/accumulation with exact binary16 rounding at upstream FP16 torso operation boundaries",
                IndependentFullDecoderReferenceQualified = false,
                Stages = schedule.Measurements, Convolutions = graph.Measurements,
                NextBlocker = "Sparse shape/detail flow and learned mesh/material decoders; occupancy is not a final textured asset",
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Decoded occupancy: {full.Coordinates.Length} at 64^3, {pooled.Coordinates.Length} at 32^3, {inferenceMilliseconds / 1000:F1} s");
            Console.WriteLine("AURELIAN_TRELLIS2_OCCUPANCY_DECODED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(outputDirectory, "dominatus-inspection.json"),
                JsonSerializer.Serialize(schedule.Inspection, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
