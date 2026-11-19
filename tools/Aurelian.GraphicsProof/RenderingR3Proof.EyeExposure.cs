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
    private sealed record EyeFrame(int Frame, EyeExposureMeasurement Eye, double MeterGpuMilliseconds, double? DisplayLuminance);

    public static void RunEyeExposure(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "R7 eye exposure"));
        Require(initialized.Success, "R7 Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var eye = new VulkanEyeExposureState3D(plant);
        var programs = new EyeExposurePrograms(assets.ComputeShader("EyeHistogram3D.v.ts"), assets.ComputeShader("EyeAdapt3D.v.ts"),
            assets.Shader("EyeToneMap3D.v.ts"), eye);
        var step = ProveEyeStep(plant, assets, programs, output);
        var route = ProveEyeRoute(plant, assets, programs, output);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Step = step, Beacon = route,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R7_EYE_EXPOSURE_PASSED " + output);
    }

    private static object ProveEyeStep(AurelianVulkanPlant plant, GameAssets assets, EyeExposurePrograms programs, string output)
    {
        const int width = 480;
        const int height = 270;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, eyeExposure: programs);
        var profile = new EyeExposure3D();
        var settings = Graphics3DSettings.Basic with { EyeExposure = profile, Exposure = 1 };
        renderer.Settings = settings;
        var scene = new Native3DScene(EyeRectangle(2, 3, 1), []);
        float darkSrgb = 1.055f * MathF.Pow(.03125f, 1f / 2.4f) - .055f;
        var dark = new NativeFrameClearColor(darkSrgb, darkSrgb, darkSrgb, 1);
        var light = new NativeFrameClearColor(1, 1, 1, 1);

        EyeFrame Render(VulkanSolid3DRenderer owner, Native3DScene view, NativeFrameClearColor color,
            int frame = 0, string? capture = null)
        {
            var result = owner.Render(view, Matrix4x4.Identity, Vector3.Zero, color, capture is not null);
            if (capture is not null)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, capture + ".png"), width, height, result.Pixels!);
            }
            return new(frame, owner.ReadEyeExposureForQualification(),
                result.GpuPassTimes.Single(pass => pass.Pass == "eye-exposure").Milliseconds,
                result.Pixels is null ? null : EyeDisplayLuminance(result.Pixels, width, height));
        }

        var baseline = Render(renderer, scene, dark, capture: "step-dark");
        var revised = scene with { TemporalRevision = "new geometry correspondence" };
        var brighter = new List<EyeFrame>();
        for (int frame = 1; frame <= 60; frame++)
        {
            brighter.Add(Render(renderer, revised, light, frame, frame is 1 or 12 or 60 ? "step-bright-" + frame : null));
        }
        var darker = new List<EyeFrame>();
        for (int frame = 1; frame <= 120; frame++)
        {
            darker.Add(Render(renderer, scene, dark, frame, frame is 1 or 12 or 120 ? "step-dark-" + frame : null));
        }
        Require(Math.Abs(baseline.Eye.MeteredLogLuminance + 5) < .08f
            && Math.Abs(brighter[^1].Eye.MeteredLogLuminance) < .08f, "Uniform HDR meter disagreed with known luminance.");
        Require(baseline.DisplayLuminance is > .17 and < .19 && brighter[0].DisplayLuminance is > .99
            && brighter[^1].DisplayLuminance is > .17 and < .19, "GPU exposure did not affect the actual tone-mapped frame.");
        float brightError = CheckEyeCurve(baseline.Eye.AppliedEv, brighter, profile.BrightTimeConstant);
        float darkError = CheckEyeCurve(brighter[^1].Eye.AppliedEv, darker, profile.DarkTimeConstant);
        renderer.ResetTemporalHistory();
        var cut = Render(renderer, scene, light, capture: "step-cut");
        Require(Math.Abs(cut.Eye.AppliedEv - cut.Eye.TargetEv) < .00001f, "Camera cut did not reset to current luminance.");

        float RunRate(int fps)
        {
            renderer.ResetTemporalHistory();
            renderer.EyeExposureElapsedSeconds = 1f / fps;
            Render(renderer, scene, dark);
            EyeFrame measured = baseline;
            for (int frame = 0; frame < fps / 2; frame++)
            {
                measured = Render(renderer, scene, light);
            }
            return measured.Eye.AppliedEv;
        }
        float thirty = RunRate(30);
        float oneTwenty = RunRate(120);
        Require(Math.Abs(thirty - oneTwenty) < .0001f, "Adaptation differed between 30 and 120 Hz at the same elapsed time.");
        renderer.EyeExposureElapsedSeconds = 1f / 60;

        var flash = new Native3DScene(EyeRectangle(.64f, 1, 8), []);
        var flashSettings = profile with { PeripheralWeight = .05f };
        renderer.Settings = settings with { EyeExposure = flashSettings };
        renderer.ResetTemporalHistory();
        var spotDark = Render(renderer, scene, dark);
        var spotFlash = Render(renderer, flash, dark, capture: "step-peripheral-flash-masked");
        renderer.Settings = settings with { EyeExposure = flashSettings with { PeripheralWeight = 1 } };
        renderer.ResetTemporalHistory();
        var uniformDark = Render(renderer, scene, dark);
        var uniformFlash = Render(renderer, flash, dark, capture: "step-peripheral-flash-uniform");
        float maskedShift = Math.Abs(spotDark.Eye.AppliedEv - spotFlash.Eye.AppliedEv);
        float uniformShift = Math.Abs(uniformDark.Eye.AppliedEv - uniformFlash.Eye.AppliedEv);
        Console.WriteLine($"R7 flash EV shift: masked {maskedShift:F4}, uniform {uniformShift:F4}.");
        Require(uniformShift > .025f && maskedShift < uniformShift * .35f, "The mask did not protect against peripheral flash pumping.");

        renderer.Settings = settings;
        renderer.ResetTemporalHistory();
        var beforeScale = Render(renderer, scene, dark);
        using var scaledTarget = new VulkanNativeFrameTarget(plant, width, height);
        using var scaled = RenderingBindlessProof.CreateRenderer(plant, assets, scaledTarget, renderScale: RenderScale3D.Half,
            eyeExposure: programs);
        scaled.Settings = settings;
        scaled.EyeExposureElapsedSeconds = 0;
        scaled.ResetTemporalHistory(resetEyeExposure: false);
        var afterScale = Render(scaled, scene, light, capture: "step-scale-change");
        Require(Math.Abs(afterScale.Eye.AppliedEv - beforeScale.Eye.AppliedEv) < .00001f
            && afterScale.Eye.TargetEv + 4 < beforeScale.Eye.TargetEv, "Render scale change discarded exposure history.");
        renderer.FrameMode = scaled.FrameMode = Native3DFrameMode.Interactive;
        scaled.EyeExposureElapsedSeconds = renderer.EyeExposureElapsedSeconds;
        for (int frame = 0; frame < 24; frame++)
        {
            var active = frame % 2 == 0 ? renderer : scaled;
            active.Render(scene, Matrix4x4.Identity, Vector3.Zero, light);
        }
        var queued = scaled.ReadEyeExposureForQualification();
        float queuedExpected = afterScale.Eye.TargetEv + (beforeScale.Eye.AppliedEv - afterScale.Eye.TargetEv)
            * MathF.Exp(-24 * scaled.EyeExposureElapsedSeconds / profile.BrightTimeConstant);
        Require(Math.Abs(queued.AppliedEv - queuedExpected) < .0005f, "In-flight scale changes lost adaptation steps.");
        renderer.FrameMode = Native3DFrameMode.Capture;

        var manual = settings with { EyeExposure = null, Exposure = 1.5f };
        renderer.Settings = manual;
        var reference = renderer.Render(scene, Matrix4x4.Identity, Vector3.Zero, dark, capture: true);
        using var manualRenderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        manualRenderer.Settings = manual;
        var control = manualRenderer.Render(scene, Matrix4x4.Identity, Vector3.Zero, dark, capture: true);
        Require(reference.PixelSha256 == control.PixelSha256, "Manual override changed display pixels.");
        return new
        {
            Profile = profile, Baseline = baseline, Bright = brighter, Dark = darker, BrightCurveMaxErrorEv = brightError,
            DarkCurveMaxErrorEv = darkError, Cut = cut, At30Hz = thirty, At120Hz = oneTwenty, MaskedFlashShiftEv = maskedShift,
            UniformFlashShiftEv = uniformShift, BeforeScale = beforeScale, AfterScale = afterScale, Queued = queued, ManualMatches = true,
        };
    }

    private static float CheckEyeCurve(float initial, IReadOnlyList<EyeFrame> frames, float tau)
    {
        float maximum = 0;
        foreach (var frame in frames)
        {
            float target = frames[0].Eye.TargetEv;
            float expected = target + (initial - target) * MathF.Exp(-frame.Frame / (60f * tau));
            maximum = Math.Max(maximum, Math.Abs(frame.Eye.AppliedEv - expected));
        }
        Console.WriteLine($"R7 response tau {tau:F2}s: maximum error {maximum:F6} EV.");
        Require(maximum < .0005f, "GPU temporal response did not follow the authored step curve.");
        return maximum;
    }

    private static object ProveEyeRoute(AurelianVulkanPlant plant, GameAssets assets, EyeExposurePrograms programs, string output)
    {
        const int width = 960;
        const int height = 540;
        using var game = new BeaconGame();
        var scene = BuildEyeRoom(game);
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, eyeExposure: programs);
        renderer.EnableVisibilityBuffer = true;
        var settings = Graphics3DSettings.Default with
        {
            SunDirection = Vector3.Normalize(new(.58f, .78f, .24f)),
            SunIntensity = 8,
            SkyAmbient = new(.018f),
            GroundAmbient = new(.009f),
            AmbientOcclusionStrength = 0,
            EyeExposure = new(),
        };
        renderer.Settings = settings;
        renderer.ResetTemporalHistory();
        var route = new List<EyeFrame>();
        for (int frame = 0; frame <= 155; frame++)
        {
            float walk = Math.Clamp((frame - 20) / 75f, 0, 1);
            var eye = new Vector3(0, 1.42f, 10.3f - walk * 6.8f);
            var view = new ReferenceCamera3D(eye, eye + new Vector3(0, -.65f, -1), Vector3.UnitY, .88f);
            Matrix4x4 clip = RegionalMatrix(view, width, height);
            bool capture = frame is 0 or 50 or 75 or 95 or 155;
            var result = renderer.Render(scene, clip, eye, Clear, capture);
            route.Add(new(frame, renderer.ReadEyeExposureForQualification(),
                result.GpuPassTimes.Single(pass => pass.Pass == "eye-exposure").Milliseconds,
                result.Pixels is null ? null : EyeDisplayLuminance(result.Pixels, width, height)));
            if (capture)
            {
                NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-adapted-{frame}.png"), width, height, result.Pixels!);
            }
        }
        renderer.Settings = settings with { EyeExposure = null };
        var manual = new Dictionary<int, double>();
        foreach (int frame in new[] { 0, 155 })
        {
            var eye = new Vector3(0, 1.42f, frame == 0 ? 10.3f : 3.5f);
            var camera = new ReferenceCamera3D(eye, eye + new Vector3(0, -.65f, -1), Vector3.UnitY, .88f);
            renderer.ResetTemporalHistory();
            var result = renderer.Render(scene, RegionalMatrix(camera, width, height), eye, Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-manual-{frame}.png"), width, height, result.Pixels!);
            manual.Add(frame, EyeDisplayLuminance(result.Pixels!, width, height));
        }
        float contrast = route[^1].Eye.MeteredLogLuminance - route[0].Eye.MeteredLogLuminance;
        float maxStep = route.Zip(route.Skip(1), (prior, current) => Math.Abs(prior.Eye.AppliedEv - current.Eye.AppliedEv)).Max();
        double medianGpu = route.Skip(12).Select(frame => frame.MeterGpuMilliseconds).Order().ElementAt((route.Count - 12) / 2);
        Console.WriteLine($"R7 Beacon room-to-sun contrast {contrast:F3} EV, largest frame step {maxStep:F3} EV, eye pass {medianGpu:F3} ms.");
        Require(contrast > 2 && route[0].Eye.AppliedEv - route[^1].Eye.AppliedEv > 2,
            "Beacon route did not exercise a substantial scene-luminance transition.");
        Require(maxStep < .3f && route[0].DisplayLuminance is > .08 and < .25 && manual[0] < .01,
            "Beacon exposure failed to reveal the dark room or changed too sharply during the walk.");
        Require(medianGpu < .35, "GPU exposure exceeded the qualification budget.");
        return new { Frames = route, ManualDisplayLuminance = manual, ContrastEv = contrast, MaxFrameStepEv = maxStep,
            MedianMeterGpuMilliseconds = medianGpu, Width = width, Height = height };
    }

    private static double EyeDisplayLuminance(byte[] pixels, int width, int height)
    {
        double sum = 0;
        int count = 0;
        for (int y = height / 3; y < 2 * height / 3; y++)
        {
            for (int x = width / 3; x < 2 * width / 3; x++)
            {
                int index = (y * width + x) * 4;
                sum += .2126 * ScalingSrgb[pixels[index]] + .7152 * ScalingSrgb[pixels[index + 1]]
                    + .0722 * ScalingSrgb[pixels[index + 2]];
                count++;
            }
        }
        return sum / count;
    }

    private static Native3DScene BuildEyeRoom(BeaconGame game)
    {
        var scene = new BeaconResidentScene3D().Build(game, includeViewmodel: false);
        var box = scene.Instances.First(instance => instance.Mesh.Identity == "beacon/unit-box").Mesh;
        var walls = new ModelMaterial("r7/meter-room") { BaseColor = new(.1f, .13f, .14f, 1), DoubleSided = true };
        var floor = new ModelMaterial("r7/meter-floor") { BaseColor = new(.065f, .085f, .09f, 1), DoubleSided = true };
        SceneInstance3D Room(string name, Vector3 center, Vector3 halfSize, ModelMaterial material)
            => new("r7/" + name, box, material, Matrix4x4.CreateScale(halfSize) * Matrix4x4.CreateTranslation(center));
        return scene with
        {
            Instances = new NativeInstanceSet3D([
                .. scene.Instances,
                Room("left", new(-1.4f, 1.2f, 9), new(.10f, 1.2f, 2.25f), walls),
                Room("right", new(1.4f, 1.2f, 9), new(.10f, 1.2f, 2.25f), walls),
                Room("roof", new(0, 2.45f, 9), new(1.5f, .1f, 2.25f), walls),
                Room("back", new(0, 1.2f, 11.2f), new(1.5f, 1.2f, .1f), walls),
                Room("floor", new(0, .04f, 9), new(1.5f, .04f, 2.2f), floor),
            ]),
        };
    }

    private static Native3DVertex[] EyeRectangle(float left, float right, float linearLuminance)
        => EyeRectangle(left, right, new Vector3(linearLuminance));

    private static Native3DVertex[] EyeRectangle(float left, float right, Vector3 rgb)
    {
        Vector3 a = new(left, -1, .5f);
        Vector3 b = new(right, -1, .5f);
        Vector3 c = new(right, 1, .5f);
        Vector3 d = new(left, 1, .5f);
        Vector4 color = new(rgb, 1);
        return new[] { a, b, c, a, c, d }.Select(vertex => new Native3DVertex(vertex, Vector3.UnitZ, color)).ToArray();
    }
}
