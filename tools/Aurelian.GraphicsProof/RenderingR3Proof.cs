using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const int Width = 96;
    private const int Height = 64;
    private static readonly NativeFrameClearColor Clear = new(0, 0, 0, 1);
    private static readonly ReferenceCamera3D Camera = new(new(5, 5, 8), new(0, .4f, 0), Vector3.UnitY, .85f);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R3 exact local ray shadows"));
        Require(initialized.Success, "R3 plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        byte[] referenceProgram = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        NativeMesh3D box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("r3-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var lights = Lights();
        var settings = Settings();
        var cases = new List<object>();
        using (var target = new VulkanNativeFrameTarget(plant, Width, Height))
        using (var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true))
        {
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Settings = settings;
            renderer.LocalLights = lights;
            byte[]? previous = null;
            for (int pose = 0; pose < 3; pose++)
            {
                Native3DScene scene = Scene(box, sphere, pose);
                var reference = Reference(plant, referenceProgram, scene, lights);
                RenderingReferenceProof.WriteImage(output, $"hall-{pose}-reference", reference);
                byte[] expected = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), reference);
                NativeGameGraphics.WritePng(Path.Combine(output, $"hall-{pose}-reference.png"), Width, Height, expected);
                var frame = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
                byte[] actual = frame.Pixels!;
                NativeGameGraphics.WritePng(Path.Combine(output, $"hall-{pose}-ray.png"), Width, Height, actual);
                Require(renderer.CurrentFramePlan!.Graph.Passes.Any(pass => pass.Name == "local-ray-shadows"), "Ray shadows bypassed the compiled frame plan.");
                var stats = renderer.RaySceneStatistics!;
                Require(stats.MeshBuilds == 2 && stats.TopLevelBuilds == pose + 1, "Retained BLAS reuse or TLAS replacement failed.");
                double error = Error(actual, expected);
                renderer.Settings = settings with { LocalLightCulling = false };
                byte[] uncull = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
                double tileError = Error(actual, uncull);
                Require(tileError < .0001, "Light tile culling rejected a contributing light: " + tileError);
                renderer.Settings = settings;
                renderer.LocalLights = lights.Select(light => light with { CastShadows = false }).ToArray();
                byte[] noShadow = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
                NativeGameGraphics.WritePng(Path.Combine(output, $"hall-{pose}-unshadowed.png"), Width, Height, noShadow);
                double controlError = Error(noShadow, expected);
                Console.WriteLine($"R3 Hall pose {pose}: reference MAE {error:F6}, unshadowed {controlError:F6}, tile {tileError:F8}");
                Require(error < .015 && error < controlError * .5, "Ray shadows did not improve the real rendering path against reference.");
                if (previous is not null) Require(!actual.SequenceEqual(previous), "Moving occluder did not change visibility.");
                previous = actual;
                renderer.LocalLights = lights;
                cases.Add(new { Pose = pose, DisplayRgbMae = error, UnshadowedDisplayRgbMae = controlError,
                    TileDisplayRgbMae = tileError, Acceleration = stats, frame.GpuPassTimes });
            }
            // A steady immutable snapshot must not build or walk scene instances again.
            Native3DScene steady = Scene(box, sphere, 0);
            for (int warmup = 0; warmup < 4; warmup++)
                renderer.Render(steady, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            var before = renderer.RaySceneStatistics;
            var stable = renderer.Render(steady, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            Require(before == renderer.RaySceneStatistics && stable.Retained!.CpuVisitedInstances == 0,
                "Steady ray scene admission repeated instance work.");
            CheckBoundaries(renderer, steady);
        }
        object masks = CheckMaskBoundaries(plant, assets, referenceProgram, box, sphere, output);
        var timings = new List<object>();
        foreach (var size in new[] { (Width: 640, Height: 360), (Width: 1280, Height: 720) })
            timings.Add(Measure(plant, assets, box, sphere, lights, size.Width, size.Height, output));
        var pairs = new List<object>();
        pairs.Add(new { Name = "identity", Reference = "hall-0-reference.png", Test = "hall-0-reference.png", Group = "identity" });
        for (int pose = 0; pose < 3; pose++)
        {
            foreach (string variant in new[] { "ray", "unshadowed" })
                pairs.Add(new { Name = $"hall-{pose}-{variant}", Reference = $"hall-{pose}-reference.png",
                    Test = $"hall-{pose}-{variant}.png", Group = $"hall-{pose}-{variant}" });
        }
        pairs.Add(new { Name = "point-shadow", Reference = "point-255-reference.png", Test = "point-255-shadow.png", Group = "point-ray" });
        pairs.Add(new { Name = "point-unshadowed", Reference = "point-255-reference.png", Test = "point-excluded.png", Group = "point-unshadowed" });
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "local-ray-shadows", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, new JsonSerializerOptions { WriteIndented = true }));
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, FullR3Complete = false, Device = plant.Facts.PhysicalDeviceName,
            ExactShadowedLights = lights.Length, PointLights = lights.Count(light => light.Kind == LocalLightKind.Point),
            SpotLights = lights.Count(light => light.Kind == LocalLightKind.Spot), Cases = cases, MaskBoundaries = masks, Measurements = timings,
            Scope = "Opaque retained LOD0 geometry; exact point/spot local shadows. No stochastic selection, area sources, denoising, skinning, MASK or acceleration compaction.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R3_EXACT_PASSED " + output);
    }

    private static object Measure(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box,
        NativeMesh3D sphere, LocalLight3D[] lights, int width, int height, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableRayShadows = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings();
        renderer.LocalLights = lights;
        Native3DScene scene = Scene(box, sphere, 0);
        var gpu = new List<double>();
        var cpu = new List<double>();

        long started = 0;
        double admissionMilliseconds = 0;
        for (int frame = 0; frame < 88; frame++)
        {
            if (frame == 24) started = Stopwatch.GetTimestamp();
            long frameStarted = Stopwatch.GetTimestamp();
            var result = renderer.Render(scene, Matrix(width, height), Camera.Eye, Clear);
            if (frame == 0) admissionMilliseconds = Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
            if (frame < 24) continue;
            Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 }, "Measured frame reuploaded instances.");
            cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
            if (result.CompletedGpuFrame is { } complete)
                gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);

        }
        renderer.WaitForPendingFrames();
        double fps = 64 / Stopwatch.GetElapsedTime(started).TotalSeconds;
        var capture = renderer.Render(scene, Matrix(width, height), Camera.Eye, Clear, capture: true);
        NativeGameGraphics.WritePng(Path.Combine(output, $"hall-{width}x{height}.png"), width, height, capture.Pixels!);
        double shadowMilliseconds = capture.GpuPassTimes.Single(time => time.Pass == "local-shadows").Milliseconds;
        double medianGpu = Median(gpu);
        Console.WriteLine($"R3 256 shadowed lights {width}x{height}: GPU {medianGpu:F3} ms, CPU {Median(cpu):F3} ms, {fps:F1} fps");
        if (width == 640) Require(medianGpu < 16.67, "Declared 640x360 exact baseline exceeded 16.67 ms GPU budget.");
        return new { Width = width, Height = height, Frames = 64, WarmupFrames = 24, MedianGpuMilliseconds = medianGpu,
            MedianCpuPrepareSubmitMilliseconds = Median(cpu), CapturedLocalShadowMilliseconds = shadowMilliseconds, FramesPerSecond = fps,
            FirstFrameAdmissionWallMilliseconds = admissionMilliseconds,
            BudgetMilliseconds = 16.67, WithinBudget = medianGpu < 16.67, Acceleration = renderer.RaySceneStatistics };
    }

    private static object CheckMaskBoundaries(AurelianVulkanPlant plant, GameAssets assets, byte[] program,
        NativeMesh3D box, NativeMesh3D sphere, string output)
    {
        const int size = 48;
        var camera = new ReferenceCamera3D(new(0, .01f, 0), new(0, -1, 0), -Vector3.UnitZ, 1, 2);
        var projection = Matrix4x4.CreateOrthographic(2, 2, .001f, 100);
        projection.M22 *= -1;
        Matrix4x4 matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        var material = new ModelMaterial("r3-receiver") { Metallic = 0, Roughness = .65f, BaseColor = new(.7f, .5f, .3f, 1), DoubleSided = true };
        var floor = new SceneInstance3D("floor", box, material, Matrix4x4.CreateScale(2, .01f, 2) * Matrix4x4.CreateTranslation(0, -.01f, 0));
        var blocker = new SceneInstance3D("off-camera-blocker", sphere, material,
            Matrix4x4.CreateScale(.28f, .4f, .28f) * Matrix4x4.CreateTranslation(0, 1, 0));
        Native3DScene Scene(SceneInstance3D item) => new([], []) { Instances = new NativeInstanceSet3D([floor, item]) };
        var light = new LocalLight3D(new(0, 2, 0), Vector3.One, 5, 5) { CastShadows = true };
        var specimen = Scene(blocker);
        var reference = Reference(plant, program, specimen, [light], camera, size, size);
        byte[] expected = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), reference);
        using var target = new VulkanNativeFrameTarget(plant, size, size);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableRayShadows = true;
        renderer.EnableVisibilityBuffer = true;
        renderer.Settings = Settings();
        var rows = new List<object>();
        foreach (int selected in new[] { 0, 15, 16, 31, 32, 63, 64, 127, 128, 191, 192, 255 })
        {
            var table = Enumerable.Range(0, selected + 1).Select(_ => light with { Intensity = 0, CastShadows = false }).ToArray();
            table[selected] = light;
            renderer.LocalLights = table;
            byte[] actual = renderer.Render(specimen, matrix, camera.Eye, Clear, capture: true).Pixels!;
            double error = Error(actual, expected);
            NativeGameGraphics.WritePng(Path.Combine(output, $"point-{selected}-shadow.png"), size, size, actual);
            NativeGameGraphics.WritePng(Path.Combine(output, "point-255-reference.png"), size, size, expected);
            Require(error < .001, $"Shadow mask boundary {selected} disagrees with one-light reference: {error}");
            rows.Add(new { Light = selected, DisplayRgbMae = error });
            if (selected == 255) NativeGameGraphics.WritePng(Path.Combine(output, "point-255-shadow.png"), size, size, actual);
        }
        byte[] excludedLayer = renderer.Render(Scene(blocker with { RayLayer = 0 }), matrix, camera.Eye, Clear, capture: true).Pixels!;
        byte[] excludedMask = renderer.Render(Scene(blocker with { RayMask = 0 }), matrix, camera.Eye, Clear, capture: true).Pixels!;
        Require(excludedLayer.SequenceEqual(excludedMask), "Ray instance layer and mask exclusions disagree.");
        renderer.LocalLights = [light with { CastShadows = false }];
        byte[] unshadowed = renderer.Render(specimen, matrix, camera.Eye, Clear, capture: true).Pixels!;
        Require(unshadowed.SequenceEqual(excludedLayer), "Excluded off-camera blocker still occludes the point light.");
        Require(Error(unshadowed, expected) > .02, "Off-camera analytic blocker did not cast a substantial point shadow.");
        NativeGameGraphics.WritePng(Path.Combine(output, "point-255-reference.png"), size, size, expected);
        NativeGameGraphics.WritePng(Path.Combine(output, "point-excluded.png"), size, size, excludedLayer);
        renderer.LocalLights = [light];
        var floorOnly = specimen with { Instances = new NativeInstanceSet3D([floor]) };
        byte[] removed = renderer.Render(floorOnly, matrix, camera.Eye, Clear, capture: true).Pixels!;
        Require(renderer.RaySceneStatistics!.ResidentMeshes == 1 && removed.SequenceEqual(excludedLayer),
            "Removing an instance retained its BLAS or shadow.");
        byte[] restored = renderer.Render(specimen, matrix, camera.Eye, Clear, capture: true).Pixels!;
        Require(renderer.RaySceneStatistics is { MeshBuilds: 3, ResidentMeshes: 2 } && Error(restored, expected) < .001,
            "Re-admitting an evicted mesh did not restore the same point shadow.");
        return new { Boundaries = rows, LayerMaskExclusionsByteIdentical = true, MeshEvictionAndReadmission = true, OffCameraShadowDisplayRgbMae = Error(unshadowed, expected) };
    }

    private static void CheckBoundaries(VulkanSolid3DRenderer renderer, Native3DScene scene)
    {
        var lights = renderer.LocalLights;
        renderer.EnableRayShadows = false;
        try
        {
            renderer.Render(scene with { Instances = new NativeInstanceSet3D(scene.Instances.Where(instance => instance.Mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles)) }, Matrix(Width, Height), Camera.Eye, Clear);
            throw new InvalidOperationException("Point shadow map fallback was incorrectly admitted.");
        }
        catch (NotSupportedException error) { Require(error.Message.Contains("AUR-LIGHT-LOCAL-002"), error.Message); }
        renderer.EnableRayShadows = true;
        renderer.LocalLights = lights.Take(1).ToArray();
        var masked = new NativeInstanceSet3D(scene.Instances.Select(instance => instance with { Material = instance.Material with { AlphaMask = true } })
            .Where(instance => instance.Mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles));
        try
        {
            renderer.Render(scene with { Instances = masked }, Matrix(Width, Height), Camera.Eye, Clear);
            throw new InvalidOperationException("Opaque ray profile silently accepted MASK.");
        }
        catch (NotSupportedException error) { Require(error.Message.Contains("AUR-RAY-SCENE-002"), error.Message); }
        renderer.LocalLights = lights;
    }

    private static ReferenceImage3D Reference(AurelianVulkanPlant plant, byte[] program, Native3DScene scene, LocalLight3D[] lights,
        ReferenceCamera3D? camera = null, int width = Width, int height = Height)
    {
        var triangles = new List<ReferenceTriangle3D>();
        var analytic = new List<ReferenceAnalytic3D>();
        foreach (var instance in scene.Instances)
        {
            var material = new ReferenceMaterial3D(new(instance.Material.BaseColor.X, instance.Material.BaseColor.Y, instance.Material.BaseColor.Z),
                instance.Material.Roughness, instance.Material.Metallic);
            if (instance.Mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
            {
                analytic.Add(new(instance.Mesh.PrimitiveKind, instance.Transform, material));
                continue;
            }
            for (int index = 0; index < instance.Mesh.Indices.Length; index += 3)
            {
                Vector3 Vertex(int lane) => Vector3.Transform(instance.Mesh.Vertices[(int)instance.Mesh.Indices[index + lane]].Position, instance.Transform);
                triangles.Add(new(Vertex(0), Vertex(1), Vertex(2), material));
            }
        }
        using var reference = new ReferenceIntegrator3D(plant, program, new(width, height, 1, 761, PixelJitter: false),
            camera ?? Camera, triangles, [], analytic: analytic, lighting: new(LocalLights: lights));
        reference.Accumulate(1);
        return reference.Capture();
    }

    private static Native3DScene Scene(NativeMesh3D box, NativeMesh3D sphere, int pose)
    {
        var material = new ModelMaterial("r3-stone") { BaseColor = new(.65f, .5f, .3f, 1), Roughness = .65f, Metallic = 0, DoubleSided = true };
        var items = new List<SceneInstance3D>();
        Add("floor", box, new(4.5f, .05f, 4.5f), new(0, -.05f, 0));
        for (int index = 0; index < 6; index++)
            Add("column-" + index, box, new(.18f, 1.1f, .18f), new(-2.3f + index * .9f, 1.1f, -.6f));
        Add("moving", sphere, new(.55f, .8f, .55f), new(-1 + pose, .8f, 1.2f));
        Add("off-camera", box, new(.25f, 1.6f, .9f), new(-3.5f, 1.6f, -2.5f));
        return new([], []) { Instances = new NativeInstanceSet3D(items) };

        void Add(string id, NativeMesh3D mesh, Vector3 scale, Vector3 position)
        {
            items.Add(new(id, mesh, material, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position)));
        }
    }

    private static NativeMesh3D Box()
    {
        var vertices = new List<Native3DVertex>();
        PrimitiveGeometry3D.AddBox(vertices, Vector3.Zero, Vector3.One, Vector4.One);
        for (int index = 0; index < vertices.Count; index += 3)
            if (Vector3.Dot(Vector3.Cross(vertices[index + 1].Position - vertices[index].Position,
                vertices[index + 2].Position - vertices[index].Position), vertices[index].Normal) < 0)
                (vertices[index + 1], vertices[index + 2]) = (vertices[index + 2], vertices[index + 1]);
        return new("r3-box", vertices.Select(vertex => new NativeModel3DVertex(vertex.Position, vertex.Normal, Vector4.One, Vector2.Zero, new(1, 0, 0, 1))),
            Enumerable.Range(0, vertices.Count).Select(index => (uint)index));
    }

    private static LocalLight3D[] Lights()
    {
        return Enumerable.Range(0, 256).Select(index => new LocalLight3D(new(-4 + (index % 16) * 8f / 15, 2.8f, -4 + (index / 16) * 8f / 15),
            new(.6f + index % 3 * .2f, .7f + index % 2 * .25f, .8f), 3f, 3.5f)
        {
            CastShadows = true, Kind = index % 4 == 0 ? LocalLightKind.Spot : LocalLightKind.Point,
            Direction = -Vector3.UnitY, InnerAngle = .55f, OuterAngle = .85f,
        }).ToArray();
    }

    private static Graphics3DSettings Settings() => Graphics3DSettings.Default with
    {
        Shadows = false, SunIntensity = 0, SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero,
        EnvironmentIntensity = 0, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        AntiAliasing = AntiAliasing3D.None, DirectDiffuseModel = DirectDiffuseModel3D.Lambert,
        ToneMapping = true, Exposure = 1, ToneMapColorRetention = 0, ToneMapGamutCompression = false,
        LocalShadowBudget = 256,
    };

    private static Matrix4x4 Matrix(int width, int height)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(Camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(Camera.Eye, Camera.Target, Camera.Up) * projection;
    }

    private static double Error(byte[] actual, byte[] expected)
    {
        double sum = 0;
        for (int index = 0; index < actual.Length; index++)
            if (index % 4 != 3) sum += Math.Abs(actual[index] - expected[index]) / 255.0;
        return sum / (actual.Length * .75);
    }
    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
