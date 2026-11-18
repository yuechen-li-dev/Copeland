using System.Numerics;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object CheckGlossyCameraMotion(AurelianVulkanPlant plant, GameAssets assets, string output,
        Native3DScene scene, Matrix4x4 camera, EnvironmentRadiance3D source, EnvironmentLighting environment,
        ReferenceImage3D reference)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var coldTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        using var control = ReflectionRenderer(plant, assets, coldTarget, source, environment);
        renderer.Settings = renderer.Settings with { GlossyReflections = GlossyReflectionMode3D.FullResolution };
        control.Settings = renderer.Settings;
        Vector3 shift = new(-.7f, 0, 0);
        Matrix4x4 priorCamera = Matrix4x4.CreateTranslation(-shift) * camera;
        for (int frame = 0; frame < GlossyWarmupFrames; frame++)
        {
            renderer.Render(scene, priorCamera, ReflectionEye + shift, Clear);
            control.Render(scene, priorCamera, ReflectionEye + shift, Clear);
        }
        control.ResetTemporalHistory();
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        control.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var reprojected = renderer.ReadLightingForQualification();
        var cold = control.ReadLightingForQualification();
        double error = Rmse(reprojected.LinearRgb, reference.LinearRgb);
        double coldError = Rmse(cold.LinearRgb, reference.LinearRgb);
        int newlyVisible = 0;
        float maximumDifference = 0;
        for (int y = 35; y < 104; y++)
        {
            // Current world X > 1.4 could not have been on the previous viewport.
            for (int x = 205; x < 228; x++)
            {
                int index = (y * ReflectionWidth + x) * 3;
                if (reference.LinearRgb[index] <= 0) continue;
                for (int channel = 0; channel < 3; channel++)
                    maximumDifference = MathF.Max(maximumDifference, MathF.Abs(cold.LinearRgb[index + channel] - reprojected.LinearRgb[index + channel]));
                newlyVisible++;
            }
        }
        Console.WriteLine($"R5b camera motion: HDR RMSE {error:F6} vs cold {coldError:F6}; {newlyVisible} disoccluded pixels max delta {maximumDifference:F6}.");
        Require(error < coldError * .7 && newlyVisible > 500 && maximumDifference == 0,
            "Camera reprojection did not improve overlapping pixels or leaked history into the disocclusion.");
        SaveSamplingImage(plant, assets, output, "camera-reprojected", reprojected);
        SaveSamplingImage(plant, assets, output, "camera-cold", cold);
        const float yaw = .2f;
        Matrix4x4 rotatedCamera = Matrix4x4.CreateRotationY(yaw) * camera;
        Vector3 rotatedEye = Vector3.Transform(ReflectionEye, Matrix4x4.CreateRotationY(-yaw));
        control.ResetTemporalHistory();
        byte[] rotated = renderer.Render(scene, rotatedCamera, rotatedEye, Clear, capture: true).Pixels!;
        byte[] coldRotation = control.Render(scene, rotatedCamera, rotatedEye, Clear, capture: true).Pixels!;
        Require(rotated.SequenceEqual(coldRotation), "A camera rotation reused history from a different reflection lobe.");
        return new { Rmse = error, ColdRmse = coldError, DisoccludedPixels = newlyVisible,
            MaxDisoccludedDelta = maximumDifference, RotationRadians = yaw, RotationMatchesExplicitReset = true };
    }

    private static object CheckGlossySecondaryMotion(AurelianVulkanPlant plant, GameAssets assets, string output,
        Native3DScene scene, Matrix4x4 camera, EnvironmentRadiance3D source, EnvironmentLighting environment, GlossyReflectionMode3D mode)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var coldTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        using var control = ReflectionRenderer(plant, assets, coldTarget, source, environment);
        renderer.Settings = renderer.Settings with { GlossyReflections = mode };
        control.Settings = renderer.Settings;
        byte[]? before = null;
        for (int frame = 0; frame < GlossyWarmupFrames; frame++)
        {
            var result = renderer.Render(scene, camera, ReflectionEye, Clear, capture: frame == GlossyWarmupFrames - 1);
            control.Render(scene, camera, ReflectionEye, Clear);
            if (result.Pixels is not null) before = result.Pixels;
        }
        control.ResetTemporalHistory();
        var moved = MoveReflectionCard(scene, 1.8f);
        byte[] after = renderer.Render(moved, camera, ReflectionEye, Clear, capture: true).Pixels!;
        byte[] reset = control.Render(moved, camera, ReflectionEye, Clear, capture: true).Pixels!;
        Require(renderer.RaySceneStatistics is { TopLevelBuilds: 1, TopLevelUpdates: 1 },
            "Glossy secondary motion bypassed the retained TLAS refit.");
        Require(before is not null && Error(before, after) > .025 && after.SequenceEqual(reset),
            "Moving the off-camera emitter retained stale glossy history.");
        NativeGameGraphics.WritePng(Path.Combine(output, $"secondary-motion-{mode}-before.png"), ReflectionWidth, ReflectionHeight, before!);
        NativeGameGraphics.WritePng(Path.Combine(output, $"secondary-motion-{mode}-after.png"), ReflectionWidth, ReflectionHeight, after);
        return new { Mode = mode.ToString(), WarmupFrames = GlossyWarmupFrames, MatchesExplicitReset = true, Statistics = renderer.RaySceneStatistics };
    }

    private static object CheckSharpInQuarterRate(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        Matrix4x4 camera, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var baseline = renderer.ReadLightingForQualification();
        renderer.Settings = renderer.Settings with { GlossyReflections = GlossyReflectionMode3D.HalfResolution };
        for (int phase = 0; phase < 4; phase++)
        {
            renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
            double error = Rmse(baseline.LinearRgb, renderer.ReadLightingForQualification().LinearRgb);
            Require(error < .0001, $"Quarter-rate reconstruction shaded a sharp pixel differently at phase {phase}: {error}.");
        }
        return new { Phases = 4, MatchesSharp = true };
    }

    private static object MeasureGlossyReflections(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        using var game = new BeaconGame();
        var publisher = new BeaconResidentScene3D();
        var arena = publisher.Build(game, includeViewmodel: false);
        arena = arena with
        {
            Instances = new NativeInstanceSet3D(arena.Instances.Select(instance => instance.Identity.Contains("/floor/", StringComparison.Ordinal)
                ? instance with { Material = instance.Material with { Roughness = .26f } }
                : instance)),
        };
        var arenaEnvironment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        var camera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var rows = new List<object>();
        foreach (int width in new[] { 1280, 1920 })
        {
            int height = width * 9 / 16;
            using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
            using var beaconTarget = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            using var beacon = BeaconRenderer(plant, assets, beaconTarget, arenaEnvironment);
            beacon.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
            Matrix4x4 clip = ReflectionCamera(width, height);
            Matrix4x4 beaconClip = RegionalMatrix(camera, width, height);
            foreach (var mode in new[] { GlossyReflectionMode3D.Raw, GlossyReflectionMode3D.FullResolution,
                GlossyReflectionMode3D.HalfResolution, GlossyReflectionMode3D.Off })
            {
                renderer.Settings = renderer.Settings with { SharpReflections = false, GlossyReflections = mode };
                var current = scene;
                object moving = CollectReflectionTiming(frame =>
                {
                    current = MoveReflectionCard(current, -.72f + .25f * MathF.Sin(frame * .2f));
                    return renderer.Render(current, clip, ReflectionEye, Clear);
                });
                renderer.WaitForPendingFrames();
                var measured = renderer.Render(MoveReflectionCard(current, -.72f), clip, ReflectionEye, Clear, capture: true);
                double trace = measured.GpuPassTimes.Single(pass => pass.Pass == "sharp-reflections").Milliseconds;
                double reconstruct = measured.GpuPassTimes.Single(pass => pass.Pass == "glossy-reflection-reconstruction").Milliseconds;
                rows.Add(new { Width = width, Height = height, Workload = "moving-mirror", Mode = mode.ToString(),
                    Timing = moving, TraceMilliseconds = trace, ReconstructionMilliseconds = reconstruct });
                Console.WriteLine($"R5b {width} moving {mode}: {moving}; trace {trace:F3}ms, reconstruction {reconstruct:F3}ms.");
                if (mode == GlossyReflectionMode3D.Raw) continue;
                beacon.Settings = beacon.Settings with { GlossyReflections = mode };
                object combined = CollectReflectionTiming(_ => beacon.Render(arena, beaconClip, camera.Eye, Clear));
                beacon.WaitForPendingFrames();
                Require(beacon.RaySceneStatistics is { TopLevelBuilds: 1 }, "Glossy Beacon rebuilt the GI TLAS.");
                rows.Add(new { Width = width, Height = height, Workload = "beacon-gi", Mode = mode.ToString(), Timing = combined });
                Console.WriteLine($"R5b {width} Beacon GI {mode}: {combined}.");
            }
        }
        return rows;
    }
}
