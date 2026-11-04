using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private static object CheckPerformance(VulkanSolid3DRenderer renderer, string output)
    {
        var comparisons = new List<object>();
        var measurements = new List<HybridMeasurement>();
        Vector3 eye = new(0, 50, 120);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, 1000);
        projection.M22 *= -1;
        var camera = Matrix4x4.CreateLookAt(eye, new(0, 0, -60), Vector3.UnitY) * projection;
        foreach (var workload in new[]
        {
            (Kind: AnalyticPrimitiveKind3D.UnitSphere, Count: 20_000, Large: false),
            (Kind: AnalyticPrimitiveKind3D.UnitBox, Count: 20_000, Large: false),
            (Kind: AnalyticPrimitiveKind3D.Capsule, Count: 1, Large: true),
            (Kind: AnalyticPrimitiveKind3D.UnitSphere, Count: 256, Large: true),
        })
        {
            var kind = workload.Kind;
            string name = kind + (workload.Large ? (workload.Count > 1 ? "-overdraw" : "-large") : "");
            Vector3 viewEye = workload.Large ? new(0, 0, 3) : eye;
            Matrix4x4 viewCamera = workload.Large ? RenderingBindlessProof.Camera(viewEye, Vector3.Zero) : camera;
            var analytic = NativeAnalyticPrimitives3D.Create("analytic-forest-" + kind, kind);
            var mesh = Mesh(kind, workload.Large ? 96 : 40, lod: kind == AnalyticPrimitiveKind3D.UnitSphere && !workload.Large);
            var material = new ModelMaterial("forest") { BaseColor = new(.3f, .6f, .16f, 1), Metallic = 0, Roughness = .6f };
            SceneInstance3D[] Instances(NativeMesh3D geometry) => Enumerable.Range(0, workload.Count).Select(index =>
                new SceneInstance3D("shape-" + index, geometry, material,
                    workload.Large ? Matrix4x4.CreateScale(2.4f) * Matrix4x4.CreateTranslation(0, 0, -index * .04f) :
                    Matrix4x4.CreateScale(.8f + index % 7 * .03f) * Matrix4x4.CreateTranslation(
                        (index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f))).ToArray();
            Native3DScene exact = new([], []) { Instances = new NativeInstanceSet3D(Instances(analytic)) };
            Native3DScene meshed = new([], []) { Instances = new NativeInstanceSet3D(Instances(mesh)) };
            comparisons.Add(ComparePrepared(renderer, exact, meshed, viewCamera, viewEye, output, name));
            foreach (bool useAnalytic in new[] { false, true, true, false })
            {
                var scene = useAnalytic ? exact : meshed;
                renderer.EnableHierarchicalZ = false;
                renderer.LodPixelError = .25f;
                renderer.Render(scene, viewCamera, viewEye, Clear, capture: true);
                var cpu = new List<double>();
                var gpu = new List<double>();
                long started = 0;
                for (int frame = 0; frame < 224; frame++)
                {
                    if (frame == 128) started = Stopwatch.GetTimestamp();
                    var result = renderer.Render(scene, viewCamera, viewEye, Clear);
                    if (frame < 128) continue;
                    Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 }, "Hybrid steady frames revisited resident CPU instances.");
                    cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                    if (result.CompletedGpuFrame is { } completed)
                        gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                }
                renderer.WaitForPendingFrames();
                double fps = 96 / Stopwatch.GetElapsedTime(started).TotalSeconds;
                cpu.Sort();
                gpu.Sort();
                long submitted = renderer.ReadVisibilityForQualification().Sum(group => group.SubmittedTriangles);
                var measured = new HybridMeasurement(name, useAnalytic, gpu[gpu.Count / 2], cpu[cpu.Count / 2], fps, submitted);
                measurements.Add(measured);
                Console.WriteLine($"{name}, analytic={useAnalytic}: GPU {measured.GpuMilliseconds:F3} ms, CPU {measured.CpuMilliseconds:F3} ms, {fps:F1} FPS");
            }
        }
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(measurements,
            new JsonSerializerOptions { WriteIndented = true }));
        var sphere = measurements.Where(sample => sample.Kind == AnalyticPrimitiveKind3D.UnitSphere.ToString()).ToArray();
        Require(sphere.Where(sample => sample.Analytic).Max(sample => sample.GpuMilliseconds)
            < sphere.Where(sample => !sample.Analytic).Min(sample => sample.GpuMilliseconds) * .85,
            "Analytic curved geometry did not improve complete GPU frame time by 15% in both ABBA repetitions.");
        return new { WarmupFrames = 128, MeasuredFrames = 96, Comparisons = comparisons, Measurements = measurements };
    }

    private static object ComparePrepared(VulkanSolid3DRenderer renderer, Native3DScene analytic, Native3DScene meshed,
        Matrix4x4 camera, Vector3 eye, string output, string name)
    {
        renderer.EnableHierarchicalZ = false;
        renderer.LodPixelError = .25f;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(meshed, camera, eye, Clear, capture: true);
        var referenceIds = renderer.ReadIdentitiesForQualification().Ids;
        long referenceTriangles = renderer.ReadVisibilityForQualification().Sum(group => group.SubmittedTriangles);
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(analytic, camera, eye, Clear, capture: true);
        var actualIds = renderer.ReadIdentitiesForQualification().Ids;
        int foreground = Enumerable.Range(0, Width * Height).Count(pixel => actualIds[pixel * 2] != 0);
        Require(foreground > 1000, "Hybrid timing/quality specimen rendered too little foreground.");
        double error = reference.Pixels!.Where((_, index) => index % 4 != 3)
            .Zip(actual.Pixels!.Where((_, index) => index % 4 != 3), (a, b) => Math.Abs(a - b)).Average();
        int coverage = Enumerable.Range(0, Width * Height).Count(pixel =>
            (referenceIds[pixel * 2] == 0) != (actualIds[pixel * 2] == 0));
        Aurelian.Games.NativeGameGraphics.WritePng(Path.Combine(output, name + "-prepared-mesh.png"), Width, Height, reference.Pixels!);
        Aurelian.Games.NativeGameGraphics.WritePng(Path.Combine(output, name + "-analytic.png"), Width, Height, actual.Pixels!);
        Require(error < 1.2 && coverage < Width * Height * .005, "Prepared mesh/analytic quality gate failed: " + name + ", " + error);
        return new { Kind = name, MeanRgbError = error, ChangedCoverage = coverage / (Width * Height * 1d),
            ForegroundPixels = foreground, ReferenceTriangles = referenceTriangles, MeshLods = meshed.Instances[0].Mesh.LodRanges };
    }

    private sealed record HybridMeasurement(string Kind, bool Analytic, double GpuMilliseconds,
        double CpuMilliseconds, double FramesPerSecond, long SubmittedProxyOrMeshTriangles);
}
