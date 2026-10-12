using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Models;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;

internal static class Trellis2MaterialDecoderProof
{
    public static void Run(string weightsDirectory, string sampleDirectory, string meshDirectory, string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "material-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"MaterialsDecoded\":false}");
        string weightsPath = Path.Combine(weightsDirectory, Trellis2MaterialWeights.DecoderName);
        Trellis2OccupancyWeights.Validate(weightsPath, Trellis2MaterialWeights.DecoderBytes, Trellis2MaterialWeights.DecoderHash);
        byte[] latentBytes = File.ReadAllBytes(Path.Combine(sampleDirectory, "decoder-latent.f32"));
        byte[] meshFeatureBytes = File.ReadAllBytes(Path.Combine(meshDirectory, "features.f32"));
        byte[] coordinateBytes = File.ReadAllBytes(Path.Combine(meshDirectory, "coordinates.json"));
        byte[] guideBytes = File.ReadAllBytes(Path.Combine(meshDirectory, "subdivisions.json"));
        using var sampling = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleDirectory, "sampling-evidence.json")));
        using var meshEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(meshDirectory, "mesh-evidence.json")));
        if (!sampling.RootElement.GetProperty("Accepted").GetBoolean()
            || sampling.RootElement.GetProperty("ModelEvaluations").GetInt32() != 12
            || sampling.RootElement.GetProperty("DecoderLatentSha256").GetString() != Hash(latentBytes)
            || sampling.RootElement.GetProperty("ShapeDecoderLatentSha256").GetString() != meshEvidence.RootElement.GetProperty("LatentSha256").GetString()
            || sampling.RootElement.GetProperty("CoordinateSha256").GetString() != Hash(File.ReadAllBytes(Path.Combine(sampleDirectory, "coordinates.json")))
            || !meshEvidence.RootElement.GetProperty("MeshDecoded").GetBoolean()
            || meshEvidence.RootElement.GetProperty("FeatureSha256").GetString() != Hash(meshFeatureBytes)
            || meshEvidence.RootElement.GetProperty("CoordinateSha256").GetString() != Hash(coordinateBytes)
            || meshEvidence.RootElement.GetProperty("SubdivisionSha256").GetString() != Hash(guideBytes))
        {
            throw new InvalidDataException("Material decoding requires completed hash-matched material sampling, mesh features and subdivision guides.");
        }
        var inputCoordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(sampleDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing material input coordinates.");
        var topology = SparseVoxelTopology.Create(32, inputCoordinates);
        using var guideDocument = JsonDocument.Parse(guideBytes);
        var guides = new List<SparseVoxelSubdivision>();
        foreach (JsonElement guide in guideDocument.RootElement.EnumerateArray())
        {
            var coordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(guide.GetProperty("Coordinates"))
                ?? throw new InvalidDataException("Missing guide coordinates.");
            var parents = guide.GetProperty("ParentRows").EnumerateArray().Select(value => value.GetInt32()).ToImmutableArray();
            var slots = guide.GetProperty("ChildSlots").EnumerateArray().Select(value => value.GetInt32()).ToImmutableArray();
            guides.Add(new(SparseVoxelTopology.Create(guide.GetProperty("Resolution").GetInt32(), coordinates), parents, slots));
        }
        var finalCoordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(coordinateBytes)
            ?? throw new InvalidDataException("Missing mesh coordinates.");
        if (guides.Count != 4 || !guides[^1].Topology.Coordinates.SequenceEqual(finalCoordinates))
        {
            throw new InvalidDataException("Final guide row order differs from mesh row order.");
        }
        using var weights = new SafeTensorFile(weightsPath);
        if (weights.Tensors.Count != 284 || weights.Tensors["output_layer.weight"].Shape[0] != 6)
        {
            throw new InvalidDataException("Unexpected six-channel material decoder checkpoint.");
        }
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS generated PBR asset"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 134217728);
        using var feedforward = new DominatusComputeFeedforward();
        math.StreamController = feedforward;
        var graph = new Trellis2SparseDecoderStage(math, weights, "material.decoder");
        var schedule = new DominatusComputeSchedule("trellis.material.decoder") { Observe = InferenceProofLog.Write };
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(graph.DecodeComplete(MemoryMarshal.Cast<byte, float>(latentBytes).ToArray(), topology, guides));
            float[] features = graph.Features ?? throw new InvalidDataException("No decoded PBR features.");
            if (features.Length != finalCoordinates.Length * 6 || !features.All(float.IsFinite) || math.ReadbackCount != 1)
            {
                throw new InvalidDataException("Invalid complete six-channel material output or hidden readback.");
            }
            File.WriteAllBytes(Path.Combine(output, "features.f32"), MemoryMarshal.AsBytes(features.AsSpan()).ToArray());
            var attributes = features.Select(value => value * .5f + .5f).ToArray();
            File.WriteAllBytes(Path.Combine(output, "voxel-attributes.f32"), MemoryMarshal.AsBytes(attributes.AsSpan()).ToArray());
            File.WriteAllBytes(Path.Combine(output, "coordinates.json"), coordinateBytes);
            File.WriteAllBytes(Path.Combine(output, "subdivisions.json"), guideBytes);
            var mesh = FlexibleDualGridMesh.Decode(guides[^1].Topology, MemoryMarshal.Cast<byte, float>(meshFeatureBytes));
            var reduced = Trellis2MeshDecoderProof.ReduceForPreview(mesh);
            object assetEvidence = Trellis2MaterialAsset.Export(reduced, attributes, output);
            string glbPath = Path.Combine(output, "pot.glb");
            var imported = GlbModelImporter.Load("trellis-pot", glbPath);
            if (!imported.Success)
            {
                throw new InvalidDataException("Generated GLB failed Aurelian import: " + string.Join("; ", imported.Diagnostics.Select(item => item.Message)));
            }
            var batches = new List<NativeModel3DBatch>();
            foreach (var occurrence in imported.Model!.Occurrences)
            {
                var primitive = occurrence.Primitive;
                var vertices = primitive.Indices.Select(index =>
                {
                    var vertex = primitive.Vertices[index];
                    return new NativeModel3DVertex(Vector3.Transform(vertex.Position, occurrence.Transform),
                        Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, occurrence.Transform)), vertex.Color, vertex.Uv, vertex.Tangent);
                }).ToArray();
                batches.AddRange(vertices.Chunk(60000).Select(chunk => new NativeModel3DBatch(chunk, primitive.Material)));
            }
            Trellis2MeshDecoderProof.RenderScene(plant, assets, new([], batches), output, "material");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, MeshDecoded = true, MaterialsDecoded = true, EndToEndExecuted = true,
                FullGenerationQualified = false, IndependentMaterialDecoderQualified = false,
                Scope = "Generated image conditioning and occupancy/shape artifacts -> shape-conditioned material flow -> guided six-channel decoder -> LOD PBR GLB -> Aurelian importer -> Vulkan renderer",
                WeightSha256 = Trellis2MaterialWeights.DecoderHash, LatentSha256 = Hash(latentBytes),
                FeatureSha256 = Hash(MemoryMarshal.AsBytes(features.AsSpan())),
                MeshFeatureSha256 = Hash(meshFeatureBytes), SubdivisionSha256 = Hash(guideBytes),
                GlbSha256 = Hash(File.ReadAllBytes(glbPath)), Rows = finalCoordinates.Length, Resolution = 512,
                Readbacks = math.ReadbackCount, ReadbackScope = "One final six-channel output; guide topology supplied by the learned shape decoder",
                DecoderAndAssetWallMilliseconds = timer.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                Channels = new[] { "baseColor.r", "baseColor.g", "baseColor.b", "metallic", "roughness", "opacity" },
                Asset = assetEvidence, ImportedContentIdentity = imported.Model.ContentIdentity,
                InheritedQualificationLimits = "Occupancy and shape CFG velocity comparisons retain their prior failed 1% numerical bounds; execution success is separate from full upstream numerical qualification",
                Stages = schedule.Measurements,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Generated PBR GLB imported and rendered: {glbPath}");
            Console.WriteLine("AURELIAN_TRELLIS2_END_TO_END_EXECUTED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"), JsonSerializer.Serialize(schedule.Inspection));
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
