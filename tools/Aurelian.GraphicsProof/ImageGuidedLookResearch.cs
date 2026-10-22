using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Cloudstep;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

/// <summary>A bounded offline experiment on actual Cloudstep game views, using the production renderer.</summary>
internal static class ImageGuidedLookResearch
{
    internal const int Width = 960;
    internal const int Height = 540;
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static readonly NativeFrameClearColor Clear = new(.56f, .74f, .93f, 1);

    public static void Run(string output, bool fit)
    {
        Directory.CreateDirectory(output);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian image-guided look pilot"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = CreateRenderer(plant, assets, target);
        using var game = new CloudstepGame();
        // Keep the historical pilot's baseline stable when the engine default changes.
        Graphics3DSettings baseline = game.Manifest.Graphics with
        {
            AntiAliasing = AntiAliasing3D.None,
            DirectDiffuseModel = DirectDiffuseModel3D.Lambert,
        };
        GameView[] views = [View(0), View(1)];
        var policies = Policies();
        if (!fit)
        {
            WriteJson(Path.Combine(output, "protocol.json"), new
            {
                Version = 1,
                Game = "Cloudstep",
                Width,
                Height,
                TrainingView = "checkpoint 0, initial game camera, simulation time 0",
                HeldOutView = "checkpoint 1, initial checkpoint camera, simulation time 0",
                Fixed = "Geometry, authored vertex colors, exposure, sun, ambient, fog, AO and bloom. AA disabled for deterministic single-frame comparison.",
                Candidates = policies,
                Metric = "Course/hero-only 24x14 patch mean CIELAB RMSE. Silhouette eroded by 4 pixels; clear sky, cloud-colored geometry and emissive model pickups excluded. Proxy for broad color/tone, not a perceptual realism score.",
                FitCriterion = "At least 10% training-loss reduction versus Lambert / retention 0.",
                TransferCriterion = "At least 5% held-out-loss reduction; held-out target is never consulted for selection.",
                CostGate = "Median full GPU frame <= baseline * 1.25 + 0.15 ms; 20 rotating samples after 5 warmup cycles.",
                ClipGate = "At most baseline foreground clipped-channel fraction + 0.005 (8-bit channels >= 254).",
                ReferenceAdmission = "Visual review of camera, objects and silhouettes; record geometry drift. Generated images supply an aesthetic target, not transport truth.",
                Utility = "Quantize 1000000/(1+training RMSE) to u32; greatest eligible score; source-order ties; native VTS verification.",
            });
            for (int index = 0; index < views.Length; index++)
            {
                renderer.Settings = baseline;
                byte[] pixels = Render(renderer, views[index]);
                Save(output, index == 0 ? "train-baseline.png" : "heldout-baseline.png", pixels);
                renderer.Settings = Graphics3DSettings.Basic;
                Vector3 maskNormal = Vector3.Normalize(Graphics3DSettings.Default.SunDirection);
                Native3DVertex[] white = views[index].Scene.Geometry.Where(vertex =>
                    !(vertex.Color.Z > .8f && vertex.Color.Z > vertex.Color.Y && vertex.Color.Y > vertex.Color.X))
                    .Select(vertex => vertex with { Color = Vector4.One, Normal = maskNormal }).ToArray();
                var maskScene = new Native3DScene(white, []);
                byte[] mask = renderer.Render(maskScene, views[index].Camera, views[index].Eye, new(0, 0, 0, 1), capture: true).Pixels!;
                Save(output, index == 0 ? "train-mask.png" : "heldout-mask.png", mask);
            }
            Console.WriteLine($"AURELIAN_LOOK_CAPTURE_PASSED output={output}");
            return;
        }
        Fit(output, plant, renderer, views, baseline, policies);
    }

    private static GameView View(int checkpoint)
    {
        using var game = new CloudstepGame();
        game.NewGame();
        Require(game.Command($"checkpoint {checkpoint}"), "Checkpoint command failed.");
        return game.View(new(Width, Height)) ?? throw new InvalidDataException("Cloudstep produced no view.");
    }

