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
    private static double SkySurfaceError(ReferenceImage3D actual, ReferenceImage3D reference, float[] motion)
    {
        double squared = 0;
        int count = 0;
        for (int pixel = 0; pixel < actual.Width * actual.Height; pixel++)
        {
            if (motion[pixel * 4 + 3] >= 1) continue;
            for (int channel = 0; channel < 3; channel++)
            {
                double difference = actual.LinearRgb[pixel * 3 + channel] - reference.LinearRgb[pixel * 3 + channel];
                squared += difference * difference;
                count++;
            }
        }
        Require(count > 300, "Sky qualification lost surface coverage.");
        return Math.Sqrt(squared / count);
    }

    private static ReferenceImage3D PresentSkyReference(ReferenceImage3D reference, float[] motion)
    {
        float[] values = (float[])reference.LinearRgb.Clone();
        for (int pixel = 0; pixel < reference.Width * reference.Height; pixel++)
        {
            if (motion[pixel * 4 + 3] < 1) continue;
            values[pixel * 3] = 0;
            values[pixel * 3 + 1] = 0;
            values[pixel * 3 + 2] = 0;
        }
        return reference with { LinearRgb = values };
    }

    private static object CheckSkyEnergy(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int size = 32;
        var source = new EnvironmentRadiance3D(1, 1, [.3f, .5f, .8f, 1]);
        var atlas = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        var settings = Settings() with { EnvironmentIntensity = 1 };
        var camera = new ReferenceCamera3D(new(0, 2, 0), Vector3.Zero, -Vector3.UnitZ, .6f);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.6f, 1, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var material = new ModelMaterial("sky-plane") { BaseColor = new(.5f, .6f, .7f, 1), Roughness = 1, Metallic = 0, DoubleSided = true };
        var floor = new SceneInstance3D("floor", Box(), material,
            Matrix4x4.CreateScale(20, .05f, 20) * Matrix4x4.CreateTranslation(0, -.05f, 0));
        var scene = new Native3DScene([], []) { Instances = new NativeInstanceSet3D([floor]) };
        var grid = new DiffuseProbeGrid(new(-.7f, .2f, -.7f), new(.7f, 1.6f, .7f), 3, 3, 3, 128);
        var options = new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 128 };
        using var target = new VulkanNativeFrameTarget(plant, size, size);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = [];
        renderer.Settings = settings;
        renderer.Environment = atlas;
        renderer.EnvironmentSource = source;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var off = renderer.ReadLightingForQualification();
        renderer.DynamicDiffuse = options;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var full = renderer.ReadLightingForQualification();
        float[] irradiance = renderer.ReadDiffuseOutputForQualification();
        renderer.DynamicDiffuse = options with { Intensity = 0 };
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var specular = renderer.ReadLightingForQualification();
        double compositionError = 0;
        double minimumSkyRatio = double.PositiveInfinity;
        double maximumSkyRatio = 0;
        float[] albedo = [.5f, .6f, .7f];
        for (int pixel = 0; pixel < size * size; pixel++)
        {
            Require(irradiance[pixel * 4 + 3] > 0, "Open plane lost its probe connection.");
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = irradiance[pixel * 4 + channel] * albedo[channel] * .96 + specular.LinearRgb[pixel * 3 + channel];
                compositionError = Math.Max(compositionError, Math.Abs(expected - full.LinearRgb[pixel * 3 + channel]));
                double ratio = irradiance[pixel * 4 + channel] / source.Rgba[channel];
                minimumSkyRatio = Math.Min(minimumSkyRatio, ratio);
                maximumSkyRatio = Math.Max(maximumSkyRatio, ratio);
            }
        }
        double ratioEnergy = full.LinearRgb.Sum(value => (double)value) / off.LinearRgb.Sum(value => (double)value);
        Console.WriteLine($"Open sky: composition {compositionError:E3}, field ratio {minimumSkyRatio:F4}..{maximumSkyRatio:F4}, full/off energy {ratioEnergy:F4}");
        Require(compositionError < .001 && minimumSkyRatio > .9 && maximumSkyRatio < 1.1 && ratioEnergy > .8 && ratioEnergy < 1.15,
            "Environment diffuse was omitted, duplicated, or composed incorrectly.");
        SaveSamplingImage(plant, assets, output, "open-sky-full", full);
        SaveSamplingImage(plant, assets, output, "open-sky-specular", specular);
        renderer.DynamicDiffuse = options with
        {
            Grid = grid with { Minimum = grid.Minimum + new Vector3(10, 0, 0), Maximum = grid.Maximum + new Vector3(10, 0, 0) },
        };
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var uncovered = renderer.ReadLightingForQualification();
        double uncoveredError = Rmse(off.LinearRgb, uncovered.LinearRgb);
        Require(uncoveredError < .00001, "Missing GI coverage removed the existing environment diffuse fallback.");
        // A metallic receiver must retain precisely the existing specular environment.
        scene = scene with { Instances = new NativeInstanceSet3D([floor with { Material = material with { Metallic = 1 } }]) };
        renderer.DynamicDiffuse = new();
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var metalOff = renderer.ReadLightingForQualification();
        renderer.DynamicDiffuse = options;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var metalOn = renderer.ReadLightingForQualification();
        double metalError = Rmse(metalOff.LinearRgb, metalOn.LinearRgb);
        Require(metalError < .00001, "GI changed the existing metallic environment response.");
        renderer.EnvironmentSource = null;
        try
        {
            renderer.Render(scene, matrix, camera.Eye, Clear);
            throw new InvalidOperationException("Missing source radiance was admitted.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("AUR-GI-007", StringComparison.Ordinal)) { }
        renderer.EnvironmentSource = new(1, 1, [1, 1, 1, 1]);
        try
        {
            renderer.Render(scene, matrix, camera.Eye, Clear);
            throw new InvalidOperationException("Mismatched source radiance was admitted.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("AUR-GI-007", StringComparison.Ordinal)) { }
        return new { MaximumCompositionError = compositionError, MinimumSkyRatio = minimumSkyRatio, MaximumSkyRatio = maximumSkyRatio,
            FullToUnoccludedEnergyRatio = ratioEnergy, MetallicEnvironmentRmse = metalError, UncoveredEnvironmentRmse = uncoveredError, MissingAndMismatchedSourcesRejected = true };
    }

    private static object CheckSkyFreshness(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        ReferenceCamera3D camera, Matrix4x4 matrix, DiffuseProbeGrid grid)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = [];
        renderer.Settings = Settings() with { SunIntensity = 2, EnvironmentIntensity = 1 };
        renderer.EnvironmentSource = new(1, 1, [.2f, .3f, .4f, 1]);
        renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 16 };
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, matrix, camera.Eye, Clear);
        renderer.CaptureCompiledDiffuseProbes();
        renderer.Settings = renderer.Settings with { SunIntensity = 4 };
        renderer.Render(scene, matrix, camera.Eye, Clear);
        RequireIncompleteSkyCapture(renderer);
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, matrix, camera.Eye, Clear);
        renderer.CaptureCompiledDiffuseProbes();
        // Same dimensions and constant CDF: source identity must invalidate coverage too.
        renderer.EnvironmentSource = new(1, 1, [.6f, .2f, .1f, 1]);
        renderer.Render(scene, matrix, camera.Eye, Clear);
        RequireIncompleteSkyCapture(renderer);
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, matrix, camera.Eye, Clear);
        renderer.CaptureCompiledDiffuseProbes();
        return new { SunChangeInvalidatesCoverage = true, SameShapeSourceChangeInvalidatesCoverage = true,
            CaptureSucceedsAfterCompleteRefresh = true, RawSourceWithoutAtlas = true };
    }

    private static void RequireIncompleteSkyCapture(VulkanSolid3DRenderer renderer)
    {
        try
        {
            renderer.CaptureCompiledDiffuseProbes();
            throw new InvalidOperationException("Changed sky was admitted before full directional refresh.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("AUR-GI-004", StringComparison.Ordinal)) { }
    }

    private static object CheckSkyImportance(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D sceneBox,
        NativeMesh3D center, ReferenceCamera3D camera, Matrix4x4 matrix, DiffuseProbeGrid grid, string output, List<object> pairs)
    {
        var scene = GiScene(sceneBox, center, null, false);
        var source = SkySource(true);
        var atlas = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        var settings = Settings() with { EnvironmentIntensity = 1 };
        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, [], camera,
            samples: 4096, bounces: 2, lighting: ReferenceLighting3D.FromSettings(settings, source));
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = [];
        renderer.Settings = settings;
        renderer.Environment = atlas;
        renderer.EnvironmentSource = source;
        var errors = new Dictionary<bool, double>();
        foreach (bool importance in new[] { false, true })
        {
            renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 128, EnvironmentImportanceSampling = importance };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var actual = renderer.ReadLightingForQualification();
            float[] motion = renderer.ReadLightMotionForQualification();
            errors[importance] = SkySurfaceError(actual, reference, motion);
            string name = importance ? "hdr-importance" : "hdr-uniform";
            SaveSamplingImage(plant, assets, output, name, actual);
            SaveSamplingImage(plant, assets, output, "hdr-reference", PresentSkyReference(reference, motion));
            pairs.Add(new { Name = name, Reference = "hdr-reference.png", Test = name + ".png", Group = name });
        }
        Console.WriteLine($"HDR environment: uniform {errors[false]:F6}, importance {errors[true]:F6}");
        Require(errors[true] < errors[false], "Environment importance did not improve the equal-budget secondary estimator.");
        return new { UniformRmse = errors[false], ImportanceRmse = errors[true], ReferenceSamples = 4096,
            MixtureUniformFraction = .05, DistributionWidth = source.ImportanceWidth, DistributionHeight = source.ImportanceHeight };
    }
}
