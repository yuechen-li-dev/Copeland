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

internal static class OceanGraphicsProof
{
    private const int Width = 960;
    private const int Height = 640;
    private const int Size = 64;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian spectral ocean"));
        WaterGraphicsProof.Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        var assets = new GameAssets();
        using var renderer = WaterGraphicsProof.CreateRenderer(plant, target, assets);
        renderer.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), 128, 64, WaterGraphicsProof.Sky());
        renderer.Settings = Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new(-.55f, .63f, .55f)),
            SunColor = new(1, .94f, .82f),
            SunIntensity = 2.4f,
            SkyAmbient = new(.11f, .16f, .23f),
            GroundAmbient = new(.025f, .035f, .04f),
            BloomIntensity = .035f,
            AntiAliasing = AntiAliasing3D.None,
        };
        var spectrum = new OceanSpectrum3D
        {
            TileLength = 42,
            Wind = new(9, 3),
            HeightDeviation = .72f,
            Choppiness = 1.52f,
        };
        var profile = new WaterSurface3D
        {
            Ocean = spectrum,
            FoamStrength = 0,
            DetailAmplitude = .0012f,
            DetailWavelength = .32f,
            MaximumDepth = 12,
            DeepColor = new(.006f, .12f, .17f),
        };
        var patch = WaterSurfaceMesh3D.Create("open-water", new(88, 102), profile, 160, 192, new(0, 0, -24));
        using var authored = SceneCompiler.Compile(Scene.World("windward-channel", [
            Scene.Box("deep-shelf", new(132, .4f, 146), new(.04f, .23f, .21f, 1), new(0, -8, -24)),
            Scene.Box("channel-marker", new(.32f, 4.2f, .32f), new(.24f, .16f, .09f, 1), new(-6, -.45f, 1)),
            Scene.Box("channel-marker-cap", new(.61f, .45f, .61f), new(.80f, .21f, .07f, 1), new(-6, 1.55f, 1)),
            Scene.Box("far-marker", new(.32f, 4.2f, .32f), new(.24f, .16f, .09f, 1), new(6, -.45f, -11)),
            Scene.Box("far-marker-cap", new(.61f, .45f, .61f), new(.80f, .21f, .07f, 1), new(6, 1.55f, -11)),
            patch,
        ])).Mount();
        Native3DScene scene = SceneGeometry3D.BuildScene(authored.Project());
        var clear = new NativeFrameClearColor(.44f, .63f, .77f, 1);
        Vector3 eye = new(8, 4.7f, 17);
        Matrix4x4 camera = Camera(eye, new(-2, -.25f, -8));
        const float time = 4.3f;
        const float next = 5.1f;
        byte[] baseline = Capture("analytic-water", profile with { Ocean = null }, time);
        byte[] ocean = Capture("ocean", profile, time);
        Capture("ocean-without-detail", profile with { DetailAmplitude = 0 }, time);
        byte[] withoutFoam = Capture("ocean-without-whitecaps", profile with { CrestFoamStrength = 0 }, time);
        Render(profile, time, false);
        byte[] moving = Capture("ocean-later", profile, next);
        var (coefficients, surface, displacement) = renderer.ReadOceanForQualification();
        var (maximumDisplacementError, maximumFoamError, imaginaryResidual, crestFraction) =
            MeasureField(coefficients, surface, displacement, spectrum, next, time);
        int spectralPixels = WaterGraphicsProof.Changed(baseline, ocean);
        int animationPixels = WaterGraphicsProof.Changed(ocean, moving);
        int foamPixels = WaterGraphicsProof.Changed(ocean, withoutFoam);
        WaterGraphicsProof.Require(spectralPixels > 20_000 && animationPixels > 20_000 && foamPixels > 2_000,
            $"Rendered FFT or crest foam response too small: {spectralPixels}, {animationPixels}, {foamPixels}.");
        byte[] rewind = Capture("ocean-rewind", profile, time);
        WaterGraphicsProof.Require(ocean.SequenceEqual(rewind), "Rewinding did not reproduce the spectral frame.");
        Capture("crest-mask", profile with { DebugView = WaterDebugView3D.CrestFoam }, time);
        var calm = profile with { Ocean = spectrum with { Choppiness = 0 } };
        byte[] zeroChop = Capture("no-chop", calm, time);
        byte[] zeroChopNoFoam = Capture("no-chop-foam-disabled", calm with { CrestFoamStrength = 0 }, time);
        WaterGraphicsProof.Require(zeroChop.SequenceEqual(zeroChopNoFoam), "Without horizontal compression the sea should produce no whitecaps.");
        WaterGraphicsProof.Require(renderer.ReadOceanForQualification().Surface.All(pixel => pixel.W == 0), "The zero-chop crest mask was nonzero.");
        eye = new(10, 17, 24);
        camera = Camera(eye, new(0, 0, -7));
        Capture("ocean-overlook", profile, time);
        var baselineTimes = new List<double>();
        var spectralTimes = new List<double>();
        var baselineFrames = new List<double>();
        var spectralFrames = new List<double>();
        for (int i = 0; i < 28; i++)
        {
            float sample = time + i / 60f;
            Measure(profile with { Ocean = null }, sample, i, baselineTimes, baselineFrames);
        }
        for (int i = 0; i < 28; i++)
            Measure(profile, time + i / 60f, i, spectralTimes, spectralFrames);

        renderer.FrameMode = Native3DFrameMode.Interactive;
        for (int i = 0; i < 18; i++)
        {
            var water = i is >= 6 and < 12 ? calm : profile;
            Render(water, next + i / 60f, false);
        }
        byte[] queued = Capture("ocean-in-flight", profile, next + .3f);
        renderer.FrameMode = Native3DFrameMode.Capture;
        renderer.ResetTemporalHistory();
        byte[] synchronous = Render(profile, next + .3f, true).Pixels!;
        WaterGraphicsProof.Require(queued.SequenceEqual(synchronous), "Queued sea-state changes corrupted the ocean atlas.");
        validation.RequireClean();

        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            Resolution = Size,
            OceanResourcePayloadBytes = Size * Size * (8 + 3 * 2 * 16) + 64,
            WaterTriangles = patch.Indices.Length / 3,
            SpectralChangedPixels = spectralPixels,
            AnimationChangedPixels = animationPixels,
            FoamChangedPixels = foamPixels,
            MaximumDisplacementErrorMetres = maximumDisplacementError,
            MaximumFoamError = maximumFoamError,
            MaximumImaginaryResidual = imaginaryResidual,
            CrestFraction = crestFraction,
            RewindExact = true,
            ZeroChopFoamExact = true,
            InFlightProfileChangeExact = true,
            MatchedFramesPerPath = spectralTimes.Count,
            AnalyticRefractionGpu = WaterGraphicsProof.Distribution(baselineTimes),
            SpectralRefractionGpu = WaterGraphicsProof.Distribution(spectralTimes),
            AnalyticFrameGpu = WaterGraphicsProof.Distribution(baselineFrames),
            SpectralFrameGpu = WaterGraphicsProof.Distribution(spectralFrames),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_OCEAN_GRAPHICS_PASSED " + evidence);

        Native3DFrameResult Render(WaterSurface3D water, float seconds, bool capture)
        {
            var batches = scene.Models.Select(batch => batch.Material.Water is null ? batch : batch with
            {
                Material = batch.Material with { Water = water },
            }).ToArray();
            return renderer.Render(scene with { Models = batches, WaterFrame = new(seconds) }, camera, eye, clear, capture);
        }

        byte[] Capture(string name, WaterSurface3D water, float seconds)
        {
            byte[] pixels = Render(water, seconds, true).Pixels!;
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), Width, Height, pixels);
            return pixels;
        }

        void Measure(WaterSurface3D water, float seconds, int index, List<double> refraction, List<double> frame)
        {
            var result = Render(water, seconds, false);
            if (index < 8)
                return;
            refraction.Add(result.GpuPassTimes.Single(item => item.Pass == "refraction").Milliseconds);
            frame.Add(result.CompletedGpuFrame!.ElapsedMilliseconds);
        }
    }

    private static (double DisplacementError, double FoamError, float Imaginary, double CrestFraction) MeasureField(
        Vector2[] coefficients, Vector4[] surface, Vector4[] displacement, OceanSpectrum3D profile, float current, float previous)
    {
        double displacementError = 0;
        double foamError = 0;
        int foamyTexels = 0;
        for (int plane = 0; plane < 2; plane++)
        {
            for (int z = 0; z < Size; z += 9)
            {
                for (int x = 0; x < Size; x += 7)
                {
                    Vector3 expected = ReferenceDft(coefficients, profile, x, z, plane == 0 ? current : previous);
                    Vector4 sample = surface[plane * Size * Size + z * Size + x];
                    displacementError = Math.Max(displacementError, Vector3.Distance(expected, new(sample.X, sample.Y, sample.Z)));
                }
            }
        }
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector4 derX = (At(x + 1, z) - At(x - 1, z)) * (Size / (2 * profile.TileLength));
                Vector4 derZ = (At(x, z + 1) - At(x, z - 1)) * (Size / (2 * profile.TileLength));
                double determinant = (1 + derX.X) * (1 + derZ.Z) - derX.Z * derZ.X;
                double amount = Math.Clamp((.64 - determinant) / .54, 0, 1);
                double expected = amount * amount * (3 - 2 * amount);
                double actual = surface[z * Size + x].W;
                foamError = Math.Max(foamError, Math.Abs(expected - actual));
                if (actual > .1) foamyTexels++;
            }
        }
        float imaginary = displacement.Max(value => Math.Abs(value.W));
        double crestFraction = (double)foamyTexels / (Size * Size);
        WaterGraphicsProof.Require(displacementError < .0001 && foamError < .00001 && imaginary < .00001
            && crestFraction is > .01 and < .65,
            $"Spectral field failed CPU reference: displacement {displacementError}, foam {foamError}, residual {imaginary}, coverage {crestFraction}.");
        return (displacementError, foamError, imaginary, crestFraction);

        Vector4 At(int x, int z) => displacement[((z + Size) % Size) * Size + (x + Size) % Size];
    }

    private static Vector3 ReferenceDft(Vector2[] coefficients, OceanSpectrum3D profile, int x, int z, double time)
    {
        // Sum travelling waves directly in double precision. No FFT, packed real
        // transform, Hermitian pairing, workgroup ordering, or GPU twiddles.
        double height = 0, dx = 0, dz = 0;
        for (int j = 0; j < Size; j++)
        {
            int modeZ = j < Size / 2 ? j : j - Size;
            for (int i = 0; i < Size; i++)
            {
                int modeX = i < Size / 2 ? i : i - Size;
                double modeLength = Math.Sqrt(modeX * modeX + modeZ * modeZ);
                if (modeLength == 0) continue;
                double omega = Math.Sqrt(9.81 * Math.Tau * modeLength / profile.TileLength);
                double phase = Math.Tau * (modeX * x + modeZ * z) / Size - omega * time;
                Vector2 h0 = coefficients[j * Size + i];
                double cosine = Math.Cos(phase);
                double sine = Math.Sin(phase);
                height += 2 * (h0.X * cosine - h0.Y * sine);
                double lateral = -2 * profile.Choppiness * (h0.X * sine + h0.Y * cosine) / modeLength;
                dx += lateral * modeX;
                dz += lateral * modeZ;
            }
        }
        return new((float)dx, (float)height, (float)dz);
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 target)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.78f, (float)Width / Height, .14f, 160);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }
}