    private static LookPolicy[] Policies()
    {
        var result = new List<LookPolicy>();
        foreach (float retention in new[] { 0f, .35f, .7f })
        {
            foreach (var model in Enum.GetValues<DirectDiffuseModel3D>())
            {
                string name = model + "-retain-" + retention.ToString("0.00", CultureInfo.InvariantCulture);
                result.Add(new(result.Count, name, model, retention));
            }
        }
        return result.ToArray();
    }

    internal static VulkanSolid3DRenderer CreateRenderer(AurelianVulkanPlant plant, GameAssets assets, VulkanNativeFrameTarget target)
    {
        return new(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                HeightFog = assets.Shader("HeightFog3D.v.ts"),
            });
    }

    private static byte[] Render(VulkanSolid3DRenderer renderer, GameView view)
    {
        return renderer.Render(view.Scene, view.Camera, view.Eye, Clear, capture: true).Pixels!;
    }

    private static void Fit(string output, AurelianVulkanPlant plant, VulkanSolid3DRenderer renderer,
        GameView[] views, Graphics3DSettings baseline, LookPolicy[] policies)
    {
        string evidencePath = Path.Combine(output, "evidence.json");
        WriteJson(evidencePath, new { Completed = false });
        object outputOracle = ProveOutput(plant);
        Require(File.Exists(Path.Combine(output, "protocol.json")), "Capture the frozen protocol before fitting.");
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "protocol.json")));
        var frozen = protocol.RootElement.GetProperty("Candidates").Deserialize<LookPolicy[]>(Json);
        Require(frozen is not null && frozen.SequenceEqual(policies), "Candidate policies changed after capture.");
        using var review = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "reference-review.json")));
        Require(review.RootElement.GetProperty("Admitted").GetBoolean(), "References have not passed visual admission.");
        var referenceHashes = new Dictionary<string, string>
        {
            ["Train"] = Hash(Path.Combine(output, "train-target.png")),
            ["Heldout"] = Hash(Path.Combine(output, "heldout-target.png")),
            ["Review"] = Hash(Path.Combine(output, "reference-review.json")),
        };
        string referenceLockPath = Path.Combine(output, "reference-lock.json");
        if (File.Exists(referenceLockPath))
        {
            var locked = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(referenceLockPath))!;
            Require(referenceHashes.All(pair => locked.TryGetValue(pair.Key, out string? value) && value == pair.Value),
                "A frozen reference or its admission changed; start a separate experiment directory.");
        }
        else
        {
            WriteJson(referenceLockPath, referenceHashes);
        }
        var training = LookImageMetrics.Load(output, "train");
        object trainingMetric = training.Qualify();
        var observations = new List<LookObservation>();
        var captures = new Dictionary<string, byte[]>();
        foreach (LookPolicy policy in policies)
        {
            renderer.Settings = Apply(baseline, policy);
            byte[] pixels = Render(renderer, views[0]);
            double loss = training.Loss(pixels);
            double clipping = training.Clipping(pixels);
            captures[policy.Name] = pixels;
            Save(output, "candidate-" + policy.Name + ".png", pixels);
            observations.Add(new(policy, loss, clipping, []));
        }
        for (int cycle = 0; cycle < 25; cycle++)
        {
            for (int index = 0; index < policies.Length; index++)
            {
                int slot = (index + cycle) % policies.Length;
                renderer.Settings = Apply(baseline, policies[slot]);
                var frame = renderer.Render(views[0].Scene, views[0].Camera, views[0].Eye, Clear);
                if (cycle >= 5)
                {
                    observations[slot].GpuMilliseconds.Add(frame.GpuPassTimes.Sum(pass => pass.Milliseconds));
                }
            }
        }
        double baselineCost = observations[0].MedianGpuMilliseconds;
        Require(double.IsFinite(baselineCost) && baselineCost > 0, "GPU timestamp measurements are unavailable.");
        double baselineClip = observations[0].ClippedChannelFraction;
        foreach (var observation in observations)
        {
            observation.Eligible = observation.MedianGpuMilliseconds <= baselineCost * 1.25 + .15
                && observation.ClippedChannelFraction <= baselineClip + .005;
            observation.Utility = (uint)Math.Round(1_000_000 / (1 + observation.TrainingLoss));
        }
        LookObservation expected = observations.Where(item => item.Eligible).OrderByDescending(item => item.Utility).First();
        LookUtilityProof utility = ProveUtility(output, plant, observations, expected.Policy.Id);
        LookObservation winner = observations[utility.Selected];
        // Selection is frozen before the held-out target is evaluated.
        WriteJson(Path.Combine(output, "selection.json"), new { winner.Policy, winner.Utility, utility });
        var heldout = LookImageMetrics.Load(output, "heldout");
        object heldoutMetric = heldout.Qualify();
        renderer.Settings = Apply(baseline, winner.Policy);
        byte[] transferred = Render(renderer, views[1]);
        Save(output, "heldout-selected.png", transferred);
        renderer.Settings = baseline;
        byte[] heldoutBaseline = Render(renderer, views[1]);
        double originalHeldoutLoss = heldout.Loss(heldoutBaseline);
        double selectedHeldoutLoss = heldout.Loss(transferred);
        double trainingGain = 1 - winner.TrainingLoss / observations[0].TrainingLoss;
        double transferGain = 1 - selectedHeldoutLoss / originalHeldoutLoss;
        double heldoutBaselineClip = heldout.Clipping(heldoutBaseline);
        double heldoutSelectedClip = heldout.Clipping(transferred);
        bool heldoutClipPassed = heldoutSelectedClip <= heldoutBaselineClip + .005;
        Save(output, "train-selected.png", captures[winner.Policy.Name]);
        WriteJson(Path.Combine(output, "selected-settings.json"), Apply(baseline, winner.Policy) with
        {
            AntiAliasing = AntiAliasing3D.Temporal,
        });
        LookImageMetrics.Sheet(Path.Combine(output, "comparison.png"), training, heldout,
            captures[policies[0].Name], captures[winner.Policy.Name], heldoutBaseline, transferred);
        WriteJson(evidencePath, new
        {
            Completed = true,
            MethodologyCriterionPassed = trainingGain >= .1 && transferGain >= .05 && heldoutClipPassed,
            Device = plant.Facts.PhysicalDeviceName,
            plant.Facts.EnabledValidationLayers,
            TrainingGain = trainingGain,
            TransferGain = transferGain,
            BaselineHeldoutLoss = originalHeldoutLoss,
            SelectedHeldoutLoss = selectedHeldoutLoss,
            HeldoutClipping = new { Baseline = heldoutBaselineClip, Selected = heldoutSelectedClip, Passed = heldoutClipPassed },
            Selected = winner.Policy,
            Observations = observations,
            Utility = utility,
            OutputResponseOracle = outputOracle,
            MetricControls = new { Training = trainingMetric, Heldout = heldoutMetric },
            ProtocolSha256 = Hash(Path.Combine(output, "protocol.json")),
            ReferenceHashes = new { Train = Hash(Path.Combine(output, "train-target.png")), Heldout = Hash(Path.Combine(output, "heldout-target.png")) },
            Limits = "One game, two still views, generated reference bias, foreground patch-color proxy, no independent human preference study. Cost gate measures a GPU frame locally. No claim of research novelty or universal realism.",
        });
        Console.WriteLine($"AURELIAN_LOOK_RESEARCH_COMPLETED selected={winner.Policy.Name} trainGain={trainingGain:P2} heldoutGain={transferGain:P2}");
    }

    internal static object ProveOutput(AurelianVulkanPlant plant)
    {
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 17, 17);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
        int cases = 0;
        float maximumError = 0;
        foreach (Vector3 color in new[] { Vector3.Zero, new Vector3(.35f), new Vector3(8), new Vector3(2, .35f, .08f), new Vector3(.1f, .5f, 3) })
        {
            foreach (float exposure in new[] { .4f, .85f, 2f })
            {
                foreach (bool toneMapping in new[] { false, true })
                {
                    byte[]? control = null;
                    foreach (float retention in new[] { 0f, .35f, .7f, 1f })
                    {
                        foreach (bool compression in new[] { false, true })
                        {
                            renderer.Settings = Graphics3DSettings.Basic with
                            {
                                SunDirection = Vector3.UnitZ,
                                Exposure = exposure,
                                ToneMapping = toneMapping,
                                ToneMapColorRetention = retention,
                                ToneMapGamutCompression = compression,
                            };
                            Native3DVertex Vertex(float x, float y) => new(new(x, y, .5f), Vector3.UnitZ, new(color, 1));
                            Native3DVertex[] quad = [Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)];
                            var frame = renderer.Render(quad, Matrix4x4.Identity, new(0, 0, 0, 1), capture: true, eye: new(0, 0, 6));
                            byte[] pixels = frame.Pixels!;
                            if (!toneMapping)
                            {
                                control ??= pixels;
                                Require(control.SequenceEqual(pixels), "Retention changed disabled tone mapping.");
                            }
                            double y = (.2126 * color.X + .7152 * color.Y + .0722 * color.Z) * exposure;
                            static double Film(double value) => Math.Clamp(value * (2.51 * value + .03) / (value * (2.43 * value + .59) + .14), 0, 1);
                            float Expected(float channel)
                            {
                                double value = channel * exposure;
                                if (toneMapping)
                                {
                                    value = Film(value) * (1 - retention) + value * Film(y) / Math.Max(y, .000001) * retention;
                                }
                                return (float)value;
                            }
                            int offset = (8 * 17 + 8) * 4;
                            Vector3 actual = new(NativeSrgbTransfer.Decode(pixels[offset] / 255f),
                                NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
                            Vector3 expected = new(Expected(color.X), Expected(color.Y), Expected(color.Z));
                            if (toneMapping && compression)
                            {
                                double center = Math.Clamp(.2126 * expected.X + .7152 * expected.Y + .0722 * expected.Z, 0, 1);
                                double Distance(float channel) => channel > center
                                    ? (channel - center) / Math.Max(1 - center, .000001)
                                    : (center - channel) / Math.Max(center, .000001);
                                double distance = Math.Max(Distance(expected.X), Math.Max(Distance(expected.Y), Distance(expected.Z)));
                                double scale = 1;
                                if (distance > .8)
                                {
                                    double excess = distance - .8;
                                    scale = (.8 + .2 * excess / (excess + .2)) / distance;
                                }
                                expected = new((float)(center + (expected.X - center) * scale),
                                    (float)(center + (expected.Y - center) * scale), (float)(center + (expected.Z - center) * scale));
                            }
                            expected = Vector3.Clamp(expected, Vector3.Zero, Vector3.One);
                            float error = Vector3.Distance(actual, expected);
                            maximumError = Math.Max(maximumError, error);
                            Require(error < .009, $"Output curve oracle mismatch r={retention} exposure={exposure} tone={toneMapping}: {actual} vs {expected}.");
                            if (color.X == color.Y && color.Y == color.Z)
                            {
                                Require(pixels[offset] == pixels[offset + 1] && pixels[offset + 1] == pixels[offset + 2], "Neutral color shifted hue.");
                            }
                            cases++;
                        }
                    }
                }
            }
        }
        return new
        {
            Cases = cases,
            MaximumLinearRgbError = maximumError,
            Scope = "Native output versus independent curve equations, including soft gamut compression; black, neutral, HDR colored and white samples, four retention values, three exposures. Disabled tone mapping is byte-identical across both gamut modes and retention values."
        };
    }

    private static LookUtilityProof ProveUtility(string output, AurelianVulkanPlant plant, List<LookObservation> observations, int expected)
    {
        var cases = new List<object>();
        int selected = -1;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 17, 17);
        var scenarios = new[]
        {
            (Name: "measured", Choices: observations.Select(item => (item.Policy.Id, item.Eligible, item.Utility)).ToArray(), Expected: expected),
            (Name: "none-eligible", Choices: observations.Select(item => (item.Policy.Id, false, item.Utility)).ToArray(), Expected: 15),
            (Name: "tie", Choices: observations.Select(item => (item.Policy.Id, true, 42u)).ToArray(), Expected: 0),
        };
        foreach (var scenario in scenarios)
        {
            string choices = string.Join(Environment.NewLine, scenario.Choices.Select(item =>
                $"        case {item.Id}.0 when {item.Item2.ToString().ToLowerInvariant()} score {item.Item3}"));
            string source = BuiltinShaderLibrary.Read("Solid3D.v.ts");
            const string seam = "DirectLightModel(base, normal, view, light, resources.camera.surface.y, resources.camera.surface.x, resources.camera.ground.w)";
            Require(source.Contains(seam, StringComparison.Ordinal), "Utility probe shader seam changed.");
            source = source.Replace(seam, "float3(ChoosePolicy() / 16.0, 0.0, 0.0)", StringComparison.Ordinal);
            source += "\nfunction ChoosePolicy(): f32 {\n    return when utility {\n" + choices
                + "\n        else 15.0\n    };\n}\n";
            File.WriteAllText(Path.Combine(output, "utility-" + scenario.Name + ".v.ts"), source);
            var sources = BuiltinShaderLibrary.LoadSources("Solid3D.v.ts").Select(file =>
                file.Path == "Solid3D.v.ts" ? new GpuSourceFile(file.Path, source) : file).ToArray();
            var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest(sources));
            Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            var backend = VdMirGraphicsBackend.Compile(module, targetEnvironment: "vulkan1.2");
            Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "Utility shader failed SPIR-V validation.");
            var program = CompiledGraphicsProgramExporter.Export(module, backend);
            using var renderer = new VulkanSolid3DRenderer(plant, program, target,
                shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
            renderer.Settings = Graphics3DSettings.Default with
            {
                AntiAliasing = AntiAliasing3D.None,
                ToneMapping = false,
                BloomIntensity = 0,
                Shadows = false,
                SunDirection = Vector3.UnitZ,
                SunColor = Vector3.One,
                SunIntensity = 1,
                SkyAmbient = Vector3.Zero,
                GroundAmbient = Vector3.Zero,
            };
            Native3DVertex Vertex(float x, float y) => new(new(x, y, .5f), Vector3.UnitZ, Vector4.One);
            Native3DVertex[] quad = [Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)];
            var frame = renderer.Render(quad, Matrix4x4.Identity, new(0, 0, 0, 1), capture: true, eye: new(0, 0, 6));
            float encoded = NativeSrgbTransfer.Decode(frame.Pixels![(8 * 17 + 8) * 4] / 255f);
            int actual = (int)MathF.Round(encoded * 16);
            Require(actual == scenario.Expected, $"Native utility mismatch {scenario.Name}: {actual} vs {scenario.Expected}.");
            if (scenario.Name == "measured")
            {
                selected = actual;
            }
            cases.Add(new { scenario.Name, scenario.Expected, Actual = actual, program.VdMirSha256 });
        }
        return new(selected, cases);
    }

    private static Graphics3DSettings Apply(Graphics3DSettings baseline, LookPolicy policy)
    {
        return baseline with { DirectDiffuseModel = policy.Diffuse, ToneMapColorRetention = policy.Retention };
    }

    internal static void Save(string output, string name, byte[] pixels)
    {
        NativeGameGraphics.WritePng(Path.Combine(output, name), Width, Height, pixels);
    }

    internal static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

internal sealed record LookPolicy(int Id, string Name, DirectDiffuseModel3D Diffuse, float Retention);

internal sealed record LookUtilityProof(int Selected, IReadOnlyList<object> Cases)
{
    public string Scope => "Measured scores evaluated by compiled VTS on Vulkan. Native decision ID applied to ordinary game settings; no per-pixel model switching or runtime AI.";
}

internal sealed record LookObservation(LookPolicy Policy, double TrainingLoss, double ClippedChannelFraction, List<double> GpuMilliseconds)
{
    public double MedianGpuMilliseconds => GpuMilliseconds.Order().ElementAt(GpuMilliseconds.Count / 2);
    public bool Eligible { get; set; }
    public uint Utility { get; set; }
}
