using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Assets.Models;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;

internal static class Trellis2MeshDecoderProof
{
    public static void Run(string weightsDirectory, string sampleDirectory, string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "mesh-evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"MeshDecoded\":false,\"MaterialsDecoded\":false}");
        string weightsPath = Path.Combine(weightsDirectory, Trellis2ShapeWeights.DecoderName);
        Trellis2OccupancyWeights.Validate(weightsPath, Trellis2ShapeWeights.DecoderBytes, Trellis2ShapeWeights.DecoderHash);
        byte[] latentBytes = File.ReadAllBytes(Path.Combine(sampleDirectory, "decoder-latent.f32"));
        string latentHash = Hash(latentBytes);
        using var sampling = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleDirectory, "sampling-evidence.json")));
        if (!sampling.RootElement.GetProperty("Accepted").GetBoolean()
            || sampling.RootElement.GetProperty("DecoderLatentSha256").GetString() != latentHash
            || sampling.RootElement.GetProperty("ModelEvaluations").GetInt32() != 21)
        {
            throw new InvalidDataException("Full decoder requires the completed hash-matched shape sampler.");
        }
        var coordinates = JsonSerializer.Deserialize<VoxelCoordinate[]>(File.ReadAllText(Path.Combine(sampleDirectory, "coordinates.json")))
            ?? throw new InvalidDataException("Missing sparse coordinates.");
        var topology = SparseVoxelTopology.Create(32, coordinates);
        using var weights = new SafeTensorFile(weightsPath);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian TRELLIS learned mesh decoder"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 134217728);
        using var feedforward = new DominatusComputeFeedforward();
        math.StreamController = feedforward;
        var graph = new Trellis2SparseDecoderStage(math, weights);
        var schedule = new DominatusComputeSchedule("trellis.shape.mesh") { Observe = InferenceProofLog.Write };
        var timer = Stopwatch.StartNew();
        try
        {
            schedule.Run(graph.DecodeComplete(MemoryMarshal.Cast<byte, float>(latentBytes).ToArray(), topology));
            float[] features = graph.Features ?? throw new InvalidDataException("No final seven-channel mesh features.");
            var finalTopology = graph.FinalTopology ?? throw new InvalidDataException("No final topology.");
            var mesh = FlexibleDualGridMesh.Decode(finalTopology, features);
            if (finalTopology.Resolution != 512 || mesh.Quads == 0 || math.ReadbackCount != 5)
            {
                throw new InvalidDataException("Invalid complete decoder grid, extracted mesh or readback count.");
            }
            File.WriteAllBytes(Path.Combine(output, "features.f32"), MemoryMarshal.AsBytes(features.AsSpan()).ToArray());
            File.WriteAllText(Path.Combine(output, "coordinates.json"), JsonSerializer.Serialize(finalTopology.Coordinates));
            File.WriteAllText(Path.Combine(output, "subdivisions.json"), JsonSerializer.Serialize(graph.Subdivisions.Select(item => new
            {
                item.Topology.Resolution, item.Topology.Coordinates, item.ParentRows, item.ChildSlots,
            })));
            WriteObj(mesh, Path.Combine(output, "mesh.obj"));
            var preview = ReduceForPreview(mesh);
            Render(plant, assets, new(preview.Positions, preview.TriangleIndices, 0, 0), output);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Accepted = true, MeshDecoded = true, MaterialsDecoded = false, FullGenerationQualified = false,
                Scope = "All 32 sparse ConvNeXt blocks, four learned upsampling stages and seven-channel flexible dual-grid mesh extraction",
                WeightSha256 = Trellis2ShapeWeights.DecoderHash, LatentSha256 = latentHash,
                FeatureSha256 = Hash(MemoryMarshal.AsBytes(features.AsSpan())),
                ObjSha256 = Hash(File.ReadAllBytes(Path.Combine(output, "mesh.obj"))),
                CoordinateSha256 = Hash(File.ReadAllBytes(Path.Combine(output, "coordinates.json"))),
                SubdivisionSha256 = Hash(File.ReadAllBytes(Path.Combine(output, "subdivisions.json"))),
                FinalRows = finalTopology.Coordinates.Length, finalTopology.Resolution,
                Vertices = mesh.Positions.Length, Triangles = mesh.TriangleIndices.Length / 3,
                PreviewLod = new { preview.CellsPerUnit, Vertices = preview.Positions.Length,
                    Triangles = preview.TriangleIndices.Length / 3, preview.MaximumPositionDeviation },
                mesh.Quads, mesh.MissingNeighborQuads,
                Levels = graph.Subdivisions.Select(item => new { item.Topology.Resolution, Rows = item.ParentRows.Length }),
                DecoderWallMilliseconds = timer.Elapsed.TotalMilliseconds,
                PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
                Readbacks = math.ReadbackCount,
                ReadbackScope = "Four subdivision logit boundaries for CPU topology and one final seven-channel mesh output",
                Runtime = "C# CPU sparse topology and mesh extraction; Vulkan learned feature arithmetic; no CUDA/Python/Torch hot path",
                PreviewMaterial = "Neutral gray diagnostic material, not generated texture/PBR output",
                NextBlocker = "Shape-conditioned 64-input-channel texture flow and six-channel texture decoder with shared subdivision guides",
                IndependentFullDecoderQualified = false,
                Stages = schedule.Measurements, Convolutions = graph.Measurements,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Learned mesh: {mesh.Positions.Length} vertices, {mesh.TriangleIndices.Length / 3} triangles, {timer.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine("AURELIAN_TRELLIS2_MESH_DECODED");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "dominatus-inspection.json"), JsonSerializer.Serialize(schedule.Inspection));
        }
    }

    internal static MeshClusterReduction ReduceForPreview(FlexibleDualGridMesh mesh)
    {
        foreach (int resolution in new[] { 128, 112, 96, 80, 64 })
        {
            var reduced = MeshClusterReducer.Reduce(mesh.Positions, mesh.TriangleIndices, resolution);
            if (reduced.TriangleIndices.Length <= 999999)
            {
                return reduced;
            }
        }
        throw new InvalidDataException("AUR-TRELLIS-MESH-001: Preview LOD cannot meet the existing renderer frame budget.");
    }

    internal static void Render(AurelianVulkanPlant plant, GameAssets assets, FlexibleDualGridMesh mesh, string output)
    {
        var vertices = new NativeModel3DVertex[mesh.TriangleIndices.Length];
        for (int first = 0; first < vertices.Length; first += 3)
        {
            Vector3 a = ToAurelian(mesh.Positions[mesh.TriangleIndices[first]]);
            Vector3 b = ToAurelian(mesh.Positions[mesh.TriangleIndices[first + 1]]);
            Vector3 c = ToAurelian(mesh.Positions[mesh.TriangleIndices[first + 2]]);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY;
            vertices[first] = new(a, normal, Vector4.One, Vector2.Zero, new(1, 0, 0, 1));
            vertices[first + 1] = vertices[first] with { Position = b };
            vertices[first + 2] = vertices[first] with { Position = c };
        }
        var material = new ModelMaterial("learned-mesh-neutral")
        {
            BaseColor = new(.65f, .65f, .65f, 1), Roughness = .8f, DoubleSided = true,
        };
        var scene = new Native3DScene([], vertices.Chunk(60000).Select(chunk => new NativeModel3DBatch(chunk, material)).ToArray());
        RenderScene(plant, assets, scene, output, "mesh");
    }

    internal static void RenderScene(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene, string output, string prefix)
    {
        using var target = new VulkanNativeFrameTarget(plant, 960, 720);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
            modelProgram: assets.Shader("StaticModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"));
        renderer.Settings = Graphics3DSettings.Default with { AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, Shadows = false };
        Vector3[] eyes = [new(1.4f, .7f, 1.8f), new(-1.4f, .7f, -1.8f), new(0, .15f, 2.3f)];
        string[] names = [prefix + "-preview", prefix + "-back", prefix + "-front"];
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.7f, 960f / 720, .01f, 10);
        projection.M22 *= -1;
        for (int index = 0; index < eyes.Length; index++)
        {
            Vector3 eye = eyes[index];
            Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
            var frame = renderer.Render(scene, camera, eye, new(.035f, .04f, .055f, 1), capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, names[index] + ".png"), 960, 720, frame.Pixels!);
        }
    }

    private static void WriteObj(FlexibleDualGridMesh mesh, string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("# Learned TRELLIS.2 flexible dual-grid mesh; Aurelian Y-up; materials not generated yet.");
        foreach (Vector3 position in mesh.Positions)
        {
            Vector3 point = ToAurelian(position);
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"v {point.X:R} {point.Y:R} {point.Z:R}"));
        }
        for (int first = 0; first < mesh.TriangleIndices.Length; first += 3)
        {
            writer.WriteLine($"f {mesh.TriangleIndices[first] + 1} {mesh.TriangleIndices[first + 1] + 1} {mesh.TriangleIndices[first + 2] + 1}");
        }
    }

    private static Vector3 ToAurelian(Vector3 value) => new(value.X, value.Z, -value.Y);
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
