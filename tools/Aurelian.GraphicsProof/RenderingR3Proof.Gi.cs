using System.Numerics;
using System.Diagnostics;
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
    public static void RunGi(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new(EnableValidation: true,
            EnableRayQueries: true, ApplicationName: "R4 retained dynamic diffuse"));
        Require(initialized.Success, "GI initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("gi-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var camera = new ReferenceCamera3D(new(5, 3.5f, 7), new(0, 1, 0), Vector3.UnitY, .65f);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)Width / Height, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var grid = new DiffuseProbeGrid(new(-1.8f, .2f, -1.8f), new(1.8f, 2.6f, 1.8f), 5, 4, 5, 128);
        var options = new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 16 };
        var lights = new[] { new LocalLight3D(new(-1.3f, 2.3f, -.8f), Vector3.One, 35, 10) { CastShadows = true } };
        var scene = GiScene(box, sphere, null, false);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = lights;
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        renderer.DynamicDiffuse = options;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        try
        {
            renderer.CaptureCompiledDiffuseProbes();
            throw new InvalidOperationException("Incomplete directional coverage was admitted as a compile.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("AUR-GI-004")) { }
        renderer.DynamicDiffuse = options with { RaysPerProbePerFrame = grid.RaysPerProbe };
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        CompiledDiffuseProbeField3D field = renderer.CaptureCompiledDiffuseProbes();
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        string fieldPath = Path.Combine(output, "compiled-probes.json");
        File.WriteAllText(fieldPath, JsonSerializer.Serialize(field, jsonOptions));
        var restored = JsonSerializer.Deserialize<CompiledDiffuseProbeField3D>(File.ReadAllText(fieldPath), jsonOptions)!;
        restored.Validate();
        Require(restored.Grid == field.Grid && restored.Data.SequenceEqual(field.Data), "Compiled field round-trip changed data.");
        renderer.SetCompiledDiffuseProbes(restored);
        var rows = new List<object>();
        var staticReference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, lights,
            camera, samples: 2048, bounces: 2);
        SaveSamplingImage(plant, assets, output, "static-reference", staticReference);
        renderer.DynamicDiffuse = new();
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var direct = renderer.ReadLightingForQualification();
        SaveSamplingImage(plant, assets, output, "static-direct", direct);
        double directError = Rmse(direct.LinearRgb, staticReference.LinearRgb);
        foreach (var mode in new[] { DynamicDiffuseMode3D.Runtime, DynamicDiffuseMode3D.CompiledResidual, DynamicDiffuseMode3D.CompiledOnly })
        {
            renderer.DynamicDiffuse = options with { Mode = mode };
            for (int frame = 0; frame < 8; frame++)
            {
                renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
                var image = renderer.ReadLightingForQualification();
                double error = Rmse(image.LinearRgb, staticReference.LinearRgb);
                rows.Add(new { Workload = "static", Mode = mode.ToString(), Frame = frame, Rmse = error });
                if (frame == 0 || frame == 7) SaveSamplingImage(plant, assets, output, $"static-{mode}-{frame}", image);
                if (frame == 7)
                {
                    Console.WriteLine($"GI static {mode}: RMSE {error:F6}, direct {directError:F6}");
                    Require(error < directError, "GI did not improve the actual surface resolve against two-bounce reference.");
                }
            }
        }
        // Warmed runtime is a strong control: after all directions have been visited,
        // it should match the finite compiled basis, including under motion.
        var moved = GiScene(box, sphere, null, true);
        var movedReference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), moved, lights,
            camera, samples: 2048, bounces: 2);
        SaveSamplingImage(plant, assets, output, "moved-reference", movedReference);
        var ends = new Dictionary<DynamicDiffuseMode3D, float[]>();
        double negative = 0;
        foreach (var mode in new[] { DynamicDiffuseMode3D.CompiledOnly, DynamicDiffuseMode3D.CompiledResidual, DynamicDiffuseMode3D.Runtime })
        {
            renderer.DynamicDiffuse = options with { Mode = mode };
            for (int frame = 0; frame < 8; frame++) renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            var predecessor = (NativeInstanceSet3D)scene.Instances;
            for (int frame = 0; frame < 8; frame++)
            {
                moved = GiScene(box, sphere, predecessor, true);
                predecessor = (NativeInstanceSet3D)moved.Instances;
                renderer.Render(moved, matrix, camera.Eye, Clear, capture: true);
                var image = renderer.ReadLightingForQualification();
                double error = Rmse(image.LinearRgb, movedReference.LinearRgb);
                rows.Add(new { Workload = "moving-occluder", Mode = mode.ToString(), Frame = frame, Rmse = error });
                if (frame == 7)
                {
                    ends[mode] = image.LinearRgb;
                    SaveSamplingImage(plant, assets, output, $"moved-{mode}", image);
                    Console.WriteLine($"GI moved {mode}: RMSE {error:F6}");
                    if (mode == DynamicDiffuseMode3D.CompiledResidual)
                    {
                        float[] cache = renderer.ReadDiffuseCacheForQualification();
                        negative = cache.Take(grid.Count * grid.RaysPerProbe * 4).Where((_, index) => index % 4 != 3 && cache[index] < 0).Sum(value => -(double)value);
                        Require(negative > .01, "Moving occlusion did not produce signed negative corrections.");
                    }
                }
            }
        }
        double staleError = Rmse(ends[DynamicDiffuseMode3D.CompiledOnly], movedReference.LinearRgb);
        double correctedError = Rmse(ends[DynamicDiffuseMode3D.CompiledResidual], movedReference.LinearRgb);
        double warmedDifference = Rmse(ends[DynamicDiffuseMode3D.Runtime], ends[DynamicDiffuseMode3D.CompiledResidual]);
        Require(correctedError < staleError, "Signed correction did not improve stale compiled lighting.");
        Require(warmedDifference < .001, "Warm runtime and residual bases diverged.");
        // Additive relighting must also be represented without a new bake.
        renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledResidual };
        renderer.LocalLights = [lights[0] with { Intensity = lights[0].Intensity * 2 }];
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var positiveCache = renderer.ReadDiffuseCacheForQualification();
        double positive = positiveCache.Take(grid.Count * grid.RaysPerProbe * 4).Where((_, index) => index % 4 != 3 && positiveCache[index] > 0).Sum(value => (double)value);
        Require(positive > .01, "Relighting did not produce positive corrections.");
        var stats = renderer.RaySceneStatistics;
        object leakage = CheckGiLeakage(plant, assets, box, sphere, camera, matrix, lights, output);
        object emissive = CheckGiEmission(plant, assets, scene, camera, matrix, options, output);
        var timings = new List<object>();
        foreach (var size in new[] { (640, 360), (1280, 720) })
        {
            timings.Add(MeasureGi(plant, assets, scene, lights, camera, options, field, size.Item1, size.Item2, output));
        }
        validation.RequireClean();
        var pairs = new[]
        {
            new { Name = "identity", Reference = "static-reference.png", Test = "static-reference.png", Group = "identity" },
            new { Name = "static-direct", Reference = "static-reference.png", Test = "static-direct.png", Group = "direct-static" },
            new { Name = "static-gi", Reference = "static-reference.png", Test = "static-CompiledResidual-7.png", Group = "gi-static" },
            new { Name = "moved-compiled", Reference = "moved-reference.png", Test = "moved-CompiledOnly.png", Group = "compiled-moved" },
            new { Name = "moved-gi", Reference = "moved-reference.png", Test = "moved-CompiledResidual.png", Group = "gi-moved" },
            new { Name = "emissive-direct", Reference = "emissive-reference.png", Test = "emissive-direct.png", Group = "direct-emissive" },
            new { Name = "emissive-gi", Reference = "emissive-reference.png", Test = "emissive-gi.png", Group = "gi-emissive" },
        };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "dynamic-diffuse", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, jsonOptions));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, FullR4Complete = false, Device = plant.Facts.PhysicalDeviceName,
            Grid = grid, RaysPerProbePerFrame = 16, CompiledRoundTrip = true,
            DirectOnlyRmse = directError, StaleMovedRmse = staleError, CorrectedMovedRmse = correctedError,
            WarmRuntimeResidualRmse = warmedDifference, NegativeCorrectionEnergy = negative, PositiveCorrectionEnergy = positive,
            Acceleration = stats, Cases = rows, Leakage = leakage, Emission = emissive, Measurements = timings,
            Scope = "Prepared constant-material diffuse probes, local/emissive sources, one indirect bounce. No sun/environment, recursive transport, sparse allocation, automatic region policy or whole-Beacon qualification.",
        }, jsonOptions));
        Console.WriteLine("AURELIAN_R4_GI_PASSED " + output);
    }

    private static object CheckGiEmission(AurelianVulkanPlant plant, GameAssets assets, Native3DScene source,
        ReferenceCamera3D camera, Matrix4x4 matrix, DynamicDiffuseLighting3D options, string output)
    {
        var scene = source with { Instances = new NativeInstanceSet3D(source.Instances.Select(instance =>
            instance.Identity == "red-wall" ? instance with { Material = instance.Material with { Emissive = new(3, .1f, .05f) } } : instance)) };
        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, [], camera, samples: 2048, bounces: 2);
        SaveSamplingImage(plant, assets, output, "emissive-reference", reference);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = [];
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var direct = renderer.ReadLightingForQualification();
        SaveSamplingImage(plant, assets, output, "emissive-direct", direct);
        renderer.DynamicDiffuse = options with { RaysPerProbePerFrame = options.Grid!.RaysPerProbe };
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var actual = renderer.ReadLightingForQualification();
        float[] initial = renderer.ReadDiffuseOutputForQualification();
        double directError = Rmse(direct.LinearRgb, reference.LinearRgb);
        double error = Rmse(actual.LinearRgb, reference.LinearRgb);
        SaveSamplingImage(plant, assets, output, "emissive-gi", actual);
        Require(error < directError, "Emissive-only retained GI did not improve the two-bounce reference.");
        renderer.StaticEmissionIntensity = 2;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        float[] doubled = renderer.ReadDiffuseOutputForQualification();
        double discrepancy = 0;
        for (int index = 0; index < initial.Length; index++)
        {
            if (index % 4 != 3) discrepancy = Math.Max(discrepancy, Math.Abs(doubled[index] - initial[index] * 2));
        }
        Require(discrepancy < .00001, "Secondary emission ignored the renderer's emission intensity.");
        Console.WriteLine($"GI emissive: RMSE {directError:F6} -> {error:F6}; emission scale error {discrepancy:E3}");
        return new { DirectOnlyRmse = directError, GiRmse = error, MaximumEmissionScaleError = discrepancy, LocalLightCount = 0 };
    }

    private static object CheckGiLeakage(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box,
        NativeMesh3D sphere, ReferenceCamera3D camera, Matrix4x4 matrix, LocalLight3D[] lights, string output)
    {
        var source = GiScene(box, sphere, null, false);
        var divider = new SceneInstance3D("thin-divider", box, new("gi-divider")
        {
            BaseColor = new(.7f, .7f, .7f, 1), Roughness = 1, Metallic = 0, DoubleSided = true,
        }, Matrix4x4.CreateScale(.0125f, 1.5f, 2.2f) * Matrix4x4.CreateTranslation(.05f, 1.5f, 0));
        var scene = source with { Instances = new NativeInstanceSet3D(source.Instances.Append(divider)) };
        var grid = new DiffuseProbeGrid(new(-1.8f, .2f, -1.8f), new(1.8f, 2.6f, 1.8f), 4, 4, 5, 128);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = lights;
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        Matrix4x4.Invert(matrix, out var inverse);
        var energies = new Dictionary<bool, double>();
        var errors = new Dictionary<bool, double>();
        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, lights, camera, samples: 1024, bounces: 2);
        SaveSamplingImage(plant, assets, output, "thin-wall-reference", reference);
        int pixels = 0;
        int connected = 0;
        foreach (bool visibility in new[] { false, true })
        {
            renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 128, ConnectivityVisibility = visibility };
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            float[] gi = renderer.ReadDiffuseOutputForQualification();
            float[] motion = renderer.ReadLightMotionForQualification();
            var image = renderer.ReadLightingForQualification();
            double energy = 0;
            double squaredError = 0;
            pixels = 0;
            connected = 0;
            for (int pixel = 0; pixel < Width * Height; pixel++)
            {
                if (motion[pixel * 4 + 3] >= 1) continue;
                var clip = new Vector4((pixel % Width + .5f) / Width * 2 - 1,
                    (pixel / Width + .5f) / Height * 2 - 1, motion[pixel * 4 + 3], 1);
                Vector4 world = Vector4.Transform(clip, inverse);
                world /= world.W;
                if (world.X < .3f || world.X > 1.5f || MathF.Abs(world.Y) > .01f || MathF.Abs(world.Z) > 1.4f) continue;
                energy += gi[pixel * 4] + gi[pixel * 4 + 1] + gi[pixel * 4 + 2];
                for (int channel = 0; channel < 3; channel++)
                {
                    double difference = image.LinearRgb[pixel * 3 + channel] - reference.LinearRgb[pixel * 3 + channel];
                    squaredError += difference * difference;
                }
                pixels++;
                if (gi[pixel * 4 + 3] > 0) connected++;
            }
            Require(pixels > 20 && connected == pixels, $"Thin-wall control lost valid receiver coverage: {connected}/{pixels}, visibility={visibility}.");
            energies[visibility] = energy / pixels;
            errors[visibility] = Math.Sqrt(squaredError / (pixels * 3));
            SaveSamplingImage(plant, assets, output, visibility ? "thin-wall-connected" : "thin-wall-leaking", image);
        }
        Console.WriteLine($"GI thin wall: leakage {energies[false]:F6} -> {energies[true]:F6} ({pixels} covered floor pixels)");
        Require(energies[true] < energies[false] * .2, "Current connectivity did not suppress cross-wall irradiance.");
        Require(errors[true] < errors[false], "Connectivity did not improve the independent reference in the dark room.");
        return new { ThicknessMetres = .025, SelectedFloorPixels = pixels, ConnectedFloorPixels = connected,
            UnconnectedIrradiance = energies[false], ConnectedIrradiance = energies[true], UnconnectedRmse = errors[false], ConnectedRmse = errors[true] };
    }

    private static object MeasureGi(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene, LocalLight3D[] lights,
        ReferenceCamera3D camera, DynamicDiffuseLighting3D options, CompiledDiffuseProbeField3D field, int width, int height, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = Settings() with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        renderer.LocalLights = lights;
        renderer.SetCompiledDiffuseProbes(field);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var rows = new List<object>();
        foreach (var mode in new[] { DynamicDiffuseMode3D.Off, DynamicDiffuseMode3D.Runtime, DynamicDiffuseMode3D.CompiledResidual, DynamicDiffuseMode3D.CompiledOnly })
        {
            renderer.DynamicDiffuse = options with { Mode = mode };
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
            double fps = 64 / Stopwatch.GetElapsedTime(started).TotalSeconds;
            rows.Add(new { Mode = mode.ToString(), MedianCpuMilliseconds = Median(cpu), MedianGpuMilliseconds = Median(gpu), FramesPerSecond = fps });
            Console.WriteLine($"GI {width}x{height} {mode}: CPU {Median(cpu):F3}ms GPU {Median(gpu):F3}ms {fps:F1}fps");
            var captured = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, $"gi-{width}x{height}-{mode}.png"), width, height, captured.Pixels!);
        }
        return new { Width = width, Height = height, Variants = rows };
    }

    private static Native3DScene GiScene(NativeMesh3D box, NativeMesh3D sphere, NativeInstanceSet3D? previous, bool occluded)
    {
        var white = new ModelMaterial("gi-white") { BaseColor = new(.7f, .7f, .7f, 1), Metallic = 0, Roughness = 1, DoubleSided = true };
        var red = white with { Slot = "gi-red", BaseColor = new(.85f, .04f, .02f, 1) };
        var items = new List<SceneInstance3D>();
        void Add(string id, NativeMesh3D mesh, Vector3 center, Vector3 size, ModelMaterial material)
        {
            Vector3 scale = size;
            if (mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles) scale *= .5f;
            items.Add(new(id, mesh, material, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center)));
        }
        Add("floor", box, new(0, -.1f, 0), new(4.4f, .2f, 4.4f), white);
        Add("red-wall", box, new(-2.1f, 1.5f, 0), new(.2f, 3, 4.4f), red);
        Add("back", box, new(0, 1.5f, -2.1f), new(4.4f, 3, .2f), white);
        Add("sphere", sphere, new(.65f, .55f, .2f), new(.55f, .55f, .55f), white);
        Add("moving", box, occluded ? new(-.8f, 1.3f, -.5f) : new(5, 1.3f, -.5f), new(.2f, 2.6f, 2.8f), white);
        return new([], []) { Instances = new NativeInstanceSet3D(items, previous) };
    }
}
