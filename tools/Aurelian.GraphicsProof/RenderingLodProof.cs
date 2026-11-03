using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingLodProof
{
    private const int Width = 640;
    private const int Height = 360;
    private static readonly NativeFrameClearColor Clear = new(.1f, .15f, .22f, 1);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        NativeMesh3D mesh = CompileAsset(output, out object assetEvidence);
        int nondegenerateTriangles = Enumerable.Range(0, mesh.Indices.Length / 3).Count(triangle =>
        {
            Vector3 a = mesh.Vertices[(int)mesh.Indices[triangle * 3]].Position;
            Vector3 b = mesh.Vertices[(int)mesh.Indices[triangle * 3 + 1]].Position;
            Vector3 c = mesh.Vertices[(int)mesh.Indices[triangle * 3 + 2]].Position;
            return Vector3.Cross(b - a, c - a).LengthSquared() > 1e-12f;
        });
        Require((long)nondegenerateTriangles * 20_000 >= 20_000_000, "Forest needs twenty million nondegenerate source triangles.");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian offline LOD"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        var materials = RenderingBindlessProof.Materials(64);
        var source = Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D("tree-" + index, mesh,
            materials[index % materials.Length], Matrix4x4.CreateScale(.8f + index % 7 * .07f)
                * Matrix4x4.CreateRotationY(index * .71f)
                * Matrix4x4.CreateTranslation((index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f))).ToArray();
        Native3DScene forest = new([], []) { Instances = new NativeInstanceSet3D(source) };
        var comparisons = new List<object>();
        foreach (Vector3 eye in new[] { new Vector3(0, 3, 8), new(0, 50, 120), new(20, 8, 12), new(-40, 20, -30) })
        {
            var camera = Camera(eye, new(0, 1.4f, -60));
            comparisons.Add(Compare(renderer, forest, camera, eye, output, "forest-" + comparisons.Count));
        }
        object boundaries = CheckBoundaries(renderer, mesh, output);
        var measurements = new List<object>();
        Vector3 benchmarkEye = new(0, 50, 120);
        var benchmarkCamera = Camera(benchmarkEye, new(0, 1.4f, -60));
        var baseline = new List<double>();
        var selected = new List<double>();
        foreach (bool enable in new[] { false, true, true, false })
        {
            renderer.EnableHierarchicalZ = false;
            renderer.LodPixelError = enable ? .25f : 0;
            renderer.Render(forest, benchmarkCamera, benchmarkEye, Clear, capture: true);
            var cpu = new List<double>();
            var gpu = new List<double>();
            long started = 0;
            for (int frame = 0; frame < 224; frame++)
            {
                if (frame == 128) started = Stopwatch.GetTimestamp();
                var result = renderer.Render(forest, benchmarkCamera, benchmarkEye, Clear);
                if (frame < 128) continue;
                cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                if (result.CompletedGpuFrame is { } completed)
                    gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 }, "LOD revisited resident CPU instances.");
            }
            renderer.WaitForPendingFrames();
            double fps = 96 / Stopwatch.GetElapsedTime(started).TotalSeconds;
            var reads = renderer.ReadVisibilityForQualification();
            long triangles = reads.Sum(read => read.SubmittedTriangles);
            Require(triangles >= (enable ? 1_000_000 : 10_000_000), "Timing control submitted too little forest geometry.");
            (enable ? selected : baseline).Add(Median(gpu));
            measurements.Add(new { Enabled = enable, GpuMilliseconds = Median(gpu), CpuMilliseconds = Median(cpu),
                FramesPerSecond = fps, SubmittedTriangles = triangles, Draws = reads.Sum(read => read.DrawCount) });
            Console.WriteLine($"LOD={enable}: GPU {Median(gpu):F3} ms, CPU {Median(cpu):F3} ms, {triangles} triangles, {fps:F1} FPS");
        }
        bool timingQualified = selected.Max() < baseline.Min() * .85;
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = timingQualified, Device = plant.Facts, Asset = assetEvidence, SourceTriangles = (long)mesh.Indices.Length / 3 * source.Length,
            NondegenerateSourceTriangles = (long)nondegenerateTriangles * source.Length, PixelErrorBudget = .25,
            WarmupFrames = 128, MeasuredFrames = 96, Comparisons = comparisons, Boundaries = boundaries, Measurements = measurements,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Require(timingQualified, "LOD did not improve complete GPU frame time by 15% in both ABBA samples.");
        Console.WriteLine("AURELIAN_LOD_PASSED " + output);
    }

    private static object Compare(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera, Vector3 eye,
        string output, string name)
    {
        renderer.EnableHierarchicalZ = false;
        renderer.LodPixelError = 0;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(scene, camera, eye, Clear, capture: true);
        var referenceIds = renderer.ReadIdentitiesForQualification().Ids;
        long referenceTriangles = renderer.ReadVisibilityForQualification().Sum(read => read.SubmittedTriangles);
        renderer.LodPixelError = .25f;
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(scene, camera, eye, Clear, capture: true);
        var identities = renderer.ReadIdentitiesForQualification().Ids;
        var reads = renderer.ReadVisibilityForQualification();
        long triangles = reads.Sum(read => read.SubmittedTriangles);
        long sum = 0;
        int changedCoverage = 0;
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            for (int channel = 0; channel < 3; channel++)
                sum += Math.Abs(reference.Pixels![pixel * 4 + channel] - actual.Pixels![pixel * 4 + channel]);
            if ((referenceIds[pixel * 2] == 0) != (identities[pixel * 2] == 0)) changedCoverage++;
        }
        double mae = sum / (Width * Height * 3d);
        double coverage = changedCoverage / (Width * Height * 1d);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-lod0.png"), Width, Height, reference.Pixels!);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-lod.png"), Width, Height, actual.Pixels!);
        var evidence = new { Name = name, PixelBudget = renderer.LodPixelError, MeanAbsoluteError = mae,
            ChangedCoverage = coverage, ReferenceTriangles = referenceTriangles, SubmittedTriangles = triangles,
            Levels = reads.SelectMany(read => read.SelectedLods.Where((level, index) => (read.Ranks[index] & 255) > 0)).GroupBy(level => level)
                .ToDictionary(group => group.Key, group => group.Count()) };
        File.WriteAllText(Path.Combine(output, name + "-quality.json"), JsonSerializer.Serialize(evidence,
            new JsonSerializerOptions { WriteIndented = true }));
        Require(mae <= 1.2 && coverage <= .02, $"{name}: LOD quality exceeded MAE 1.2/255 or 2% changed silhouette coverage: {mae}, {coverage}.");
        return evidence;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target, float fieldOfView = .85f)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, (float)Width / Height, .1f, 1000);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
