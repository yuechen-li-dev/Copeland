using System.Diagnostics;
using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object CheckSecondaryMotion(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box,
        ReferenceCamera3D camera, Matrix4x4 matrix, DiffuseProbeGrid grid, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        LocalLight3D[] lights = [new(new(.5f, 2.8f, 2), Vector3.One, 25, 10)];
        renderer.LocalLights = lights;
        renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid,
            RaysPerProbePerFrame = 128, CaptureSecondaryWitness = true };
        var initial = SecondaryScene(box, "all-channels");
        NativeInstanceSet3D? previous = null;
        Native3DScene current = initial;
        for (int frame = 0; frame < 6; frame++)
        {
            var instances = initial.Instances.Select(instance => instance.Identity == "secondary-source"
                ? instance with { Transform = instance.Transform * Matrix4x4.CreateRotationY(frame * .04f)
                    * Matrix4x4.CreateTranslation(frame * .045f, 0, .03f * frame) }
                : instance);
            previous = new NativeInstanceSet3D(instances, previous);
            current = initial with { Instances = previous };
            renderer.Render(current, matrix, camera.Eye, Clear, capture: frame == 5);
        }
        var actual = renderer.ReadLightingForQualification();
        var (field, witness) = SecondaryOracleField(previous!, grid, renderer.ReadSecondaryWitnessForQualification(), lights);
        var statistics = renderer.RaySceneStatistics!;
        Require(statistics.TopLevelUpdates > 0, "Moving secondary material proof did not exercise TLAS updates.");
        SaveSamplingImage(plant, assets, output, "moving-full", actual);
        renderer.SetCompiledDiffuseProbes(field);
        renderer.DynamicDiffuse = renderer.DynamicDiffuse with { Mode = DynamicDiffuseMode3D.CompiledOnly };
        current = current with { Instances = new NativeInstanceSet3D(current.Instances.Select(instance => instance with { PreviousTransform = null }), previous) };
        renderer.Render(current, matrix, camera.Eye, Clear, capture: true);
        var oracle = renderer.ReadLightingForQualification();
        SaveSamplingImage(plant, assets, output, "moving-oracle", oracle);
        double error = Rmse(actual.LinearRgb, oracle.LinearRgb);
        Require(error < .0005, "Moving secondary shading read stale geometry or material data.");
        Console.WriteLine($"Moving secondary material: HDR RMSE {error:E3}, TLAS updates {statistics.TopLevelUpdates}");
        return new { Frames = 6, HdrRmse = error, Witness = witness, Statistics = statistics };
    }

    private static object MeasureSecondaryMaterials(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        ReferenceCamera3D camera, DiffuseProbeGrid grid)
    {
        const int width = 1280;
        const int height = 720;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        renderer.LocalLights = [new(new(.5f, 2.8f, 2), Vector3.One, 25, 10)];
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var rows = new List<object>();
        foreach (string variant in new[] { "off", "factor-only", "full-materials" })
        {
            renderer.DynamicDiffuse = new()
            {
                Mode = variant == "off" ? DynamicDiffuseMode3D.Off : DynamicDiffuseMode3D.Runtime,
                Grid = grid, RaysPerProbePerFrame = 16, SecondaryMaterials = variant == "full-materials",
            };
            var cpu = new List<double>();
            var gpu = new List<double>();
            long started = 0;
            for (int frame = 0; frame < 88; frame++)
            {
                if (frame == 24) started = Stopwatch.GetTimestamp();
                var result = renderer.Render(scene, matrix, camera.Eye, Clear);
                if (frame < 24) continue;
                cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
                if (result.CompletedGpuFrame is { } complete)
                    gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
            }
            renderer.WaitForPendingFrames();
            Require(gpu.Count > 50, "Secondary performance measurement lost GPU timestamps.");
            double fps = 64 / Stopwatch.GetElapsedTime(started).TotalSeconds;
            rows.Add(new { Variant = variant, MedianCpuMilliseconds = Median(cpu), MedianGpuMilliseconds = Median(gpu), FramesPerSecond = fps });
            Console.WriteLine($"Secondary 720p {variant}: CPU {Median(cpu):F3}ms GPU {Median(gpu):F3}ms {fps:F1}fps");
        }
        return new { Width = width, Height = height, Probes = grid.Count, RaysPerProbePerFrame = 16,
            WarmupFrames = 24, MeasuredFrames = 64, CaptureSecondaryWitness = false, Variants = rows };
    }
}
