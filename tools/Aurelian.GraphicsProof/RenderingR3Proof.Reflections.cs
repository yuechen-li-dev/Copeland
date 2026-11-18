using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
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
    private const int ReflectionWidth = 240;
    private const int ReflectionHeight = 144;
    private static readonly Vector3 ReflectionEye = new(0, 2, 4);
    private static readonly Vector3 ReflectionView = Vector3.Normalize(ReflectionEye);
    private static readonly Vector3 ReflectionSky = new(.08f, .16f, .24f);
    private static readonly Vector3 ReflectionDirection = Vector3.Reflect(-ReflectionView, Vector3.UnitY);

    public static void RunSharpReflections(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R5a sharp hardware reflections"));
        Require(initialized.Success, "Sharp reflection plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = new EnvironmentRadiance3D(1, 1, [ReflectionSky.X, ReflectionSky.Y, ReflectionSky.Z, 1]);
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        var pairs = new List<object>();
        var cases = new List<object>();
        var flat = ReflectionScene(curved: false);
        var curved = ReflectionScene(curved: true);
        Matrix4x4 clip = ReflectionCamera(ReflectionWidth, ReflectionHeight);
        foreach (var (name, scene) in new[] { ("mirror", flat), ("curved", curved) })
        {
            using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
            using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
            renderer.Settings = renderer.Settings with { SharpReflections = false };
            renderer.Render(scene, clip, ReflectionEye, Clear, capture: true);
            var baseline = renderer.ReadLightingForQualification();
            Require(renderer.CurrentFramePlan!.Graph.Passes.All(pass => pass.Name != "sharp-reflections"), "Disabled trace was scheduled.");
            renderer.Settings = renderer.Settings with { SharpReflections = true };
            renderer.Render(scene, clip, ReflectionEye, Clear, capture: true);
            var actual = renderer.ReadLightingForQualification();
            var witness = renderer.ReadReflectionsForQualification();
            var (expected, geometry) = ReflectionOracle((NativeInstanceSet3D)scene.Instances, clip, witness);
            double error = Rmse(actual.LinearRgb, expected.LinearRgb);
            double baselineError = Rmse(baseline.LinearRgb, expected.LinearRgb);
            Console.WriteLine($"R5a {name}: HDR RMSE {error:F6}, environment {baselineError:F6}. {geometry}");
            Require(error < .025 && error < baselineError * .18, "Sharp reflection did not improve against the independent CPU mirror oracle.");
            Require(renderer.CurrentFramePlan!.Graph.Passes.Any(pass => pass.Name == "sharp-reflections"),
                "Sharp reflections bypassed the compiled frame plan.");
            SaveSamplingImage(plant, assets, output, name + "-environment", baseline);
            SaveSamplingImage(plant, assets, output, name + "-hardware", actual);
            SaveSamplingImage(plant, assets, output, name + "-reference", expected);
            foreach (string variant in new[] { "hardware", "environment" })
                pairs.Add(new { Name = name + "-" + variant, Reference = name + "-reference.png", Test = name + "-" + variant + ".png", Group = name + "-" + variant });
            cases.Add(new { Name = name, Rmse = error, EnvironmentRmse = baselineError, Geometry = geometry });
        }

        pairs.Add(new { Name = "identity", Reference = "mirror-reference.png", Test = "mirror-reference.png", Group = "identity" });
        CheckRoughReflectionFallback(plant, assets, flat, clip, source, environment);
        object lighting = CheckReflectionLighting(plant, assets, output, flat, clip, source, environment);
        object motion = CheckReflectionMotion(plant, assets, output, flat, clip, source, environment);
        var performance = new List<object>();
        foreach (var size in new[] { (1280, 720), (1920, 1080) })
            performance.Add(MeasureSharpReflections(plant, assets, flat, source, environment, size.Item1, size.Item2));
        object beacon = MeasureBeaconSharpReflections(plant, assets, output);
        validation.RequireClean();
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "sharp-reflections", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Cases = cases, Lighting = lighting, Motion = motion, Performance = performance,
            BeaconPerformance = beacon, RoughBoxProbeMatchesDisabled = true,
            Oracle = "CPU orthographic camera, Moller-Trumbore and affine sphere intersection, CPU material sampling and Schlick mirror throughput. No GPU hit guides the reference.",
        }, json));
        Console.WriteLine("AURELIAN_R5A_PASSED " + output);
    }

    private static VulkanSolid3DRenderer ReflectionRenderer(AurelianVulkanPlant plant, GameAssets assets,
        VulkanNativeFrameTarget target, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = Settings() with { SharpReflections = true, EnvironmentIntensity = 1 };
        renderer.LocalLights = [];
        renderer.EnableRayShadows = false;
        renderer.EnvironmentSource = source;
        renderer.Environment = environment;
        return renderer;
    }

    private static Matrix4x4 ReflectionCamera(int width, int height)
    {
        var projection = Matrix4x4.CreateOrthographic(2.4f * width / height, 2.4f, .1f, 30);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(ReflectionEye, Vector3.Zero, Vector3.UnitY) * projection;
    }

    private static void CheckRoughReflectionFallback(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        Matrix4x4 clip, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        var items = scene.Instances.Select(instance => instance.Identity == "mirror"
            ? instance with { Material = instance.Material with { Roughness = .55f } }
            : instance);
        var rough = scene with { Instances = new NativeInstanceSet3D(items) };
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        renderer.ReflectionProbe = new(Vector3.Zero, new(8));
        renderer.Settings = renderer.Settings with { SharpReflections = false };
        byte[] baseline = renderer.Render(rough, clip, ReflectionEye, Clear, capture: true).Pixels!;
        renderer.Settings = renderer.Settings with { SharpReflections = true };
        byte[] actual = renderer.Render(rough, clip, ReflectionEye, Clear, capture: true).Pixels!;
        Require(actual.SequenceEqual(baseline), "Sharp reflections changed the rough box-probe fallback.");
    }

    private static Native3DScene ReflectionScene(bool curved)
    {
        var sphere = NativeAnalyticPrimitives3D.Create("reflection-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var mirror = new ModelMaterial("reflection-mirror") { Roughness = .045f, Metallic = 1, BaseColor = new(.85f, .92f, 1, 1) };
        Vector3 panelUp = Vector3.Normalize(new(0, -ReflectionDirection.Z, ReflectionDirection.Y));
        var panel = ReflectionQuad("reflection-panel", Vector3.UnitX * .65f, panelUp * .8f);
        var checker = new ModelTextureBinding(new("reflection-checker", 2, 2,
            [255, 72, 28, 255, 255, 190, 52, 255, 32, 150, 255, 255, 54, 235, 140, 255]),
            new(false, false, TextureWrap.Clamp, TextureWrap.Clamp));
        var emissive = new ModelMaterial("reflection-card")
        {
            Metallic = 0, Roughness = .8f, Emissive = new(2), EmissiveTexture = checker,
            BaseColor = new(0, 0, 0, 1),
        };
        var analytic = new ModelMaterial("reflection-orb")
        {
            Metallic = 0, Roughness = 1, Emissive = new(.15f, 1, 2.3f), BaseColor = new(0, 0, 0, 1),
        };
        return new([], [])
        {
            Instances = new NativeInstanceSet3D([
                new("mirror", curved ? sphere : ReflectionQuad("reflection-floor", Vector3.UnitX * 1.85f, -Vector3.UnitZ * 2.2f),
                    mirror, curved ? Matrix4x4.CreateScale(1.02f, .98f, 1.04f) : Matrix4x4.Identity),
                new("checker", panel, emissive, Matrix4x4.CreateTranslation(ReflectionDirection * 5 - Vector3.UnitX * .72f)),
                new("orb", sphere, analytic, Matrix4x4.CreateScale(.62f, .81f, .53f) * Matrix4x4.CreateRotationZ(.3f)
                    * Matrix4x4.CreateTranslation(ReflectionDirection * 4.9f + Vector3.UnitX * .75f)),
            ]),
        };
    }

    private static NativeMesh3D ReflectionQuad(string name, Vector3 u, Vector3 v)
    {
        var normal = Vector3.Normalize(Vector3.Cross(u, v));
        var tangent = new Vector4(Vector3.Normalize(u), 1);
        return new(name, [
            new(-u - v, normal, Vector4.One, Vector2.Zero, tangent),
            new(u - v, normal, Vector4.One, Vector2.UnitX, tangent),
            new(u + v, normal, Vector4.One, Vector2.One, tangent),
            new(-u + v, normal, Vector4.One, Vector2.UnitY, tangent),
        ], [0, 1, 2, 0, 2, 3]);
    }

    private readonly record struct CpuReflectionHit(int Identity, float Distance, Vector3 Normal, int Primitive);

    private static CpuReflectionHit TraceReflectionOracle(NativeInstanceSet3D scene, Vector3 origin, Vector3 direction)
    {
        var best = new CpuReflectionHit(-1, float.PositiveInfinity, Vector3.Zero, -1);
        for (int id = 0; id < scene.PackedInstances.Length; id++)
        {
            var instance = scene.PackedInstances[id];
            Matrix4x4.Invert(instance.Transform, out var inverse);
            Vector3 p = Vector3.Transform(origin, inverse);
            Vector3 d = Vector3.TransformNormal(direction, inverse);
            if (instance.Mesh.PrimitiveKind == AnalyticPrimitiveKind3D.UnitSphere)
            {
                float a = Vector3.Dot(d, d);
                float b = Vector3.Dot(p, d);
                float c = Vector3.Dot(p, p) - 1;
                float discriminant = b * b - a * c;
                if (discriminant <= 0) continue;
                float root = MathF.Sqrt(discriminant);
                float distance = (-b - root) / a;
                if (distance < .0002f) distance = (-b + root) / a;
                Commit(distance, Vector3.Normalize(p + d * distance), 0);
                continue;
            }
            var mesh = instance.Mesh;
            for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
            {
                Vector3 a = mesh.Vertices[(int)mesh.Indices[triangle]].Position;
                Vector3 b = mesh.Vertices[(int)mesh.Indices[triangle + 1]].Position - a;
                Vector3 c = mesh.Vertices[(int)mesh.Indices[triangle + 2]].Position - a;
                Vector3 cross = Vector3.Cross(d, c);
                float divisor = Vector3.Dot(b, cross);
                if (MathF.Abs(divisor) < 1e-7f) continue;
                Vector3 offset = p - a;
                float u = Vector3.Dot(offset, cross) / divisor;
                if (u < 0 || u > 1) continue;
                Vector3 side = Vector3.Cross(offset, b);
                float v = Vector3.Dot(d, side) / divisor;
                if (v < 0 || u + v > 1) continue;
                Commit(Vector3.Dot(c, side) / divisor, Vector3.Cross(b, c), triangle / 3);
            }

            void Commit(float distance, Vector3 localNormal, int primitive)
            {
                if (distance < .0002f || distance >= best.Distance) return;
                var normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(inverse)));
                best = new(id, distance, normal, primitive);
            }
        }
        return best;
    }

    private static (ReferenceImage3D Image, object Evidence) ReflectionOracle(NativeInstanceSet3D scene,
        Matrix4x4 clip, (float[] Radiance, float[] Hits) witness)
    {
        Matrix4x4.Invert(clip, out var inverse);
        var pixels = new float[ReflectionWidth * ReflectionHeight * 3];
        int rays = 0, triangleHits = 0, sphereHits = 0, misses = 0, differences = 0, matchingPrimitives = 0, hardwareHits = 0;
        float maximumDistanceError = 0;
        for (int index = 0; index < pixels.Length / 3; index++)
        {
            var position = new Vector3(2 * (index % ReflectionWidth + .5f) / ReflectionWidth - 1,
                2 * (index / ReflectionWidth + .5f) / ReflectionHeight - 1, 0);
            Vector3 start = Vector3.Transform(position, inverse);
            CpuReflectionHit primary = TraceReflectionOracle(scene, start, -ReflectionView);
            if (primary.Identity < 0) continue;
            Require(primary.Identity == 0, "Reflector entered the primary camera.");
            Vector3 p = start - ReflectionView * primary.Distance;
            Vector3 direction = Vector3.Reflect(-ReflectionView, primary.Normal);
            Vector3 origin = p + primary.Normal * .0005f;
            CpuReflectionHit hit = TraceReflectionOracle(scene, origin, direction);
            Vector3 radiance = ReflectionSky;
            if (hit.Identity >= 0)
            {
                var instance = scene.PackedInstances[hit.Identity];
                Require(hit.Identity != 0, "Independent oracle hit its own receiver.");
                var surface = SecondaryOracle(instance, origin + direction * hit.Distance, hit.Normal, true);
                radiance = Vector3.Dot(hit.Normal, -direction) > 0 ? surface.Emission : Vector3.Zero;
                if (instance.Mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles) triangleHits++;
                else sphereHits++;
            }
            else
            {
                misses++;
            }
            Vector4 color = scene.PackedInstances[0].Material.BaseColor;
            Vector3 f0 = new(color.X, color.Y, color.Z);
            Vector3 fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1 - Vector3.Dot(primary.Normal, ReflectionView), 5);
            Vector3 value = fresnel * radiance;
            pixels[index * 3] = value.X;
            pixels[index * 3 + 1] = value.Y;
            pixels[index * 3 + 2] = value.Z;

            rays++;
            int packet = index * 16;
            int observed = (int)witness.Hits[packet + 3];
            if (witness.Radiance[index * 4 + 3] < .99f || observed != hit.Identity)
            {
                differences++;
            }
            else if (hit.Identity >= 0)
            {
                hardwareHits++;
                if (hit.Primitive == (int)witness.Hits[packet + 7]) matchingPrimitives++;
                maximumDistanceError = MathF.Max(maximumDistanceError, MathF.Abs(hit.Distance - witness.Hits[packet + 4]));
            }
        }
        Require(triangleHits > 50 && sphereHits > 30 && misses > 100, "Oracle must witness triangle, affine sphere and environment miss.");
        Console.WriteLine($"R5a hit oracle: {differences}/{rays} identity differences, {matchingPrimitives}/{hardwareHits} primitive matches, {maximumDistanceError:F6} m max distance error.");
        Require(differences < rays * .006 && matchingPrimitives > hardwareHits * .99 && maximumDistanceError < .035f,
            "Hardware identity or distance did not match the independent reflection rays.");
        return (new(ReflectionWidth, ReflectionHeight, 1, pixels),
            new { Rays = rays, TriangleHits = triangleHits, SphereHits = sphereHits, Misses = misses, IdentityDifferences = differences,
                MatchingPrimitives = matchingPrimitives, MatchingHits = hardwareHits, MaximumDistanceErrorMeters = maximumDistanceError });
    }

    private static object CheckReflectionMotion(AurelianVulkanPlant plant, GameAssets assets, string output, Native3DScene scene,
        Matrix4x4 clip, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var freshTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        using var fresh = ReflectionRenderer(plant, assets, freshTarget, source, environment);
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        fresh.Settings = renderer.Settings;
        var previous = (NativeInstanceSet3D)scene.Instances;
        var moved = MoveReflectionCard(scene, 1.8f);
        byte[] initial = renderer.Render(scene, clip, ReflectionEye, Clear, capture: true).Pixels!;
        for (int frame = 1; frame < 8; frame++) renderer.Render(scene, clip, ReflectionEye, Clear);
        byte[] updated = renderer.Render(moved, clip, ReflectionEye, Clear, capture: true).Pixels!;
        byte[] expected = fresh.Render(moved, clip, ReflectionEye, Clear, capture: true).Pixels!;
        Require(renderer.RaySceneStatistics is { TopLevelBuilds: 1, TopLevelUpdates: 1 } && ((NativeInstanceSet3D)moved.Instances).ContinuesFrom(previous),
            "Off-camera motion did not exercise a single retained TLAS refit.");
        Require(!initial.SequenceEqual(updated), "Off-camera motion did not change the presented reflection.");
        Require(updated.SequenceEqual(expected), "Reflection history retained a trail after off-camera motion at matching jitter.");
        NativeGameGraphics.WritePng(Path.Combine(output, "motion-before.png"), ReflectionWidth, ReflectionHeight, initial);
        NativeGameGraphics.WritePng(Path.Combine(output, "motion-after.png"), ReflectionWidth, ReflectionHeight, updated);
        return new { HistoryFrames = 8, MatchesFresh = true, Statistics = renderer.RaySceneStatistics };
    }

    private static Native3DScene MoveReflectionCard(Native3DScene scene, float x)
    {
        var previous = (NativeInstanceSet3D)scene.Instances;
        return scene with
        {
            Instances = new NativeInstanceSet3D(previous.Select(instance => instance with
            {
                Transform = instance.Identity == "checker" ? instance.Transform with { M41 = x } : instance.Transform,
                PreviousTransform = null,
            }), previous),
        };
    }

    private static object MeasureSharpReflections(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        EnvironmentRadiance3D source, EnvironmentLighting environment, int width, int height)
    {
        using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
        using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        Matrix4x4 clip = ReflectionCamera(width, height);
        var rows = new List<object>();
        foreach (var (name, trace, gi) in new[] { ("reflections", true, false), ("environment", false, false), ("gi", false, true), ("gi-reflections", true, true) })
        {
            renderer.Settings = renderer.Settings with { SharpReflections = trace };
            renderer.EnableRayShadows = gi;
            renderer.DynamicDiffuse = gi
                ? new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = new(new(-2, .15f, -4), new(2, 2.5f, 2), 6, 4, 6, 128), RaysPerProbePerFrame = 16 }
                : new();
            Native3DScene current = scene;
            object timing = CollectReflectionTiming(frame =>
            {
                current = MoveReflectionCard(current, -.72f + .25f * MathF.Sin(frame * .2f));
                return renderer.Render(current, clip, ReflectionEye, Clear);
            });
            renderer.WaitForPendingFrames();
            var captureScene = MoveReflectionCard(current, -.72f);
            var captured = renderer.Render(captureScene, clip, ReflectionEye, Clear, capture: true);
            double? tracePass = trace ? captured.GpuPassTimes.SingleOrDefault(pass => pass.Pass == "sharp-reflections")?.Milliseconds : null;
            double? sampledRmse = trace && !gi
                ? CheckHighResolutionReflection((NativeInstanceSet3D)captureScene.Instances, clip, renderer.ReadLightingForQualification())
                : null;
            rows.Add(new { Variant = name, Timing = timing, CapturedTraceMilliseconds = tracePass, SampledHdrRmse = sampledRmse });
            Console.WriteLine($"R5a {width}x{height} {name}: {timing}, trace {tracePass:F3}ms.");
        }
        return new { Width = width, Height = height, Variants = rows };
    }

    private static double CheckHighResolutionReflection(NativeInstanceSet3D scene, Matrix4x4 clip, ReferenceImage3D actual)
    {
        Matrix4x4.Invert(clip, out var inverse);
        Vector4 color = scene[0].Material.BaseColor;
        Vector3 f0 = new(color.X, color.Y, color.Z);
        Vector3 fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1 - ReflectionView.Y, 5);
        double sum = 0;
        int count = 0;
        for (int y = 3; y < actual.Height; y += 9)
        {
            for (int x = 3; x < actual.Width; x += 9)
            {
                var ndc = new Vector3(2 * (x + .5f) / actual.Width - 1, 2 * (y + .5f) / actual.Height - 1, 0);
                Vector3 start = Vector3.Transform(ndc, inverse);
                var primary = TraceReflectionOracle(scene, start, -ReflectionView);
                Vector3 expected = Vector3.Zero;
                if (primary.Identity == 0)
                {
                    Vector3 origin = start - ReflectionView * primary.Distance + Vector3.UnitY * .0005f;
                    var hit = TraceReflectionOracle(scene, origin, ReflectionDirection);
                    Vector3 source = ReflectionSky;
                    if (hit.Identity >= 0)
                        source = SecondaryOracle(scene.PackedInstances[hit.Identity], origin + ReflectionDirection * hit.Distance, hit.Normal, true).Emission;
                    expected = fresnel * source;
                }
                int offset = (y * actual.Width + x) * 3;
                Vector3 observed = new(actual.LinearRgb[offset], actual.LinearRgb[offset + 1], actual.LinearRgb[offset + 2]);
                sum += Vector3.DistanceSquared(expected, observed);
                count++;
            }
        }
        double rmse = Math.Sqrt(sum / (count * 3));
        Require(rmse < .012, $"Multi-dispatch reflection output diverged at {actual.Width}x{actual.Height}: {rmse}.");
        return rmse;
    }

    private static object MeasureBeaconSharpReflections(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        using var game = new BeaconGame();
        var publisher = new BeaconResidentScene3D();
        var scene = publisher.Build(game, includeViewmodel: false);
        bool IsFloor(SceneInstance3D instance) => instance.Identity.Contains("/floor/", StringComparison.Ordinal);
        Require(scene.Instances.Any(IsFloor), "Beacon proof needs the authored floor receiver.");
        scene = scene with
        {
            Instances = new NativeInstanceSet3D(scene.Instances.Select(instance => IsFloor(instance)
                ? instance with { Material = instance.Material with { Roughness = .075f } }
                : instance)),
        };
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        var camera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var rows = new List<object>();
        foreach (int width in new[] { 1280, 1920 })
        {
            int height = width * 9 / 16;
            using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            using var renderer = BeaconRenderer(plant, assets, target, environment);
            Matrix4x4 clip = RegionalMatrix(camera, width, height);
            renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
            byte[]? baseline = null;
            foreach (bool reflections in new[] { false, true })
            {
                renderer.Settings = renderer.Settings with { SharpReflections = reflections };
                var timing = CollectReflectionTiming(_ => renderer.Render(scene, clip, camera.Eye, Clear));
                renderer.WaitForPendingFrames();
                byte[] pixels = renderer.Render(scene, clip, camera.Eye, Clear, capture: true).Pixels!;
                string variant = reflections ? "gi-reflections" : "gi";
                NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-{width}-{variant}.png"), width, height, pixels);
                if (baseline is not null)
                    Require(Error(pixels, baseline) > .003, "The polished Beacon floor did not change with perspective reflections.");
                baseline = pixels;
                Require(renderer.RaySceneStatistics is { TopLevelBuilds: 1 }, "Beacon reflections rebuilt the GI acceleration state.");
                rows.Add(new { Width = width, Height = height, Variant = variant, Timing = timing });
                Console.WriteLine($"R5a Beacon {width}x{height} {variant}: {timing}.");
            }
        }
        return rows;
    }

    private static object CollectReflectionTiming(Func<int, Native3DFrameResult> render)
    {
        const int warmup = 40;
        const int measured = 64;
        var gpu = new List<double>();
        var cpu = new List<double>();
        for (int frame = 0; frame < warmup + measured; frame++)
        {
            var result = render(frame);
            if (frame < warmup) continue;
            cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
            if (result.CompletedGpuFrame is { } completion)
                gpu.Add((completion.EndTicks - completion.StartTicks) * completion.NanosecondsPerTick / 1_000_000);
        }
        Require(gpu.Count >= measured - 3, "Missing reflection performance timestamps.");
        double p95 = gpu.Order().ElementAt((int)Math.Ceiling(gpu.Count * .95) - 1);
        return new { WarmupFrames = warmup, MeasuredFrames = measured, MedianGpuMilliseconds = Median(gpu),
            P95GpuMilliseconds = p95, MedianCpuMilliseconds = Median(cpu) };
    }
}
