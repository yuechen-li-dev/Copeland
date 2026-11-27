using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static readonly NativeFrameClearColor ScalingClear = new(.035f, .048f, .065f, 1);
    private static readonly float[] ScalingSrgb = Enumerable.Range(0, 256).Select(value => NativeSrgbTransfer.Decode(value / 255f)).ToArray();
    private readonly record struct ScalingVariant(string Name, RenderScale3D Scale, bool Temporal, bool History = true);
    private static readonly ScalingVariant[] ScalingVariants =
    [new("native", RenderScale3D.Native, true), new("two-thirds-spatial", RenderScale3D.TwoThirds, false),
        new("two-thirds-temporal", RenderScale3D.TwoThirds, true), new("half-spatial", RenderScale3D.Half, false),
        new("half-temporal", RenderScale3D.Half, true)];

    public static void RunResolutionScaling(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R6 fixed-scale reconstruction"));
        Require(initialized.Success, "Resolution scaling Vulkan plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = new EnvironmentRadiance3D(1, 1, [ReflectionSky.X, ReflectionSky.Y, ReflectionSky.Z, 1]);
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        const int width = 960, height = 540, frames = 24;
        var quality = new List<object>();
        var pairs = new List<object>();
        foreach (string specimen in new[] { "thin-static", "thin-motion", "particles", "glossy" })
        {
            var reference = CaptureScaling(plant, assets, source, environment, specimen, new("reference", RenderScale3D.Native, false),
                width * 2, height * 2, frames);
            byte[] expected = ReduceScalingReference(reference.Last, width, height);
            byte[] previous = ReduceScalingReference(reference.Previous, width, height);
            string referenceName = specimen + "-reference.png";
            NativeGameGraphics.WritePng(Path.Combine(output, referenceName), width, height, expected);
            var spatial = new Dictionary<RenderScale3D, ScalingMetrics>();
            foreach (var variant in ScalingVariants)
            {
                var capture = CaptureScaling(plant, assets, source, environment, specimen, variant, width, height, frames);
                string name = specimen + "-" + variant.Name;
                byte[]? cold = null;
                if (variant is { Temporal: true, Scale: not RenderScale3D.Native } && specimen != "glossy")
                {
                    var control = CaptureScaling(plant, assets, source, environment, specimen, variant with { History = false }, width, height, frames);
                    cold = control.Last;
                    string coldName = name + "-cold";
                    NativeGameGraphics.WritePng(Path.Combine(output, coldName + ".png"), width, height, cold);
                    pairs.Add(new { Name = coldName, Reference = referenceName, Test = coldName + ".png", Group = coldName });
                }
                NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, capture.Last);
                var metrics = ScalingError(capture.Last, expected, previous, cold);
                quality.Add(new { Specimen = specimen, Variant = variant.Name, capture.RenderWidth, capture.RenderHeight, Metrics = metrics });
                pairs.Add(new { Name = name, Reference = referenceName, Test = name + ".png", Group = name });
                Console.WriteLine($"R6 {name} at {capture.RenderWidth}x{capture.RenderHeight}: {metrics}");
                if (variant.Scale == RenderScale3D.Native) continue;
                if (!variant.Temporal)
                {
                    spatial.Add(variant.Scale, metrics);
                    continue;
                }
                var controlMetrics = spatial[variant.Scale];
                Require(metrics.LinearDisplayRmse <= controlMetrics.LinearDisplayRmse * 1.02,
                    "Scaled temporal reconstruction exceeded the spatial error limit for " + name);
                if (specimen is "thin-motion" or "particles")
                {
                    Require(metrics.VacatedPixels > 1000 && metrics.ExcessHistoryGhostEnergy is <= .000001,
                        "History left excess luminance in the vacated region for " + name);
                }
                if (specimen == "thin-motion") Require(metrics.LinearDisplayRmse < controlMetrics.LinearDisplayRmse * .96,
                    "Scaled temporal reconstruction did not improve moving geometry for " + name);
            }
        }
        pairs.Add(new { Name = "identity", Reference = "thin-static-reference.png", Test = "thin-static-reference.png", Group = "identity" });
        object reset = CheckScaledCameraCut(plant, assets);
        object performance = MeasureResolutionScaling(plant, assets, output);
        validation.RequireClean();
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "resolution-scaling", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Quality = quality, Performance = performance, Reset = reset, Width = width, Height = height,
            ReferenceScale = 2, Frames = frames,
        }, json));
        Console.WriteLine("AURELIAN_R6_PASSED " + output);
    }

    private sealed record ScalingCapture(byte[] Last, byte[] Previous, uint RenderWidth, uint RenderHeight);

    private static ScalingCapture CaptureScaling(AurelianVulkanPlant plant, GameAssets assets, EnvironmentRadiance3D source,
        EnvironmentLighting environment, string specimen, ScalingVariant variant, int width, int height, int frames)
    {
        bool glossy = specimen == "glossy";
        bool particles = specimen == "particles";
        using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
        using var emitter = particles ? new VulkanParticles3D(plant, assets.ComputeShader("Particles3D.v.ts"), 1, 192) : null;
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, enableCulling: glossy, rayShadows: glossy,
            target: target, renderScale: variant.Scale, particles: particles);
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings() with
        {
            SolidPbr = glossy, AntiAliasing = variant.Temporal ? AntiAliasing3D.Temporal : AntiAliasing3D.None,
            GlossyReflections = glossy ? GlossyReflectionMode3D.FullResolution : GlossyReflectionMode3D.Off,
            EnvironmentIntensity = glossy ? 1 : 0,
            TemporalHistoryWeight = variant.History ? Graphics3DSettings.Default.TemporalHistoryWeight : 0,
        };
        if (glossy)
        {
            renderer.EnableVisibilityBuffer = true;
            renderer.Environment = environment;
            renderer.EnvironmentSource = source;
        }
        renderer.ParticleSystem = emitter;
        var sparks = ParticleEmitter3D.Sparks("scaling-sparks", new(-.27f, -.34f, .4f), 105) with
        {
            Speed = .62f, Lifetime = 1.9f, Radius = .09f, Size = .011f, Rate = 42, Turbulence = .12f,
        };
        var reflected = glossy ? SetReflectionRoughness(ReflectionScene(false), .27f) : null;
        byte[] previous = [], last = [];
        for (int frame = 0; frame < frames; frame++)
        {
            if (particles) renderer.ParticleEmitters = [sparks with { ElapsedSeconds = .68f + frame * .043f }];
            var scene = reflected ?? new Native3DScene(ScalingGeometry(specimen, frame), []);
            Matrix4x4 camera = glossy ? ReflectionCamera(width, height) : Matrix4x4.Identity;
            var frameResult = renderer.Render(scene, camera, glossy ? ReflectionEye : new(0, 0, -2), ScalingClear, capture: frame >= frames - 2);
            if (frame == frames - 2) previous = frameResult.Pixels!;
            if (frame == frames - 1) last = frameResult.Pixels!;
        }
        var graph = renderer.CurrentFramePlan!.Graph;
        var geometry = graph.Resources.Single(resource => resource.Name == "geometry-color");
        var display = graph.Resources.Single(resource => resource.Name == "display-output");
        Require(geometry.Width == renderer.RenderWidth && geometry.Height == renderer.RenderHeight && display.Width == width && display.Height == height,
            "The compiled frame plan did not preserve the separate render and display extents.");
        bool reconstructs = variant.Temporal || variant.Scale != RenderScale3D.Native;
        if (reconstructs)
        {
            var history = graph.Resources.Single(resource => resource.Name == "temporal-history");
            Require(history.Width == width && history.Height == height, "Reconstruction history did not have display dimensions.");
        }
        return new(last, previous, renderer.RenderWidth, renderer.RenderHeight);
    }

    private static Native3DVertex[] ScalingGeometry(string specimen, int frame)
    {
        var vertices = new List<Native3DVertex>();
        Rectangle(-1, -.82f, 1, .86f, .78f, new(.037f, .063f, .104f, 1));
        if (specimen == "thin-static")
        {
            for (int bar = 0; bar < 115; bar++)
            {
                float x = -.81f + bar * .01427f;
                float phase = (bar % 5) * .011f;
                Rectangle(x, -.64f + phase, x + .00145f + phase * .014f, .65f - phase, .32f, new(.9f, .8f, .31f, 1));
            }
        }
        if (specimen == "thin-motion")
        {
            float x = -.72f + frame * .0517f;
            Rectangle(x, -.62f, x + .125f, .69f, .23f, new(.93f, .64f, .19f, 1));
            for (int bar = 0; bar < 8; bar++)
            {
                float left = x + .18f + bar * .025f;
                Rectangle(left, -.69f, left + .0032f, .72f, .27f, new(.28f, .82f, .9f, 1));
            }
        }
        return vertices.ToArray();

        void Rectangle(float left, float top, float right, float bottom, float z, Vector4 color)
        {
            Vector3 a = new(left, top, z), b = new(right, top, z), c = new(right, bottom, z), d = new(left, bottom, z);
            foreach (Vector3 point in new[] { a, b, c, a, c, d }) vertices.Add(new(point, Vector3.UnitZ, color));
        }
    }

    private sealed record ScalingMetrics(double LinearDisplayRmse, double GhostEnergy, double? ExcessHistoryGhostEnergy, int VacatedPixels);

    private static ScalingMetrics ScalingError(byte[] pixels, byte[] reference, byte[] previous, byte[]? cold,
        ReadOnlySpan<bool> ghostMask = default)
    {
        double squared = 0, ghost = 0, historyGhost = 0;
        int vacated = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                double difference = ScalingSrgb[pixels[offset + channel]] - ScalingSrgb[reference[offset + channel]];
                squared += difference * difference;
            }
            if (!ghostMask.IsEmpty && !ghostMask[offset / 4]) continue;
            float expected = Luminance(reference, offset);
            if (Luminance(previous, offset) - expected < .07f) continue;
            ghost += Math.Max(0, Luminance(pixels, offset) - expected);
            if (cold is not null) historyGhost += Math.Max(0, Luminance(pixels, offset) - Math.Max(expected, Luminance(cold, offset)));
            vacated++;
        }
        return new(Math.Sqrt(squared / (pixels.Length / 4 * 3)), ghost / Math.Max(vacated, 1),
            cold is null ? null : historyGhost / Math.Max(vacated, 1), vacated);

        static float Luminance(byte[] image, int offset) => .2126f * ScalingSrgb[image[offset]]
            + .7152f * ScalingSrgb[image[offset + 1]] + .0722f * ScalingSrgb[image[offset + 2]];
    }

    private static byte[] ReduceScalingReference(byte[] pixels, int width, int height)
    {
        var result = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int input = (y * 2 * width * 2 + x * 2) * 4;
                int output = (y * width + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    int at = input + channel;
                    float linear = (ScalingSrgb[pixels[at]] + ScalingSrgb[pixels[at + 4]]
                        + ScalingSrgb[pixels[at + width * 8]] + ScalingSrgb[pixels[at + width * 8 + 4]]) * .25f;
                    float srgb = linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
                    result[output + channel] = (byte)Math.Clamp((int)MathF.Round(srgb * 255), 0, 255);
                }
                result[output + 3] = 255;
            }
        }
        return result;
    }

    private static object CheckScaledCameraCut(AurelianVulkanPlant plant, GameAssets assets)
    {
        using var target = new VulkanNativeFrameTarget(plant, 513, 289);
        using var coldTarget = new VulkanNativeFrameTarget(plant, 513, 289);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, renderScale: RenderScale3D.TwoThirds);
        using var cold = RenderingBindlessProof.CreateRenderer(plant, assets, coldTarget, renderScale: RenderScale3D.TwoThirds);
        renderer.Settings = cold.Settings = Settings() with { SolidPbr = false, AntiAliasing = AntiAliasing3D.Temporal };
        var scene = new Native3DScene(ScalingGeometry("thin-static", 0), []);
        for (int frame = 0; frame < 12; frame++) renderer.Render(scene, Matrix4x4.Identity, Vector3.Zero, ScalingClear);
        renderer.ResetTemporalHistory();
        Matrix4x4 clip = Matrix4x4.CreateTranslation(.31f, -.19f, 0);
        byte[] actual = renderer.Render(scene, clip, Vector3.Zero, ScalingClear, capture: true).Pixels!;
        byte[] expected = cold.Render(scene, clip, Vector3.Zero, ScalingClear, capture: true).Pixels!;
        Require(actual.SequenceEqual(expected) && actual.Length == 513 * 289 * 4 && renderer.RenderWidth == 342 && renderer.RenderHeight == 193,
            "A camera cut or odd-size reconstruction failed to match fresh display history.");
        return new { Width = 513, Height = 289, renderer.RenderWidth, renderer.RenderHeight, MatchesFresh = true };
    }

    private static object MeasureResolutionScaling(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        using var game = new BeaconGame();
        var arena = new BeaconResidentScene3D().Build(game, includeViewmodel: false);
        arena = arena with
        {
            Instances = new NativeInstanceSet3D(arena.Instances.Select(instance => instance.Identity.Contains("/floor/", StringComparison.Ordinal)
                ? instance with { Material = instance.Material with { Roughness = .24f } }
                : instance)),
        };
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        var camera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        const int width = 1920, height = 1080;
        Matrix4x4 clip = RegionalMatrix(camera, width, height);
        var rows = new List<object>();
        foreach (var variant in ScalingVariants.Append(ScalingVariants[0] with { Name = "native-repeat" }))
        {
            using var target = new VulkanNativeFrameTarget(plant, width, height);
            using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, enableCulling: true, rayShadows: true,
                target: target, renderScale: variant.Scale);
            renderer.FrameMode = Native3DFrameMode.Interactive;
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Environment = environment;
            renderer.EnvironmentSource = BeaconDiffuseLighting3D.Source;
            renderer.LocalLights = BeaconDiffuseLighting3D.Lights;
            renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
            renderer.Settings = BeaconDiffuseLighting3D.Settings with
            {
                AntiAliasing = variant.Temporal ? AntiAliasing3D.Temporal : AntiAliasing3D.None,
                GlossyReflections = GlossyReflectionMode3D.HalfResolution,
            };
            object timing = CollectReflectionTiming(_ => renderer.Render(arena, clip, camera.Eye, Clear));
            renderer.WaitForPendingFrames();
            var captured = renderer.Render(arena, clip, camera.Eye, Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, "beacon-" + variant.Name + ".png"), width, height, captured.Pixels!);
            double resolve = captured.GpuPassTimes.Single(pass => pass.Pass == "temporal-resolve").Milliseconds;
            rows.Add(new { Variant = variant.Name, renderer.RenderWidth, renderer.RenderHeight, Width = width, Height = height, Timing = timing, ResolveMilliseconds = resolve });
            Console.WriteLine($"R6 Beacon {variant.Name}: {timing}; reconstruction {resolve:F3}ms.");
        }
        return rows;
    }
}
