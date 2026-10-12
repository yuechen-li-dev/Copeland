using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

internal static class WaterGraphicsProof
{
    private const int Width = 960;
    private const int Height = 640;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian water"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
                RefractiveModel = assets.Shader("RefractiveModel3D.v.ts"),
                RefractionResolve = assets.Shader("RefractionResolve3D.v.ts"),
                WaterTemporal = assets.Shader("TemporalResolve3D.v.ts"),
            });
        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"),
            128, 64, Sky());
        renderer.Settings = Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new(-.5f, .85f, .35f)),
            SunIntensity = 2.5f,
            SunColor = new(1, .94f, .82f),
            SkyAmbient = new(.04f, .06f, .08f),
            GroundAmbient = new(.015f),
            BloomIntensity = .03f,
            Exposure = 1.1f,
            AntiAliasing = AntiAliasing3D.None,
        };
        var water = new WaterSurface3D();
        var patch = WaterSurfaceMesh3D.Create("pool", new(8, 6), water);
        var stone = new ModelMaterial("stone") { Metallic = 0, Roughness = .8f, BaseColor = new(.42f, .44f, .40f, 1) };
        var tile = new ModelMaterial("tile") { Metallic = 0, Roughness = .65f, BaseColorTexture = Checker() };
        using var authored = SceneCompiler.Compile(Scene.World("pond", [
            Scene.Box("surround", new(14, .25f, 12), new(.23f, .30f, .23f, 1), new(0, -1.65f, 0)),
            Scene.Box("bottom", new(8, .2f, 6), Vector4.One, new(0, -1.5f, 0)) with { Material = tile },
            Scene.Box("step-low", new(1.4f, .7f, 5.9f), Vector4.One, new(-2.5f, -1.05f, 0)) with { Material = tile },
            Scene.Box("step-mid", new(.75f, 1.1f, 5.9f), Vector4.One, new(-3.35f, -.85f, 0)) with { Material = tile },
            Scene.Box("step-shallow", new(.4f, 1.38f, 5.9f), Vector4.One, new(-3.8f, -.71f, 0)) with { Material = tile },
            Scene.Box("west", new(.35f, .4f, 6.7f), Vector4.One, new(-4.18f, .1f, 0)) with { Material = stone },
            Scene.Box("east", new(.35f, .4f, 6.7f), Vector4.One, new(4.18f, .1f, 0)) with { Material = stone },
            Scene.Box("north", new(8, .4f, .35f), Vector4.One, new(0, .1f, -3.18f)) with { Material = stone },
            Scene.Box("south", new(8, .4f, .35f), Vector4.One, new(0, .1f, 3.18f)) with { Material = stone },
            Scene.Box("contact-rock", new(.65f, 1.7f, .8f), Vector4.One, new(1.7f, -.6f, -.8f)) with { Material = stone },
            Scene.Box("reflection-marker", new(.5f, 2.4f, .5f), new(.65f, .18f, .08f, 1), new(.9f, 1.2f, -3.7f)),
            Scene.Box("reflection-marker-green", new(.6f, 1.1f, .5f), new(.10f, .40f, .13f, 1), new(2.3f, .55f, -3.7f)),
            patch,
        ])).Mount();
        Native3DScene scene = SceneGeometry3D.BuildScene(authored.Project());
        var clear = new NativeFrameClearColor(.16f, .22f, .29f, 1);
        Vector3 eye = new(8, 5, 9);
        Vector3 look = new(0, -.15f, 0);
        Matrix4x4 camera = Camera(eye, look);
        byte[] dry = Capture("dry", scene with { Models = scene.Models.Where(batch => batch.Material.Water is null).ToArray() });
        byte[] initial = Capture("water", scene with { WaterFrame = new(1) });
        Require(Changed(dry, initial) > 10000, "Water failed to shade the pond.");
        byte[] waves = Capture("waves", scene with { WaterFrame = new(1.7f) });
        int wavePixels = Changed(initial, waves);
        Require(wavePixels > 1000, "GPU wind waves did not animate.");
        var impactFrame = new WaterFrame3D(1.7f) { Impulses = [new(new(.2f, .3f), 1.2f, .09f)] };
        byte[] impact = Capture("impact", scene with { WaterFrame = impactFrame });
        int impulsePixels = Changed(waves, impact);
        Require(impulsePixels > 100, "An impact event did not alter the GPU water surface.");
        byte[] expired = Capture("expired", scene with { WaterFrame = impactFrame with { Time = 5 } });
        byte[] noEvent = Capture("expired-reference", scene with { WaterFrame = new(5) });
        Require(expired.SequenceEqual(noEvent), "Expired impulse retained a water displacement.");
        var noFoam = scene with { Models = scene.Models.Select(batch => batch.Material.Water is null ? batch : batch with
        { Material = batch.Material with { Water = water with { FoamStrength = 0 } } }).ToArray(), WaterFrame = new(1) };
        byte[] foamReference = Capture("without-foam", noFoam);
        byte[] foam = Capture("foam", scene with { WaterFrame = new(1) });
        int foamPixels = Changed(foamReference, foam);
        Require(foamPixels > 100, "Shallow contact foam did not affect the water.");
        var reflected = scene with { WaterReflection = new(0), WaterFrame = new(1) };
        foreach (WaterDebugView3D debug in Enum.GetValues<WaterDebugView3D>().Where(value => value != WaterDebugView3D.Shaded))
        {
            Capture("debug-" + debug, reflected with { Models = reflected.Models.Select(batch => batch.Material.Water is null ? batch : batch with
            { Material = batch.Material with { Water = water with { DebugView = debug } } }).ToArray() });
        }
        byte[] planar = Capture("planar", reflected);
        int reflectionPixels = Changed(foam, planar);
        Require(reflectionPixels > 100, "Planar reflection did not add the above-water scene.");
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        var frames = new List<double>();
        var reflectionTimes = new List<double>();
        for (int frame = 0; frame < 40; frame++)
        {
            float time = 1 + frame / 60f;
            Vector3 movingEye = eye + new Vector3(frame * .006f, 0, 0);
            var result = renderer.Render(reflected with { WaterFrame = impactFrame with { Time = time } },
                Camera(movingEye, look), movingEye, clear);
            if (frame >= 8)
            {
                frames.Add(result.GpuPassTimes.First(item => item.Pass == "refraction").Milliseconds);
                reflectionTimes.Add(result.GpuPassTimes.First(item => item.Pass == "water-reflection").Milliseconds);
            }
        }
        var finalScene = reflected with { WaterFrame = impactFrame with { Time = 2 } };
        eye += new Vector3(.144f, 0, 0);
        camera = Camera(eye, look);
        byte[] final = Capture("pond-final", finalScene);
        var calm = finalScene with { WaterFrame = new(2) };
        byte[] cancelledImpact = Capture("cancelled-impact", calm);
        renderer.ResetTemporalHistory();
        byte[] freshCalm = Capture("cancelled-impact-fresh", calm);
        Require(freshCalm.SequenceEqual(cancelledImpact), "Cancelling an active impulse retained its old water history.");
        var withoutReflection = calm with { WaterReflection = null };
        byte[] toggledReflection = Capture("reflection-disabled", withoutReflection);
        renderer.ResetTemporalHistory();
        byte[] freshReflection = Capture("reflection-disabled-fresh", withoutReflection);
        Require(toggledReflection.SequenceEqual(freshReflection), "Disabling reflection retained its old water history.");
        for (int frame = 0; frame < 12; frame++)
        {
            renderer.Render(calm, camera, eye, clear);
        }
        Capture("pond-preview", calm);
        var removed = finalScene with { Models = finalScene.Models.Where(batch => batch.Material.Water is null).ToArray(), WaterReflection = null };
        byte[] afterRemoval = Capture("removed", removed);
        renderer.ResetTemporalHistory();
        byte[] freshRemoval = Capture("removed-fresh", removed);
        Require(afterRemoval.SequenceEqual(freshRemoval), "Removing water left temporal history behind.");
        bool underwaterRejected = false;
        try
        {
            renderer.Render(scene, camera, new(0, -.1f, 1), clear);
        }
        catch (NotSupportedException error)
        {
            underwaterRejected = error.Message.StartsWith("AUR-WATER-009", StringComparison.Ordinal);
        }
        Require(underwaterRejected, "Underwater transport was silently admitted.");
        // Independent flat-water optical fixture: no wind, foam, sky or direct
        // lighting. Compare GPU transport against Snell/Fresnel/Beer-Lambert.
        renderer.Environment = null;
        renderer.Settings = renderer.Settings with
        {
            AntiAliasing = AntiAliasing3D.None, ToneMapping = false, Exposure = 1,
            SunIntensity = 0, SkyAmbient = Vector3.Zero, GroundAmbient = Vector3.Zero, BloomIntensity = 0,
        };
        eye = new(0, 6, 4);
        camera = Camera(eye, Vector3.Zero);
        var flat = new WaterSurface3D { Waves = [], DetailAmplitude = 0, FoamStrength = 0, DeepColor = Vector3.Zero };
        float maximumOpticalError = 0;
        var opticalSamples = new List<object>();
        foreach (float depth in new[] { .25f, 1.5f })
        {
            using var fixture = SceneCompiler.Compile(Scene.World("optics", [
                Scene.Box("reference", new(20, .1f, 20), Vector4.One, new(0, -depth - .05f, 0)) with
                { Material = new("unlit") { Unlit = true, Metallic = 0, BaseColor = new(.4f, .6f, .8f, 1) } },
                WaterSurfaceMesh3D.Create("flat-water", new(10, 10), flat, 2, 2),
            ])).Mount();
            byte[] pixels = Capture("optical-" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SceneGeometry3D.BuildScene(fixture.Project()));
            float eta = 1 / 1.333f;
            float nv = Vector3.Normalize(eye).Y;
            float cosine = MathF.Sqrt(1 - eta * eta * (1 - nv * nv));
            float path = depth / cosine;
            float f0 = MathF.Pow((1.333f - 1) / (1.333f + 1), 2);
            float fresnel = f0 + (1 - f0) * MathF.Pow(1 - nv, 5);
            Vector3 expected = new Vector3(.4f, .6f, .8f) * new Vector3(
                MathF.Pow(.38f, path / 2), MathF.Pow(.78f, path / 2), MathF.Pow(.84f, path / 2))
                * ((1 - fresnel) * MathF.Exp(-path * .18f));
            int offset = (Height / 2 * Width + Width / 2) * 4;
            Vector3 actual = new(NativeSrgbTransfer.Decode(pixels[offset] / 255f),
                NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
            float error = Vector3.Distance(actual, expected);
            maximumOpticalError = Math.Max(maximumOpticalError, error);
            opticalSamples.Add(new { Depth = depth, Path = path, Expected = new[] { expected.X, expected.Y, expected.Z },
                Actual = new[] { actual.X, actual.Y, actual.Z }, Error = error });
        }
        Require(maximumOpticalError < .015f, "Water absorption failed the independent optical fixture: " + maximumOpticalError);
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            WindChangedPixels = wavePixels,
            ImpactChangedPixels = impulsePixels,
            FoamChangedPixels = foamPixels,
            ReflectionChangedPixels = reflectionPixels,
            ExpiredImpulseExact = true,
            RemovedWaterHistoryExact = true,
            CancelledImpulseHistoryExact = true,
            ReflectionToggleHistoryExact = true,
            UnderwaterRejected = underwaterRejected,
            OpticalError = maximumOpticalError,
            OpticalSamples = opticalSamples,
            WaterTriangles = patch.Indices.Length / 3,
            WarmupFrames = 8,
            MeasuredFrames = frames.Count,
            RefractionGpu = Distribution(frames),
            PlanarReflectionGpu = Distribution(reflectionTimes),
            Limits = new[] { "above-water perspective view", "nearest refractive layer", "screen-space opaque refraction", "one coplanar opaque reflection", "eight authored impulses" },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_WATER_GRAPHICS_PASSED " + evidence);

        byte[] Capture(string name, Native3DScene input)
        {
            byte[] pixels = renderer.Render(input, camera, eye, clear, true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, pixels);
            return pixels;
        }
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.8f, (float)Width / Height, .1f, 80);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    private static float[] Sky()
    {
        var values = new float[128 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                int first = (y * 128 + x) * 4;
                float horizon = MathF.Exp(-MathF.Pow((y - 30) / 14f, 2));
                values[first] = .14f + horizon * .5f;
                values[first + 1] = .28f + horizon * .5f;
                values[first + 2] = .5f + horizon * .4f;
                values[first + 3] = 1;
            }
        }
        return values;
    }

    private static ModelTextureBinding Checker()
    {
        byte[] pixels = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool line = x % 8 == 0 || y % 8 == 0;
                int first = (y * 32 + x) * 4;
                pixels[first] = line ? (byte)90 : (byte)190;
                pixels[first + 1] = line ? (byte)110 : (byte)211;
                pixels[first + 2] = line ? (byte)112 : (byte)205;
                pixels[first + 3] = 255;
            }
        }
        return new(new("pool-checker", 32, 32, pixels.ToImmutableArray()), new());
    }

    private static int Changed(byte[] a, byte[] b)
    {
        int count = 0;
        for (int first = 0; first < a.Length; first += 4)
        {
            if (Math.Abs(a[first] - b[first]) + Math.Abs(a[first + 1] - b[first + 1]) + Math.Abs(a[first + 2] - b[first + 2]) > 4)
            {
                count++;
            }
        }
        return count;
    }

    private static object Distribution(List<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return new { MedianMilliseconds = sorted[sorted.Length / 2], P95Milliseconds = sorted[(int)((sorted.Length - 1) * .95)] };
    }

    private static void Require(bool accepted, string message)
    {
        if (!accepted)
        {
            throw new InvalidOperationException(message);
        }
    }
}
