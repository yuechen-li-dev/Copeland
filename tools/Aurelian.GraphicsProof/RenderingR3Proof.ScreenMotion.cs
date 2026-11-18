using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object CheckScreenReflectionMotion(AurelianVulkanPlant plant, GameAssets assets, string output,
        EnvironmentRadiance3D source, EnvironmentLighting environment, Native3DScene layered, Native3DScene offscreen)
    {
        const int width = ReflectionWidth - 1;
        const int height = ReflectionHeight - 1;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var hardwareTarget = new VulkanNativeFrameTarget(plant, width, height);
        using var screen = ReflectionRenderer(plant, assets, target, source, environment);
        using var hardware = ReflectionRenderer(plant, assets, hardwareTarget, source, environment);
        hardware.Settings = hardware.Settings with
        {
            GlossyReflections = GlossyReflectionMode3D.HalfResolution,
            AntiAliasing = AntiAliasing3D.Temporal,
        };
        screen.Settings = hardware.Settings with { ScreenSpaceReflectionBounds = true };
        int cameraCandidates = 0, cameraFallbacks = 0;
        byte[]? first = null;
        byte[]? last = null;
        const int cameraFrames = 12;
        for (int frame = 0; frame < cameraFrames; frame++)
        {
            Vector3 offset = new((frame - 4) * .16f, 0, 0);
            var camera = ScreenCamera with { Eye = ScreenCamera.Eye + offset, Target = ScreenCamera.Target + offset };
            Matrix4x4 clip = RegionalMatrix(camera, width, height);
            last = RenderMotionPair(layered, clip, camera.Eye, out var counts);
            first ??= last;
            cameraCandidates += counts.Candidates;
            cameraFallbacks += counts.HardwareFallback;
        }
        Require(cameraCandidates > 500 && cameraFallbacks > 500 && Error(first!, last!) > .01,
            "The camera replay did not exercise visible and off-screen ray segments.");
        NativeGameGraphics.WritePng(Path.Combine(output, "camera-pan-first.png"), width, height, first!);
        NativeGameGraphics.WritePng(Path.Combine(output, "camera-pan-last.png"), width, height, last!);

        const int warmup = 8;
        Matrix4x4 offscreenClip = ReflectionCamera(width, height);
        for (int frame = 0; frame < warmup; frame++)
        {
            hardware.Render(offscreen, offscreenClip, ReflectionEye, Clear);
            screen.Render(offscreen, offscreenClip, ReflectionEye, Clear);
        }
        var before = RenderMotionPair(offscreen, offscreenClip, ReflectionEye, out _);
        var moved = MoveReflectionCard(offscreen, 1.8f);
        var after = RenderMotionPair(moved, offscreenClip, ReflectionEye, out var movedCounts);
        Require(Error(before, after) > .02 && movedCounts.HardwareFallback == movedCounts.Attempts && movedCounts.SurfaceHits > 200,
            "The moved off-screen emitter did not exercise hardware fallback.");
        Require(screen.RaySceneStatistics is { TopLevelUpdates: 1 }, "The moved emitter bypassed TLAS refit.");
        NativeGameGraphics.WritePng(Path.Combine(output, "offscreen-motion-before.png"), width, height, before);
        NativeGameGraphics.WritePng(Path.Combine(output, "offscreen-motion-after.png"), width, height, after);
        return new { CameraFrames = cameraFrames, Width = width, Height = height, Jittered = true, CameraCandidates = cameraCandidates,
            CameraFallbacks = cameraFallbacks, OffscreenRefitMatches = true, Motion = movedCounts, Statistics = screen.RaySceneStatistics };

        byte[] RenderMotionPair(Native3DScene scene, Matrix4x4 clip, Vector3 eye, out ScreenHitCounts counts)
        {
            var expected = hardware.Render(scene, clip, eye, Clear, capture: true).Pixels!;
            var actual = screen.Render(scene, clip, eye, Clear, capture: true).Pixels!;
            Require(actual.SequenceEqual(expected), "The screen-assisted camera or emitter replay diverged from hardware.");
            counts = CompareScreenHits(hardware.ReadGlossyReflectionsForQualification(), screen.ReadGlossyReflectionsForQualification());
            Require(counts.IdentityDifferences == 0 && counts.DistanceDifferences == 0, "A moving ray hit differed from hardware.");
            return actual;
        }
    }
}
