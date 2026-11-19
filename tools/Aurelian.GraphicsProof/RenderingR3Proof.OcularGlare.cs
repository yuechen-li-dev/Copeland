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
    private static readonly NativeFrameClearColor GlareBlack = new(0, 0, 0, 1);
    private static readonly double[] GlareRadii = [.5, 1.5, 3, 6, 12, 24, 48, 96];
    private static readonly (int Width, int Height, int Field, bool Perspective)[] GlareViews =
        [(321, 181, 50, false), (640, 360, 38, true), (640, 360, 72, true)];

    public static void RunOcularGlare(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "R7 ocular glare"));
        Require(initialized.Success, "R7 ocular glare Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var impulse = new List<object>();
        var pairs = new List<object>();
        foreach (var (width, height, degrees, perspective) in GlareViews)
        {
            string name = $"point-{width}x{height}-{degrees}";
            impulse.Add(ProveGlareImpulse(plant, assets, output, name, width, height, degrees, perspective));
            foreach (string mode in new[] { "bloom", "ocular" })
            {
                string pair = name + "-" + mode;
                pairs.Add(new { Name = pair, Reference = name + "-reference.png", Test = pair + ".png", Group = pair });
            }
        }
        const string identity = "point-321x181-50-reference.png";
        pairs.Add(new { Name = "identity", Reference = identity, Test = identity, Group = "identity" });
        var beacon = ProveBeaconGlare(plant, assets, output);
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "ocular-glare", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Impulse = impulse, Beacon = beacon,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R7_OCULAR_GLARE_PASSED " + output);
    }

    private static object ProveGlareImpulse(AurelianVulkanPlant plant, GameAssets assets, string output, string name, int width,
        int height, int field, bool perspective)
    {
        using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), ocularGlareProgram: assets.Shader("OcularGlare3D.v.ts"));
        var baseline = Graphics3DSettings.Basic with { SunDirection = Vector3.UnitZ, ToneMapping = true };
        var ocular = new OcularGlare3D();
        var bloom = baseline with { BloomIntensity = Graphics3DSettings.Default.BloomIntensity };
        var glare = bloom with { OcularGlare = ocular };
        Matrix4x4 camera = perspective
            ? Matrix4x4.CreatePerspectiveFieldOfView(field * MathF.PI / 180, (float)width / height, .1f, 10)
            : Matrix4x4.Identity;
        var point = GlarePatch(width, height, width / 2, height / 2, 1, 1);

        ReferenceImage3D Capture(Native3DVertex[] geometry, Graphics3DSettings settings, string? label = null)
        {
            renderer.Settings = settings;
            if (perspective)
            {
                // Project the same one-pixel emitter for both fields of view.
                geometry = geometry.Select(vertex => vertex with
                {
                    Position = new(vertex.Position.X / camera.M11, vertex.Position.Y / camera.M22, -1),
                }).ToArray();
            }
            var frame = renderer.Render(geometry, camera, GlareBlack, capture: label is not null);
            if (label is not null)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, name + "-" + label + ".png"), width, height, frame.Pixels!);
            }
            var optical = renderer.ReadOpticalOutputForQualification();
            return optical.Bloom is null ? optical.Color : ComposeBloomReference(optical.Color, optical.Bloom, settings.BloomIntensity);
        }

        var source = Capture(point, baseline, "source");
        var legacy = Capture(point, bloom, "bloom");
        var scattered = Capture(point, glare, "ocular");
        int peak = FindGlarePeak(source);
        var energy = ChannelEnergy(source);
        Require(energy.All(value => value > 1), "Optical impulse was absent from the rendered input.");
        double focal = .5 * height / Math.Tan(field * Math.PI / 360);
        var expected = SpencerReference(source, peak, focal);
        var expectedDisplay = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), expected);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-reference.png"), width, height, expectedDisplay);
        var expectedProfile = GlareProfile(expected, peak, energy[0]);
        var legacyProfile = GlareProfile(legacy, peak, energy[0]);
        var ocularProfile = GlareProfile(scattered, peak, energy[0]);
        double legacyError = ProfileError(legacyProfile, expectedProfile);
        double ocularError = ProfileError(ocularProfile, expectedProfile);
        var ocularEnergy = ChannelEnergy(scattered).Zip(energy, (filtered, original) => filtered / original).ToArray();
        var legacyEnergy = ChannelEnergy(legacy).Zip(energy, (filtered, original) => filtered / original).ToArray();
        double expectedRatio = ChannelEnergy(expected)[0] / energy[0];
        double normalizedLegacyError = NormalizedError(legacyProfile, legacyEnergy[0], expectedProfile, expectedRatio);
        double normalizedOcularError = NormalizedError(ocularProfile, ocularEnergy[0], expectedProfile, expectedRatio);
        Require(ocularEnergy.All(value => value is > .8 and <= 1.002), "Ocular scatter created energy or lost its core.");
        Require(legacyEnergy[0] > 1.04 && ocularError < legacyError * .45,
            "Ocular scatter did not improve the spatial PSF against the existing bloom.");
        Require(normalizedOcularError < normalizedLegacyError * .5, "Ocular PSF did not improve after equalizing output energy.");

        var edge = GlarePatch(width, height, 0, 0, 1, 1);
        var edgeSource = ChannelEnergy(Capture(edge, baseline));
        var edgeOutput = ChannelEnergy(Capture(edge, glare, "edge"));
        var edgeRatio = edgeOutput.Zip(edgeSource, (filtered, original) => filtered / original).ToArray();
        Require(edgeRatio.All(value => value > 0 && value <= 1.002) && edgeRatio[0] < ocularEnergy[0],
            "Scatter at the corner did not leave the visible field.");

        var fieldGeometry = GlarePatch(width, height, 0, 0, width, height, new(.5f, .25f, .125f, 1));
        var flat = Capture(fieldGeometry, glare);
        int center = (height / 2 * width + width / 2) * 3;
        double flatCenter = flat.LinearRgb[center] / .5;
        double flatMaximum = flat.LinearRgb.Where((_, index) => index % 3 == 0).Max() / .5;
        Require(flatCenter is > .87 and <= 1.002 && flatMaximum <= 1.002, "A uniform field gained energy or dulled excessively.");

        double maxOffsetEnergyDrift = 0;
        double maxOffsetProfileError = ocularError;
        for (int step = 1; step <= 4; step++)
        {
            var moved = GlarePatch(width, height, width / 2 + step, height / 2 + step, 1, 1);
            var movedSource = Capture(moved, baseline);
            var movedGlare = Capture(moved, glare);
            double inputRed = ChannelEnergy(movedSource)[0];
            double outputRed = ChannelEnergy(movedGlare)[0];
            var movedProfile = GlareProfile(movedGlare, FindGlarePeak(movedSource), inputRed);
            maxOffsetEnergyDrift = Math.Max(maxOffsetEnergyDrift, Math.Abs(outputRed / inputRed - ocularEnergy[0]));
            maxOffsetProfileError = Math.Max(maxOffsetProfileError, ProfileError(movedProfile, expectedProfile));
        }
        Require(maxOffsetEnergyDrift < .025 && maxOffsetProfileError < legacyError * .55, "Moving the source broke the angular profile.");
        Console.WriteLine($"R7 {name}: energy ocular {ocularEnergy[0]:P2}, bloom {legacyEnergy[0]:P2}; "
            + $"equal-energy profile MAE ocular {normalizedOcularError:F4}, bloom {normalizedLegacyError:F4}.");
        return new
        {
            Name = name, Width = width, Height = height, VerticalDegrees = field, Perspective = perspective, RadiiPixels = GlareRadii,
            ExpectedProfile = expectedProfile, LegacyProfile = legacyProfile, OcularProfile = ocularProfile,
            LegacyProfileMae = legacyError, OcularProfileMae = ocularError, OcularEnergy = ocularEnergy,
            EqualEnergyLegacyProfileMae = normalizedLegacyError, EqualEnergyOcularProfileMae = normalizedOcularError,
            LegacyEnergy = legacyEnergy, CornerEnergy = edgeRatio, UniformCenterGain = flatCenter, UniformPeakGain = flatMaximum,
            MaximumOffsetEnergyDrift = maxOffsetEnergyDrift, MaximumOffsetProfileMae = maxOffsetProfileError,
        };
    }

    private static object ProveBeaconGlare(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        const int width = 1920;
        const int height = 1080;
        using var game = new BeaconGame();
        var scene = new BeaconResidentScene3D().Build(game, includeViewmodel: false);
        using var state = new VulkanEyeExposureState3D(plant);
        var eye = new EyeExposurePrograms(assets.ComputeShader("EyeHistogram3D.v.ts"), assets.ComputeShader("EyeAdapt3D.v.ts"),
            assets.Shader("EyeToneMap3D.v.ts"), state);
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, eyeExposure: eye, ocularGlare: true);
        renderer.EnableVisibilityBuffer = true;
        var settings = Graphics3DSettings.Default with
        {
            SunIntensity = .65f, SkyAmbient = new(.025f), GroundAmbient = new(.015f),
        };
        var camera = new ReferenceCamera3D(new(0, 2.5f, 7), new(.5f, 4.6f, -3), Vector3.UnitY, .86f);
        Matrix4x4 clip = RegionalMatrix(camera, width, height);
        var controls = new[] { ("direct", settings with { BloomIntensity = 0 }), ("bloom", settings),
            ("ocular", settings with { OcularGlare = new() }), ("bloom-repeat", settings) };
        var evidence = new List<object>();
        var medians = new Dictionary<string, double>();
        var captureHashes = new Dictionary<string, string?>();
        foreach (var (name, options) in controls)
        {
            renderer.Settings = options;
            renderer.ResetTemporalHistory();
            var times = new List<double>();
            var wholeTimes = new List<double>();
            for (int frame = 0; frame < 80; frame++)
            {
                var result = renderer.Render(scene, clip, camera.Eye, GlareBlack, capture: frame == 79);
                if (frame >= 32)
                {
                    string pass = name == "ocular" ? "ocular-glare" : "bloom";
                    times.Add(result.GpuPassTimes.Single(timing => timing.Pass == pass).Milliseconds);
                    wholeTimes.Add(result.CompletedGpuFrame!.ElapsedMilliseconds);
                }
                if (frame == 79)
                {
                    captureHashes.Add(name, result.PixelSha256);
                    NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-{name}.png"), width, height, result.Pixels!);
                }
            }
            double median = times.Order().ElementAt(times.Count / 2);
            double full = wholeTimes.Order().ElementAt(wholeTimes.Count / 2);
            medians.Add(name, median);
            evidence.Add(new { Name = name, MedianOpticalGpuMilliseconds = median, MedianWholeGpuMilliseconds = full, Frames = times });
            Console.WriteLine($"R7 Beacon {name}: optics {median:F3} ms, GPU frame {full:F3} ms.");
        }
        EyeExposureMeasurement Metered(Graphics3DSettings options)
        {
            renderer.Settings = options with { EyeExposure = new() { CompensationEv = -6, MinimumEv = -12, MaximumEv = 12 } };
            renderer.ResetTemporalHistory();
            renderer.Render(scene, clip, camera.Eye, GlareBlack);
            return renderer.ReadEyeExposureForQualification();
        }
        var bloomMeter = Metered(settings);
        var ocularMeter = Metered(settings with { OcularGlare = new() });
        Require(Math.Abs(bloomMeter.TargetEv) < 12 && Math.Abs(bloomMeter.TargetEv - ocularMeter.TargetEv) < .0001f
            && Math.Abs(bloomMeter.MeteredLogLuminance - ocularMeter.MeteredLogLuminance) < .0001f,
            "Eye metering picked up optical scatter.");
        Require(medians["ocular"] < 2.5, "Ocular scatter exceeded the 1080p qualification budget.");
        Require(captureHashes["bloom"] == captureHashes["bloom-repeat"], "Bloom changed after switching the optical mode.");
        return new { Width = width, Height = height, Trials = evidence, BloomMeter = bloomMeter, OcularMeter = ocularMeter, CaptureHashes = captureHashes };
    }

    private static Native3DVertex[] GlarePatch(int width, int height, int left, int bottom, int countX, int countY, Vector4? color = null)
    {
        float x0 = 2f * left / width - 1;
        float x1 = 2f * (left + countX) / width - 1;
        float y0 = 2f * bottom / height - 1;
        float y1 = 2f * (bottom + countY) / height - 1;
        Vector3 a = new(x0, y0, .5f);
        Vector3 b = new(x1, y0, .5f);
        Vector3 c = new(x1, y1, .5f);
        Vector3 d = new(x0, y1, .5f);
        Vector4 emitted = color ?? new(48, 32, 16, 1);
        return new[] { a, b, c, a, c, d }.Select(position => new Native3DVertex(position, Vector3.UnitZ, emitted)).ToArray();
    }

    private static ReferenceImage3D ComposeBloomReference(ReferenceImage3D scene, ReferenceImage3D bloom, float intensity)
    {
        float[] combined = (float[])scene.LinearRgb.Clone();
        for (int y = 0; y < scene.Height; y++)
        {
            float lowY = Math.Clamp((y + .5f) * bloom.Height / scene.Height - .5f, 0, bloom.Height - 1);
            int y0 = (int)lowY;
            int y1 = Math.Min(y0 + 1, bloom.Height - 1);
            for (int x = 0; x < scene.Width; x++)
            {
                float lowX = Math.Clamp((x + .5f) * bloom.Width / scene.Width - .5f, 0, bloom.Width - 1);
                int x0 = (int)lowX;
                int x1 = Math.Min(x0 + 1, bloom.Width - 1);
                for (int channel = 0; channel < 3; channel++)
                {
                    float a = bloom.LinearRgb[(y0 * bloom.Width + x0) * 3 + channel];
                    float b = bloom.LinearRgb[(y0 * bloom.Width + x1) * 3 + channel];
                    float c = bloom.LinearRgb[(y1 * bloom.Width + x0) * 3 + channel];
                    float d = bloom.LinearRgb[(y1 * bloom.Width + x1) * 3 + channel];
                    float upper = a + (b - a) * (lowX - x0);
                    float lower = c + (d - c) * (lowX - x0);
                    combined[(y * scene.Width + x) * 3 + channel] += intensity * (upper + (lower - upper) * (lowY - y0));
                }
            }
        }
        return scene with { LinearRgb = combined };
    }

    private static ReferenceImage3D SpencerReference(ReferenceImage3D source, int peak, double focal)
    {
        const int rings = 50_000;
        double delta = (Math.PI / 2) / rings;
        double near = 0;
        double far = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            double theta = (ring + .5) * delta;
            double shift = theta * 180 / Math.PI + .02;
            double area = 2 * Math.PI * Math.Sin(theta) * delta;
            far += area * 72.37 / (shift * shift);
            near += area * 20.91 / (shift * shift * shift);
        }
        float[] result = new float[source.LinearRgb.Length];
        int centerX = peak % source.Width;
        int centerY = peak / source.Width;
        for (int py = 0; py < source.Height; py++)
        {
            for (int px = 0; px < source.Width; px++)
            {
                int x = px - centerX;
                int y = py - centerY;
                int samples = Math.Abs(x) <= 1 && Math.Abs(y) <= 1 ? 64 : 4;
                double mass = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double nx = (x + (sx + .5) / samples - .5) / focal;
                        double ny = (y + (sy + .5) / samples - .5) / focal;
                        double r2 = nx * nx + ny * ny;
                        double angle = Math.Atan(Math.Sqrt(r2)) * 180 / Math.PI;
                        double area = 1 / (focal * focal * samples * samples * Math.Pow(1 + r2, 1.5));
                        double shift = angle + .02;
                        mass += area * (.478 * 20.91 / (near * shift * shift * shift)
                            + .138 * 72.37 / (far * shift * shift));
                    }
                }
                int pixel = py * source.Width + px;
                if (pixel == peak) mass += .384;
                for (int channel = 0; channel < 3; channel++)
                {
                    result[pixel * 3 + channel] = (float)(source.LinearRgb[peak * 3 + channel] * mass);
                }
            }
        }
        return source with { LinearRgb = result };
    }

    private static double[] ChannelEnergy(ReferenceImage3D image)
    {
        var energy = new double[3];
        for (int index = 0; index < image.LinearRgb.Length; index++)
        {
            float value = image.LinearRgb[index];
            Require(float.IsFinite(value) && value >= 0, "Nonfinite or negative HDR optical sample.");
            energy[index % 3] += value;
        }
        return energy;
    }

    private static int FindGlarePeak(ReferenceImage3D image)
    {
        int peak = 0;
        for (int index = 3; index < image.LinearRgb.Length; index += 3)
        {
            if (image.LinearRgb[index] > image.LinearRgb[peak]) peak = index;
        }
        return peak / 3;
    }

    private static double[] GlareProfile(ReferenceImage3D image, int peak, double sourceEnergy)
    {
        var profile = new double[GlareRadii.Length];
        int centerX = peak % image.Width;
        int centerY = peak / image.Width;
        for (int index = 0; index < image.LinearRgb.Length / 3; index++)
        {
            int x = index % image.Width - centerX;
            int y = index / image.Width - centerY;
            double r2 = x * x + y * y;
            for (int bin = 0; bin < profile.Length; bin++)
            {
                if (r2 < GlareRadii[bin] * GlareRadii[bin]) profile[bin] += image.LinearRgb[index * 3] / sourceEnergy;
            }
        }
        return profile;
    }

    private static double ProfileError(double[] value, double[] expected) =>
        value.Zip(expected, (actual, reference) => Math.Abs(actual - reference)).Average();

    private static double NormalizedError(double[] value, double total, double[] expected, double expectedTotal) =>
        value.Zip(expected, (actual, reference) => Math.Abs(actual / total - reference / expectedTotal)).Average();
}
