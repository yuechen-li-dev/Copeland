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
    private static readonly Vector3 PhotopicWeights = new(.2126f, .7152f, .0722f);

    public static void RunLowLightVision(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "R7 low-light vision"));
        Require(initialized.Success, "R7 Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var state = new VulkanEyeExposureState3D(plant);
        var eye = new EyeExposurePrograms(assets.ComputeShader("EyeHistogram3D.v.ts"), assets.ComputeShader("EyeAdapt3D.v.ts"),
            assets.Shader("EyeToneMap3D.v.ts"), state);
        var patches = ProveRodConePatches(plant, assets, eye, output);
        var wetNight = RainNightDemo.CompareLowLight(plant, assets, eye, output);
        var beacon = ProveLowLightBeacon(plant, assets, eye, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Patches = patches, WetNight = wetNight, Beacon = beacon,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R7_LOW_LIGHT_PASSED " + output);
    }

    private static object ProveRodConePatches(AurelianVulkanPlant plant, GameAssets assets, EyeExposurePrograms eye, string output)
    {
        const int width = 480;
        const int height = 240;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"),
            eyeExposurePrograms: eye);
        var fixedGain = new EyeExposure3D { MinimumEv = 0, MaximumEv = 0 };
        var lowLight = fixedGain with { LowLight = new() };
        var settings = Graphics3DSettings.Basic with { SunDirection = Vector3.UnitZ };
        var red = new Vector3(1, .025f, .02f);
        var cyan = new Vector3(.025f, .65f, .8f);
        var gold = new Vector3(.68f, .44f, .055f);

        Native3DVertex[] Patches(float luminance) =>
        [
            .. EyeRectangle(-1, -.5f, red * (luminance / Vector3.Dot(red, PhotopicWeights))),
            .. EyeRectangle(-.5f, 0, cyan * (luminance / Vector3.Dot(cyan, PhotopicWeights))),
            .. EyeRectangle(0, .5f, gold * (luminance / Vector3.Dot(gold, PhotopicWeights))),
            .. EyeRectangle(.5f, 1, luminance),
        ];

        (PatchColor[] Colors, EyeExposureMeasurement Meter, string? Hash) Render(Native3DVertex[] geometry, string label)
        {
            var result = renderer.Render(geometry, Matrix4x4.Identity, GlareBlack, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, $"patches-{label}.png"), width, height, result.Pixels!);
            var measured = Enumerable.Range(0, 4)
                .Select(index => SamplePatch(result.Pixels!, width, width * (2 * index + 1) / 8, height / 2)).ToArray();
            return (measured, renderer.ReadEyeExposureForQualification(), result.PixelSha256);
        }

        var levels = new List<object>();
        foreach (var (name, luminance) in new[] { ("photopic", .18f), ("mesopic", .040f), ("scotopic", .007f) })
        {
            var geometry = Patches(luminance);
            renderer.Settings = settings with { EyeExposure = fixedGain };
            renderer.ResetTemporalHistory();
            var baseline = Render(geometry, name + "-control");
            renderer.Settings = settings with { EyeExposure = lowLight };
            renderer.ResetTemporalHistory();
            var modeled = Render(geometry, name + "-rod-cone");
            double baselineRatio = baseline.Colors[1].Luminance / baseline.Colors[0].Luminance;
            double modeledRatio = modeled.Colors[1].Luminance / modeled.Colors[0].Luminance;
            double saturation = modeled.Colors.Take(3).Average(color => color.Chroma)
                / baseline.Colors.Take(3).Average(color => color.Chroma);
            Require(Math.Abs(baselineRatio - 1) < .04, "Vulkan input patches were not isoluminant.");
            Require(modeled.Meter == baseline.Meter && modeled.Meter.AppliedEv == 0, "Rod–cone presentation changed metering or gain.");
            Require(Math.Abs(modeled.Colors[3].Luminance - baseline.Colors[3].Luminance) < .0001,
                "The neutral patch shifted in luminance.");
            if (name == "photopic")
            {
                Require(modeled.Hash == baseline.Hash, "Rod–cone presentation changed daylight.");
            }
            else if (name == "scotopic")
            {
                Require(modeledRatio > 1.35 && saturation < .65, "Deep night failed the Purkinje shift or desaturation check.");
            }
            else
            {
                Require(modeledRatio > 1.1 && saturation < .95, "Mesopic patches did not enter the rod–cone transition.");
            }
            Console.WriteLine($"R7 {name} cyan/red {baselineRatio:F3} → {modeledRatio:F3}, chroma retention {saturation:P1}.");
            levels.Add(new { Name = name, Baseline = baseline.Colors, Modeled = modeled.Colors, BaselineRatio = baselineRatio,
                ModeledRatio = modeledRatio, ChromaRetention = saturation, Meter = modeled.Meter });
        }

        renderer.ResetTemporalHistory();
        renderer.Render(Patches(.18f), Matrix4x4.Identity, GlareBlack);
        var daylight = renderer.ReadEyeExposureForQualification();
        var dark = Patches(.007f);
        var firstDark = Render(dark, "transition-first");
        for (int frame = 1; frame < 150; frame++)
        {
            renderer.Render(dark, Matrix4x4.Identity, GlareBlack);
        }
        var adaptedDark = Render(dark, "transition-adapted");
        double expected = firstDark.Meter.MeteredLogLuminance
            + (daylight.AdaptedLogLuminance - firstDark.Meter.MeteredLogLuminance)
            * Math.Exp(-151 * renderer.EyeExposureElapsedSeconds / fixedGain.DarkTimeConstant);
        Require(firstDark.Meter.AdaptedLogLuminance > firstDark.Meter.MeteredLogLuminance + 3
            && Math.Abs(adaptedDark.Meter.AdaptedLogLuminance - expected) < .0001
            && adaptedDark.Meter.AppliedEv == 0
            && adaptedDark.Colors[1].Luminance / adaptedDark.Colors[0].Luminance > 1.3,
            "Rod adaptation did not evolve independently of fixed exposure.");
        return new { Levels = levels, Daylight = daylight, FirstDark = firstDark.Meter, AdaptedDark = adaptedDark.Meter };
    }

    private static object ProveLowLightBeacon(AurelianVulkanPlant plant, GameAssets assets, EyeExposurePrograms eye, string output)
    {
        const int width = 960;
        const int height = 540;
        using var game = new BeaconGame();
        var room = BuildEyeRoom(game);
        var box = room.Instances.First(instance => instance.Mesh.Identity == "beacon/unit-box").Mesh;
        Vector3 warning = new(.83f, .81f, 7.55f);
        Vector3 guide = new(-.83f, .81f, 7.55f);

        SceneInstance3D Cue(string name, Vector3 center, Vector3 scale, Vector3 color) => new("r7/cue-" + name, box,
            new ModelMaterial("r7/cue-" + name) { BaseColor = new(color, 1), Unlit = true, DoubleSided = true },
            Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center));

        var scene = room with
        {
            Instances = new NativeInstanceSet3D([
                .. room.Instances,
                Cue("warning-background", warning + new Vector3(0, 0, -.035f), new(.18f, .20f, .02f), new(.0015f)),
                Cue("guide-background", guide + new Vector3(0, 0, -.035f), new(.18f, .20f, .02f), new(.0015f)),
                Cue("warning", warning, new(.075f, .13f, .02f), new(.085f, .003f, .001f)),
                Cue("guide", guide, new(.11f, .065f, .02f), new(.001f, .027f, .033f)),
            ]),
        };
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, eyeExposure: eye, ocularGlare: true);
        renderer.EnableVisibilityBuffer = true;
        var settings = Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new(.58f, .78f, .24f)),
            SunIntensity = 8, SkyAmbient = new(.018f), GroundAmbient = new(.009f), AmbientOcclusionStrength = 0,
            EyeExposure = new(), OcularGlare = new() { Strength = .35f },
        };
        var control = Route("control", settings);
        var modeled = Route("rod-cone", settings with { EyeExposure = settings.EyeExposure with { LowLight = new() } });
        Require(control.Frames.Zip(modeled.Frames).All(pair => pair.First.Exposure == pair.Second.Exposure),
            "Rod–cone response affected Beacon's exposure history.");
        float contrast = modeled.Frames[0].Exposure.MeteredLogLuminance - modeled.Frames[^1].Exposure.MeteredLogLuminance;
        Require(contrast > 2, "Beacon route did not pass from bright to dark.");
        Require(modeled.Readability.Length >= 2, "Beacon did not exercise both early and adapted cue sightings.");
        foreach (var (baseline, candidate) in control.Readability.Zip(modeled.Readability))
        {
            Require(candidate.WarningContrast > .005f && candidate.GuideContrast > .005f
                && candidate.WarningContrast > baseline.WarningContrast * .60f && candidate.GuideContrast > baseline.GuideContrast * .60f
                && candidate.WarningHue > .04f && candidate.GuideHue > .04f,
                $"Beacon cue readability failed at frame {candidate.Frame}: {candidate} against {baseline}.");
        }
        var first = modeled.Readability[0];
        var last = modeled.Readability[^1];
        Console.WriteLine($"R7 Beacon {contrast:F2} EV transition, adapted cue contrast {last.WarningContrast:F4}/{last.GuideContrast:F4}.");
        Require(Math.Abs(modeled.Frames[^1].Exposure.AdaptedLogLuminance - modeled.Frames[^1].Exposure.MeteredLogLuminance) < .25f,
            "Beacon did not settle near the dark adaptation level.");
        return new { ContrastEv = contrast, Width = width, Height = height, Control = control, RodCone = modeled,
            FirstCueFrame = first.Frame, ControlToneMapMedian = ToneMapMedian(control), RodConeToneMapMedian = ToneMapMedian(modeled) };

        static double ToneMapMedian(LowLightRoute route)
        {
            var times = route.Frames.Skip(16).Select(frame => frame.ToneMapGpuMilliseconds).Order().ToArray();
            return times[times.Length / 2];
        }

        LowLightRoute Route(string name, Graphics3DSettings profile)
        {
            renderer.Settings = profile;
            renderer.ResetTemporalHistory();
            var frames = new List<LowLightFrame>();
            var readings = new List<CueReading>();
            for (int frame = 0; frame <= 360; frame++)
            {
                float walk = Math.Clamp((frame - 20) / 75f, 0, 1);
                var eyePosition = new Vector3(0, 1.42f, 3.5f + walk * 6.8f);
                var view = new ReferenceCamera3D(eyePosition, eyePosition + new Vector3(0, -.30f, -1), Vector3.UnitY, .88f);
                var clip = RegionalMatrix(view, width, height);
                bool capture = frame is 0 or 55 or 95 or 125 or 240 or 360;
                var result = renderer.Render(scene, clip, eyePosition, Clear, capture);
                if (capture)
                {
                    NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-{name}-{frame}.png"), width, height, result.Pixels!);
                }
                if (frame is 95 or 125 or 240 or 360)
                {
                    var warningColor = ProjectPatch(result.Pixels!, clip, warning);
                    var guideColor = ProjectPatch(result.Pixels!, clip, guide);
                    var warningBack = ProjectPatch(result.Pixels!, clip, warning + new Vector3(.12f, 0, -.015f));
                    var guideBack = ProjectPatch(result.Pixels!, clip, guide + new Vector3(0, .12f, -.015f));
                    readings.Add(new(frame, warningColor.Luminance - warningBack.Luminance,
                        guideColor.Luminance - guideBack.Luminance, warningColor.RedBias, -guideColor.RedBias));
                }
                frames.Add(new(frame, renderer.ReadEyeExposureForQualification(),
                    result.GpuPassTimes.Single(timing => timing.Pass == "tone-map-output").Milliseconds));
            }
            return new(frames.ToArray(), readings.ToArray());
        }

        static PatchColor ProjectPatch(byte[] pixels, Matrix4x4 clip, Vector3 center)
        {
            var position = Vector4.Transform(new Vector4(center, 1), clip);
            int x = (int)((position.X / position.W * .5f + .5f) * width);
            int y = (int)((position.Y / position.W * .5f + .5f) * height);
            Require(x >= 2 && x < width - 2 && y >= 2 && y < height - 2, "Beacon cue was outside the viewport.");
            return SamplePatch(pixels, width, x, y);
        }
    }

    private static PatchColor SamplePatch(byte[] pixels, int width, int x, int y)
    {
        var color = Vector3.Zero;
        for (int offsetY = -2; offsetY <= 2; offsetY++)
        {
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                int index = ((y + offsetY) * width + x + offsetX) * 4;
                color += new Vector3((float)ScalingSrgb[pixels[index]], (float)ScalingSrgb[pixels[index + 1]],
                    (float)ScalingSrgb[pixels[index + 2]]);
            }
        }
        color /= 25;
        float luminance = Vector3.Dot(color, PhotopicWeights);
        float chroma = MathF.Max(color.X, MathF.Max(color.Y, color.Z)) - MathF.Min(color.X, MathF.Min(color.Y, color.Z));
        return new(color.X, color.Y, color.Z, luminance, chroma,
            (color.X - .5f * (color.Y + color.Z)) / MathF.Max(color.X + color.Y + color.Z, .0001f));
    }

    private sealed record PatchColor(float Red, float Green, float Blue, float Luminance, float Chroma, float RedBias);
    private sealed record LowLightFrame(int Frame, EyeExposureMeasurement Exposure, double ToneMapGpuMilliseconds);
    private sealed record CueReading(int Frame, float WarningContrast, float GuideContrast, float WarningHue, float GuideHue);
    private sealed record LowLightRoute(LowLightFrame[] Frames, CueReading[] Readability);
}
