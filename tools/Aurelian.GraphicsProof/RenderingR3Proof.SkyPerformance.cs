using System.Diagnostics;
using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object MeasureSkyTransport(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        ReferenceCamera3D camera, DiffuseProbeGrid grid, string output)
    {
        const int width = 1280;
        const int height = 720;
        var source = SkySource(false);
        var atlas = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = [];
        renderer.Settings = Settings() with
        {
            Shadows = true, SunDirection = Vector3.Normalize(new(1, .65f, .3f)), SunIntensity = 5,
            EnvironmentIntensity = 1, ShadowRadius = 6, ShadowDistance = 20,
        };
        renderer.Environment = atlas;
        renderer.EnvironmentSource = source;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var rows = new List<object>();
        foreach (string variant in new[] { "off", "visibility-only", "full-sky-bounce" })
        {
            renderer.DynamicDiffuse = new()
            {
                Mode = variant == "off" ? DynamicDiffuseMode3D.Off : DynamicDiffuseMode3D.Runtime,
                Grid = grid, RaysPerProbePerFrame = 16, SecondarySkyLighting = variant == "full-sky-bounce",
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
            Require(gpu.Count > 50, "Sky measurement lost GPU timestamp coverage.");
            double fps = 64 / Stopwatch.GetElapsedTime(started).TotalSeconds;
            rows.Add(new { Variant = variant, MedianCpuMilliseconds = Median(cpu), MedianGpuMilliseconds = Median(gpu), FramesPerSecond = fps });
            Console.WriteLine($"Sky 720p {variant}: CPU {Median(cpu):F3}ms GPU {Median(gpu):F3}ms {fps:F1}fps");
            var captured = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, "sky-720p-" + variant + ".png"), width, height, captured.Pixels!);
        }
        return new { Width = width, Height = height, Probes = grid.Count, RaysPerProbePerFrame = 16,
            WarmupFrames = 24, MeasuredFrames = 64, Variants = rows };
    }
}
