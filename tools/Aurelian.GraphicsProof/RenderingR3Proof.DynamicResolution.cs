using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Maths;
using Silk.NET.Windowing;

internal static partial class RenderingR3Proof
{
    private const int ResolutionPhaseFrames = 160;
    private const int ResolutionTailFrames = 64;
    private static readonly string[] ResolutionPhases = ["light", "heavy", "recovery", "heavy-repeat", "recovery-repeat"];

    public static void RunDynamicResolution(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");

        var assets = new GameAssets();
        using var game = new BeaconGame();
        var scene = new BeaconResidentScene3D().Build(game, includeViewmodel: false);
        scene = scene with
        {
            Instances = new NativeInstanceSet3D(scene.Instances.Select(instance => instance.Identity.Contains("/floor/", StringComparison.Ordinal)
                ? instance with { Material = instance.Material with { Roughness = .24f } }
                : instance)),
        };
        var baseline = MeasureResolutionPolicy(output, assets, scene, "native", RenderScale3D.Native, null);
        var medium = MeasureResolutionPolicy(output, assets, scene, "two-thirds", RenderScale3D.TwoThirds, null);
        var low = MeasureResolutionPolicy(output, assets, scene, "half", RenderScale3D.Half, null);
        var repeatedNative = MeasureResolutionPolicy(output, assets, scene, "native-repeat", RenderScale3D.Native, null);
        ResolutionSummary baselineHeavy = SummarizeResolution(baseline, "heavy-repeat");
        ResolutionSummary repeatHeavy = SummarizeResolution(repeatedNative, "heavy-repeat");
        ResolutionSummary mediumHeavy = SummarizeResolution(medium, "heavy-repeat");
        ResolutionSummary lowHeavy = SummarizeResolution(low, "heavy-repeat");
        // Place both budgets between independently measured fixed-scale controls.
        double mediumBudget = (Math.Min(baselineHeavy.MedianGpuMilliseconds, repeatHeavy.MedianGpuMilliseconds)
            + mediumHeavy.P95GpuMilliseconds) * .5;
        double tightBudget = (mediumHeavy.MedianGpuMilliseconds + lowHeavy.P95GpuMilliseconds) * .5;
        Console.WriteLine($"R6 dynamic budgets: medium {mediumBudget:F2} ms, tight {tightBudget:F2} ms.");
        var adaptive = MeasureResolutionPolicy(output, assets, scene, "dynamic", RenderScale3D.Native, mediumBudget);
        var tight = MeasureResolutionPolicy(output, assets, scene, "dynamic-tight", RenderScale3D.Native, tightBudget);
        ResolutionTrial[] trials = [baseline, medium, low, repeatedNative, adaptive, tight];
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(trials, json));
        var summaries = trials.Select(trial => new
        {
            trial.Name,
            trial.BudgetMilliseconds,
            trial.Switches,
            trial.ResidentScales,
            trial.SwapchainRebuilds,
            trial.AcceptedSamples,
            trial.Device,
            trial.Width,
            trial.Height,
            trial.StartupMilliseconds,
            Phases = ResolutionPhases.Select(phase => SummarizeResolution(trial, phase, trial == tight ? tightBudget : mediumBudget)).ToArray(),
        }).ToArray();
        var pairs = new List<object>();
        foreach (var trial in trials.Skip(1))
        {
            foreach (string phase in new[] { "heavy-repeat", "recovery-repeat" })
            {
                string name = trial.Name + "-" + phase;
                pairs.Add(new { Name = name, Reference = "native-" + phase + ".png", Test = name + ".png", Group = name });
            }
        }
        pairs.Add(new { Name = "identity", Reference = "native-heavy-repeat.png", Test = "native-heavy-repeat.png", Group = "identity" });
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "dynamic-resolution", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));

        bool Accepted = baselineHeavy.MedianGpuMilliseconds > mediumBudget && repeatHeavy.MedianGpuMilliseconds > mediumBudget
            && mediumHeavy.P95GpuMilliseconds < mediumBudget && mediumHeavy.MedianGpuMilliseconds > tightBudget
            && lowHeavy.P95GpuMilliseconds < tightBudget
            && QualifiesResolutionTrial(adaptive, RenderScale3D.TwoThirds, mediumBudget)
            && QualifiesResolutionTrial(tight, RenderScale3D.Half, tightBudget);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { Accepted, MediumBudget = mediumBudget, TightBudget = tightBudget, Trials = summaries }, json));
        Require(Accepted, "Dynamic resolution did not settle at the highest sustainable fixed scale and recover native detail.");
        Console.WriteLine("AURELIAN_R6_DYNAMIC_PASSED " + output);
    }

    private sealed record ResolutionSample(string Phase, int Frame, RenderScale3D Scale, double WallMilliseconds)
    {
        public double? GpuMilliseconds { get; set; }
    }

    private sealed record ResolutionSwitch(string Phase, int Frame, RenderScale3D Scale, bool Revisited, double WorldRenderMilliseconds);

    private sealed record ResolutionTrial(string Name, double? BudgetMilliseconds, string Device, int Width, int Height, double StartupMilliseconds,
        int ResidentScales, int SwapchainRebuilds, long AcceptedSamples, IReadOnlyList<ResolutionSwitch> Switches, IReadOnlyList<ResolutionSample> Frames);

    private sealed record ResolutionSummary(string Phase, double MedianGpuMilliseconds, double P95GpuMilliseconds,
        double TailOverBudgetFraction, double WholePhaseOverBudgetFraction, double NativeFraction, double TwoThirdsFraction,
        double HalfFraction, double MeanRasterArea, double P95WallMilliseconds, int TimingSamples);

    private static ResolutionTrial MeasureResolutionPolicy(string output, GameAssets assets, Native3DScene scene, string name,
        RenderScale3D scale, double? budget)
    {
        var options = WindowOptions.DefaultVulkan;
        options.Title = "Beacon dynamic resolution proof";
        options.Size = new Vector2D<int>(1920, 1080);
        options.WindowBorder = WindowBorder.Hidden;
        options.IsVisible = false;
        options.VSync = false;
        using var window = Window.Create(options);
        window.Initialize();
        long started = Stopwatch.GetTimestamp();
        using var graphics = new NativeGameGraphics(window, options.Title, false, assets, enableRayQueries: true, vSync: false,
            renderScale: scale, dynamicGpuBudgetMilliseconds: budget);
        double startup = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Console.WriteLine($"R6 {name} startup: {startup:F2} ms, {graphics.ResidentRenderScales} allocated world renderers.");
        using var validation = new RenderingR0Validation(graphics.Plant);
        graphics.EnableAdaptiveVisibility = false;
        graphics.Renderer.EnableVisibilityBuffer = true;
        graphics.Renderer.EnableRayShadows = true;
        graphics.Renderer.EnvironmentSource = BeaconDiffuseLighting3D.Source;
        graphics.Environment = VulkanEnvironmentCompiler.Compile(graphics.Plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        graphics.LocalLights = BeaconDiffuseLighting3D.Lights;
        var heavy = BeaconDiffuseLighting3D.Settings with { GlossyReflections = GlossyReflectionMode3D.HalfResolution };
        var light = heavy with { GlossyReflections = GlossyReflectionMode3D.Off, AmbientOcclusionStrength = 0 };
        var heavyCamera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var lightCamera = new ReferenceCamera3D(new(4, 5.5f, 9), new(-3, .65f, -2), Vector3.UnitY, .82f);
        var samples = new List<ResolutionSample>();
        var switches = new List<ResolutionSwitch>();
        var seenScales = new HashSet<RenderScale3D> { scale };
        var pending = new Dictionary<(VulkanSolid3DRenderer, long), ResolutionSample>();
        ulong present = 0;
        for (int index = 0; index < ResolutionPhases.Length; index++)
        {
            string phase = ResolutionPhases[index];
            bool loaded = index is 1 or 3;
            var camera = loaded ? heavyCamera : lightCamera;
            graphics.Settings = loaded ? heavy : light;
            graphics.Renderer.DynamicDiffuse = loaded ? BeaconDiffuseLighting3D.Runtime : new();
            Matrix4x4 clip = RegionalMatrix(camera, graphics.Width, graphics.Height);
            for (int frame = 0; frame < ResolutionPhaseFrames; frame++)
            {
                window.DoEvents();
                bool capture = frame == ResolutionPhaseFrames - 1;
                RenderScale3D before = graphics.ActiveRenderScale;
                var watch = Stopwatch.StartNew();
                var result = graphics.Render(scene, clip, camera.Eye, capture);
                double worldMilliseconds = watch.Elapsed.TotalMilliseconds;
                Require(graphics.Present(++present), "Beacon dynamic proof lost its swapchain.");
                double wallMilliseconds = watch.Elapsed.TotalMilliseconds;
                var actual = graphics.ActiveRenderScale;
                if (actual != before)
                {
                    switches.Add(new(phase, frame, actual, !seenScales.Add(actual), worldMilliseconds));
                    Console.WriteLine($"R6 {name} {phase} frame {frame}: {before} → {actual}, world render {worldMilliseconds:F2} ms.");
                }
                var sample = new ResolutionSample(phase, frame, actual, wallMilliseconds);
                samples.Add(sample);
                if (result.CpuTimings is { } submitted) pending.Add((graphics.Renderer, submitted.Sequence), sample);
                if (result.CompletedGpuFrame is { } completed && pending.Remove((graphics.Renderer, completed.Sequence), out var measured))
                    measured.GpuMilliseconds = completed.ElapsedMilliseconds;
                if (capture)
                {
                    NativeGameGraphics.WritePng(Path.Combine(output, name + "-" + phase + ".png"),
                        graphics.Width, graphics.Height, result.Pixels!);
                }
            }
            Require(graphics.Renderer.DynamicDiffuse.Mode == (loaded ? DynamicDiffuseMode3D.Runtime : DynamicDiffuseMode3D.Off),
                "The world's lighting mode changed during a scale transition.");
        }
        graphics.Renderer.WaitForPendingFrames();
        validation.RequireClean();
        var trial = new ResolutionTrial(name, budget, graphics.Plant.Facts.PhysicalDeviceName, graphics.Width, graphics.Height,
            startup, graphics.ResidentRenderScales, graphics.SurfaceRebuilds, graphics.ResolutionPolicy?.AcceptedSamples ?? 0, switches, samples);
        foreach (string phase in ResolutionPhases)
        {
            var summary = SummarizeResolution(trial, phase, budget);
            Console.WriteLine($"R6 {name} {summary}.");
        }
        return trial;
    }

    private static ResolutionSummary SummarizeResolution(ResolutionTrial trial, string phase, double? budget = null)
    {
        var frames = trial.Frames.Where(sample => sample.Phase == phase).ToArray();
        var tail = frames.TakeLast(ResolutionTailFrames).ToArray();
        var sorted = tail.Where(sample => sample.GpuMilliseconds > 0).Select(sample => sample.GpuMilliseconds!.Value).Order().ToArray();
        Require(sorted.Length >= ResolutionTailFrames - 8, "Too few complete GPU timing samples for " + trial.Name + " " + phase);
        double[] allGpu = frames.Where(sample => sample.GpuMilliseconds > 0).Select(sample => sample.GpuMilliseconds!.Value).ToArray();
        double[] wall = tail.Take(tail.Length - 1).Select(sample => sample.WallMilliseconds).Order().ToArray();
        return new ResolutionSummary(phase, sorted[sorted.Length / 2], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
            budget is { } target ? sorted.Count(ms => ms > target) / (double)sorted.Length : 0,
            budget is { } wholeTarget ? allGpu.Count(ms => ms > wholeTarget) / (double)allGpu.Length : 0,
            tail.Count(sample => sample.Scale == RenderScale3D.Native) / (double)tail.Length,
            tail.Count(sample => sample.Scale == RenderScale3D.TwoThirds) / (double)tail.Length,
            tail.Count(sample => sample.Scale == RenderScale3D.Half) / (double)tail.Length,
            frames.Average(sample => sample.Scale switch { RenderScale3D.Native => 1, RenderScale3D.TwoThirds => 4d / 9, _ => .25 }),
            wall[(int)Math.Ceiling(wall.Length * .95) - 1], allGpu.Length);
    }

    private static bool QualifiesResolutionTrial(ResolutionTrial trial, RenderScale3D loadedScale, double budget)
    {
        foreach (string phase in new[] { "heavy", "heavy-repeat" })
        {
            var heavy = SummarizeResolution(trial, phase, budget);
            double fraction = loadedScale == RenderScale3D.TwoThirds ? heavy.TwoThirdsFraction : heavy.HalfFraction;
            if (fraction < .9 || heavy.TailOverBudgetFraction > .10 || heavy.WholePhaseOverBudgetFraction > .15)
            {
                return false;
            }
        }
        return SummarizeResolution(trial, "recovery", budget).NativeFraction >= .9
            && SummarizeResolution(trial, "recovery-repeat", budget).NativeFraction >= .9
            && trial.SwapchainRebuilds == 0 && trial.AcceptedSamples > 500 && trial.Switches.Count <= 8
            && trial.Switches.Any(change => change.Revisited && change.Scale == loadedScale);
    }
}
