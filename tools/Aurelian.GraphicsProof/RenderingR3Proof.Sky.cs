using System.Numerics;
using System.Text.Json;
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
    public static void RunSky(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R4 sun and environment transport"));
        Require(initialized.Success, "Sky GI initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("sky-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var camera = new ReferenceCamera3D(new(5, 3.5f, 7), new(0, 1, 0), Vector3.UnitY, .65f);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)Width / Height, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var grid = new DiffuseProbeGrid(new(-1.8f, .2f, -1.8f), new(1.8f, 2.6f, 1.8f), 5, 4, 5, 128);
        var options = new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 128 };
        var rows = new List<object>();
        var pairs = new List<object>();
        foreach (string name in new[] { "sun", "environment", "combined" })
        {
            NativeMesh3D center = name == "environment" ? sphere : box;
            var scene = GiScene(box, center, null, false);
            var moved = GiScene(box, center, null, true);
            var settings = Settings() with
            {
                Shadows = name != "environment", SunDirection = Vector3.Normalize(new(1, .65f, .3f)),
                SunColor = new(1, .85f, .65f), SunIntensity = name == "environment" ? 0 : 5,
                EnvironmentIntensity = 1, ShadowRadius = 6, ShadowDistance = 20,
                ShadowWorldBias = .001f, ShadowBias = .0001f,
            };
            EnvironmentRadiance3D? source = name == "sun" ? null : SkySource(false);
            EnvironmentLighting? atlas = source is null ? null
                : VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
            var lighting = ReferenceLighting3D.FromSettings(settings, source);
            var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, [], camera,
                samples: 2048, bounces: 2, lighting: lighting);
            SaveSamplingImage(plant, assets, output, name + "-path-reference", reference);
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
            renderer.Settings = settings;
            renderer.LocalLights = [];
            renderer.Environment = atlas;
            renderer.EnvironmentSource = source;
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var off = renderer.ReadLightingForQualification();
            SaveSamplingImage(plant, assets, output, name + "-off", off);
            renderer.DynamicDiffuse = options with { SecondarySkyLighting = false };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var visibilityOnly = renderer.ReadLightingForQualification();
            SaveSamplingImage(plant, assets, output, name + "-visibility-only", visibilityOnly);
            renderer.DynamicDiffuse = options;
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var actual = renderer.ReadLightingForQualification();
            float[] coverage = renderer.ReadLightMotionForQualification();
            SaveSamplingImage(plant, assets, output, name + "-reference", PresentSkyReference(reference, coverage));
            SaveSamplingImage(plant, assets, output, name + "-gi", actual);
            double control = SkySurfaceError(off, reference, coverage);
            double error = SkySurfaceError(actual, reference, coverage);
            double visibilityError = SkySurfaceError(visibilityOnly, reference, coverage);
            Console.WriteLine($"Sky {name}: HDR RMSE {control:F6} -> {error:F6}");
            Console.WriteLine($"Sky {name}: visibility-only {visibilityError:F6}");
            Require(error < visibilityError, "Secondary sky illumination did not improve visibility-only GI.");
            Require(error < control, "Sky GI did not improve the real surface output against the independent path reference.");
            rows.Add(new { Case = name, OffRmse = control, VisibilityOnlyRmse = visibilityError, GiRmse = error,
                OffFullFrameRmse = Rmse(off.LinearRgb, reference.LinearRgb), GiFullFrameRmse = Rmse(actual.LinearRgb, reference.LinearRgb) });
            pairs.Add(new { Name = name + "-gi", Reference = name + "-reference.png", Test = name + "-gi.png", Group = name + "-gi" });
            pairs.Add(new { Name = name + "-visibility-only", Reference = name + "-reference.png", Test = name + "-visibility-only.png", Group = name + "-visibility-only" });
            pairs.Add(new { Name = name + "-off", Reference = name + "-reference.png", Test = name + "-off.png", Group = name + "-off" });
            var field = renderer.CaptureCompiledDiffuseProbes();
            renderer.SetCompiledDiffuseProbes(field);
            var movedReference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), moved, [], camera,
                samples: 2048, bounces: 2, lighting: lighting);
            SaveSamplingImage(plant, assets, output, name + "-moved-path-reference", movedReference);
            renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledOnly };
            moved = GiScene(box, center, (NativeInstanceSet3D)scene.Instances, true);
            renderer.Render(moved, matrix, camera.Eye, Clear, capture: true);
            var stale = renderer.ReadLightingForQualification();
            coverage = renderer.ReadLightMotionForQualification();
            SaveSamplingImage(plant, assets, output, name + "-moved-reference", PresentSkyReference(movedReference, coverage));
            SaveSamplingImage(plant, assets, output, name + "-moved-stale", stale);
            renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledResidual, RaysPerProbePerFrame = 16 };
            for (int frame = 0; frame < 8; frame++)
            {
                moved = GiScene(box, center, (NativeInstanceSet3D)moved.Instances, true);
                renderer.Render(moved, matrix, camera.Eye, Clear, capture: true);
            }
            var corrected = renderer.ReadLightingForQualification();
            SaveSamplingImage(plant, assets, output, name + "-moved-gi", corrected);
            double staleError = SkySurfaceError(stale, movedReference, coverage);
            double correctedError = SkySurfaceError(corrected, movedReference, coverage);
            float[] cache = renderer.ReadDiffuseCacheForQualification();
            double negative = cache.Take(grid.Count * grid.RaysPerProbe * 4)
                .Where((value, index) => index % 4 != 3 && value < 0).Sum(value => -(double)value);
            Console.WriteLine($"Sky {name} moved: {staleError:F6} -> {correctedError:F6}, negative correction {negative:F4}");
            Require(correctedError < staleError && negative > .01, "Sky occlusion failed to remove stale compiled bounce light.");
            rows.Add(new { Case = name + "-moving", StaleRmse = staleError, GiRmse = correctedError, NegativeCorrection = negative });
            pairs.Add(new { Name = name + "-moved-gi", Reference = name + "-moved-reference.png", Test = name + "-moved-gi.png", Group = name + "-moved-gi" });
            pairs.Add(new { Name = name + "-moved-stale", Reference = name + "-moved-reference.png", Test = name + "-moved-stale.png", Group = name + "-moved-stale" });
        }
        object freshness = CheckSkyFreshness(plant, assets, GiScene(box, box, null, false), camera, matrix, grid);
        object energy = CheckSkyEnergy(plant, assets, output);
        object importance = CheckSkyImportance(plant, assets, sceneBox: box, center: sphere, camera, matrix, grid, output, pairs);
        object performance = MeasureSkyTransport(plant, assets, GiScene(box, box, null, false), camera, grid, output);
        pairs.Add(new { Name = "identity", Reference = "sun-reference.png", Test = "sun-reference.png", Group = "identity" });
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "sky-diffuse", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        { Accepted = true, FullR4Complete = false, Device = plant.Facts.PhysicalDeviceName, Cases = rows, Energy = energy, Freshness = freshness, DefaultPromoted = false, Importance = importance, Performance = performance,
            Comparison = "Surface-only HDR RMSE; path-reference backgrounds retained separately and matched to authored black clear for display FLIP.",
            Scope = "Opaque single-indirect-bounce sun/environment transport with visibility-aware diffuse sky and existing split-sum primary specular." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R4_SKY_PASSED " + output);
    }

    private static EnvironmentRadiance3D SkySource(bool concentrated)
    {
        const int width = 64;
        const int height = 32;
        float[] values = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 color = y < 16 ? new(.5f, .7f, 1) : new(.05f, .04f, .03f);
                if (concentrated)
                {
                    color = new(.005f, .005f, .005f);
                    if (x >= 30 && x < 34 && y >= 5 && y < 9) color = new(35, 25, 15);
                }
                int at = (y * width + x) * 4;
                values[at] = color.X;
                values[at + 1] = color.Y;
                values[at + 2] = color.Z;
                values[at + 3] = 1;
            }
        }
        return new(width, height, values);
    }
}
