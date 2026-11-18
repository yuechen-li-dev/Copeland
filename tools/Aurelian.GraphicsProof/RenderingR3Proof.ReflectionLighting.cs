using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object CheckReflectionLighting(AurelianVulkanPlant plant, GameAssets assets, string output,
        Native3DScene flat, Matrix4x4 clip, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        var receiver = flat.Instances[0];
        var panel = flat.Instances[1];
        Vector3 center = panel.Transform.Translation;
        Vector3 panelNormal = -ReflectionDirection;
        Vector3 panelUp = Vector3.Normalize(new(0, -ReflectionDirection.Z, ReflectionDirection.Y));
        var smoothMesh = new NativeMesh3D("reflection-lit-smooth", panel.Mesh.Vertices.Select(vertex => vertex with
        {
            Normal = Vector3.Normalize(panelNormal + Vector3.UnitX * ((vertex.Uv.X - .5f) * .45f)
                + panelUp * ((vertex.Uv.Y - .5f) * .3f)),
        }).ToArray(), panel.Mesh.Indices);
        var normalMap = new ModelTextureBinding(new("reflection-normal-map", 2, 2,
            [166, 128, 245, 255, 100, 148, 250, 255, 117, 108, 251, 255, 145, 160, 249, 255]),
            new(false, false, TextureWrap.Clamp, TextureWrap.Clamp));
        panel = panel with
        {
            Mesh = smoothMesh,
            Material = panel.Material with
            {
                Emissive = Vector3.Zero,
                BaseColor = Vector4.One,
                BaseColorTexture = panel.Material.EmissiveTexture,
                EmissiveTexture = null,
                NormalTexture = normalMap,
            },
        };
        var blocker = new SceneInstance3D("reflection-shadow-blocker",
            ReflectionQuad("reflection-blocker", Vector3.UnitX * .36f, panelUp * .38f),
            new("reflection-black") { BaseColor = new(0, 0, 0, 1), Roughness = 1 },
            Matrix4x4.CreateTranslation(center + panelNormal * .5f + Vector3.UnitX * .58f));
        var geometry = new NativeInstanceSet3D([receiver, panel, blocker]);
        var scene = new Native3DScene([], []) { Instances = geometry };
        var point = new LocalLight3D(center + panelNormal * 1.45f + Vector3.UnitX * 1.45f, new(.3f, .8f, 1), 8, 8)
        {
            CastShadows = true,
        };
        Vector3 sunDirection = Vector3.Normalize(panelNormal + Vector3.UnitX * .95f);
        Vector3 sunColor = new(1, .5f, .3f);
        const float sunIntensity = 2.8f;
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        renderer.Environment = null;
        renderer.EnvironmentSource = null;
        renderer.EnableRayShadows = true;
        renderer.LocalLights = [point];
        renderer.Settings = renderer.Settings with { SunDirection = sunDirection, SunColor = sunColor, SunIntensity = sunIntensity, Shadows = true };
        renderer.Render(scene, clip, ReflectionEye, Clear, capture: true);
        var shaded = renderer.ReadReflectionsForQualification();
        SaveSamplingImage(plant, assets, output, "lit-shadowed", renderer.ReadLightingForQualification());
        renderer.LocalLights = [point with { CastShadows = false }];
        renderer.Settings = renderer.Settings with { Shadows = false };
        renderer.Render(scene, clip, ReflectionEye, Clear, capture: true);
        var unshadowed = renderer.ReadReflectionsForQualification();
        SaveSamplingImage(plant, assets, output, "lit-unshadowed", renderer.ReadLightingForQualification());

        Matrix4x4.Invert(clip, out var inverse);
        int samples = 0, sunBlocked = 0, localBlocked = 0;
        double shadedError = 0, controlError = 0;
        for (int index = 0; index < ReflectionWidth * ReflectionHeight; index++)
        {
            var ndc = new Vector3(2 * (index % ReflectionWidth + .5f) / ReflectionWidth - 1,
                2 * (index / ReflectionWidth + .5f) / ReflectionHeight - 1, 0);
            Vector3 start = Vector3.Transform(ndc, inverse);
            var primary = TraceReflectionOracle(geometry, start, -ReflectionView);
            if (primary.Identity < 0) continue;
            Require(primary.Identity == 0, "Secondary lighting specimen entered the camera.");
            Vector3 origin = start - ReflectionView * primary.Distance + Vector3.UnitY * .0005f;
            var hit = TraceReflectionOracle(geometry, origin, ReflectionDirection);
            if (hit.Identity != 1) continue;
            Vector3 position = origin + ReflectionDirection * hit.Distance;
            var surface = SecondaryOracle(panel, position, hit.Normal, true);
            Vector3 shadowOrigin = position + hit.Normal * .0005f;
            Vector3 segment = point.Position - shadowOrigin;
            bool pointVisible = TraceReflectionOracle(geometry, shadowOrigin, Vector3.Normalize(segment)).Distance >= segment.Length() - .0005f;
            bool sunVisible = TraceReflectionOracle(geometry, shadowOrigin, sunDirection).Identity < 0;
            if (!pointVisible) localBlocked++;
            if (!sunVisible) sunBlocked++;
            var sources = new List<LocalLight3D>();
            if (pointVisible) sources.Add(point);
            if (sunVisible) sources.Add(new(position + sunDirection, sunColor, sunIntensity, 1000));
            Vector3 expected = SecondaryPointResponse(surface, position, -ReflectionDirection, hit.Normal, sources.ToArray());
            Vector3 actual = ReadRgb(shaded.Radiance, index);
            Vector3 control = ReadRgb(unshadowed.Radiance, index);
            shadedError += Vector3.DistanceSquared(actual, expected);
            controlError += Vector3.DistanceSquared(control, expected);
            samples++;
        }
        double error = Math.Sqrt(shadedError / (samples * 3));
        double baseline = Math.Sqrt(controlError / (samples * 3));
        Console.WriteLine($"R5a secondary direct light: {samples} samples, {sunBlocked} sun and {localBlocked} point occlusions, HDR RMSE {error:F6} vs {baseline:F6}.");
        Require(samples > 500 && sunBlocked > 50 && localBlocked > 50 && error < .006 && error < baseline * .12,
            "Reflected smooth, normal-mapped lighting or ray-traced secondary visibility diverged from the CPU oracle.");
        return new { Samples = samples, BlockedSun = sunBlocked, BlockedPoint = localBlocked, Rmse = error, UnshadowedRmse = baseline };
    }

    private static Vector3 ReadRgb(float[] rgba, int pixel) => new(rgba[pixel * 4], rgba[pixel * 4 + 1], rgba[pixel * 4 + 2]);
}
