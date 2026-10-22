using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Cloudstep;
using static ImageGuidedLookRefinement;

/// <summary>Third bounded trial: fixed PathDiffuse, independently labelled reference swatches.</summary>
internal static class SemanticLookCalibration
{
    private static readonly JsonSerializerOptions Json = ImageGuidedLookResearch.Json;
    private static readonly string[] Splits = ["train0", "train1", "heldout"];
    private const int Budget = 360;

    public static void Run(string output, bool fit, bool admit, bool verifyOcclusion)
    {
        Require((fit ? 1 : 0) + (admit ? 1 : 0) + (verifyOcclusion ? 1 : 0) <= 1, "Admission, fitting and confirmation are separate stages.");
        Directory.CreateDirectory(output);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian semantic look calibration"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, ImageGuidedLookResearch.Width, ImageGuidedLookResearch.Height);
        using var renderer = ImageGuidedLookResearch.CreateRenderer(plant, assets, target);
        LookView[] views = [ImageGuidedLookRefinement.View(0), ImageGuidedLookRefinement.View(1), ImageGuidedLookRefinement.View(2)];
        using var game = new CloudstepGame();
        Graphics3DSettings authored = game.Manifest.Graphics with { AntiAliasing = AntiAliasing3D.None };
        Require(authored.DirectDiffuseModel == DirectDiffuseModel3D.PathDiffuse, "The shaded default must be PathDiffuse.");
        if (!fit && !admit && !verifyOcclusion)
        {
            Capture(output, renderer, views, authored);
            return;
        }
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "protocol.json")));
        Require(protocol.RootElement.GetProperty("ViewHashes").Deserialize<string[]>()!.SequenceEqual(views.Select(ViewHash)),
            "Game presentation geometry or camera changed after capture.");
        Dictionary<string, string> hashes = ReferenceHashes(output);
        using var review = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "reference-review.json")));
        Require(review.RootElement.GetProperty("AdmittedForSemanticSwatches").GetBoolean(), "Semantic reference review is required.");
        if (admit)
        {
            Require(!File.Exists(Path.Combine(output, "reference-lock.json")), "Reference inputs are already locked.");
            var controls = Splits.Select(split => new SemanticLookMetric(output, split).Qualify()).ToArray();
            Write(output, "admission.json", new { Passed = true, Controls = controls, Scope = "Independent labelled material swatches, not pixel registration." });
            Write(output, "reference-lock.json", hashes);
            Console.WriteLine("AURELIAN_SEMANTIC_LOOK_ADMISSION_PASSED");
            return;
        }
        var locked = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(output, "reference-lock.json")))!;
        Require(hashes.Count == locked.Count && hashes.All(pair => locked.TryGetValue(pair.Key, out string? value) && pair.Value == value),
            "Frozen references, annotations, protocol or review changed.");
        if (verifyOcclusion)
        {
            VerifyOcclusion(output, renderer, views, authored);
            return;
        }
        Require(!File.Exists(Path.Combine(output, "selection.json")), "Selection is already frozen. Use a new directory for another trial.");
        Fit(output, plant, renderer, views, authored);
    }

    private static void Capture(string output, VulkanSolid3DRenderer renderer, LookView[] views, Graphics3DSettings authored)
    {
        Require(!File.Exists(Path.Combine(output, "protocol.json")), "Use a new capture directory.");
        Write(output, "protocol.json", new
        {
            Version = 3,
            Training = "Checkpoint 0 and 1, both previously used references and now explicitly training data.",
            Heldout = "Checkpoint 2 yaw +0.6/pitch 0.45, fresh generated reference, independently labelled before search. Camera previously inspected but not used for fitting.",
            Diffuse = "PathDiffuse fixed for every searched profile. Lambert from the previous trial is a control only.",
            Admission = "Four independently labelled material swatches, at least 60 interior samples each, visual label review before fitting. No pixel-registration assumption. Joint image/label translation must leave the score unchanged.",
            SourceMask = "Excluded model materials and background are rendered with label zero, retaining depth occlusion.",
            Objective = "Equal mean over hero, grass, earth and structure. Each region: 50% mean Lab distance, 50% RMS of marginal Lab quantile distances at 0.1/0.25/0.5/0.75/0.9. Four-pixel erosion; Lab sampled every second pixel; clipping checks every interior pixel.",
            Search = "108 coarse profiles with prior palette: sky scale 0.4/0.8/1.2, ground scale 0.3/1, sun scale 0.75/1/1.25, exposure 0.85/1.2, retention 0/0.35/0.7. Three coordinate passes on the best two seeds over five lighting/tone variables and nine palette components. Decode and gamut compression enabled.",
            Bounds = "Sky/ground scale 0.05..1.5, sun 0.25..2, exposure 0.3..1.8, retention 0..1, palette 0.25..4. Coordinate multiplicative step exp(+/-0.3*0.5^pass), retention additive +/-0.2*0.5^pass.",
            Budget,
            Gates = "Training foreground clipped channels <= 0.001 in both views; 30-sample rotating GPU median <= default*1.25+0.15 ms after five warmups.",
            Success = "At least 10% training and 5% fresh-reference reduction versus the previous profile with PathDiffuse substituted. All heldout regions improve over the uncalibrated PathDiffuse default; heldout clipping <= 0.001. No realism or exact-relighting claim.",
            Stress = "Post-selection exposure 0.5/1/2 by sun 0.25/1/2; report clipping rather than assume arbitrary-light robustness.",
            Selection = "Offline deterministic CPU search evaluating production Vulkan frames. No runtime inference or model selection.",
            ViewHashes = views.Select(ViewHash).ToArray(),
        });
        for (int index = 0; index < views.Length; index++)
        {
            renderer.Settings = authored;
            Save(output, Splits[index] + "-default.png", Render(renderer, views[index].View));
            renderer.Settings = Graphics3DSettings.Basic with { SunDirection = Vector3.UnitZ };
            Save(output, Splits[index] + "-regions.png", renderer.Render(RegionScene(views[index].Frame, includeBackgroundOccluders: true), views[index].View.Camera,
                views[index].View.Eye, new(0, 0, 0, 1), capture: true).Pixels!);
        }
        File.Copy("docs/Aurelian/assets/image-guided-look/train-target.png", Path.Combine(output, "train0-target.png"));
        File.Copy("docs/Aurelian/assets/image-guided-look/heldout-target.png", Path.Combine(output, "train1-target.png"));
        File.Copy("docs/Aurelian/assets/look-refinement/selected-profile.json", Path.Combine(output, "previous-profile.json"));
        Console.WriteLine("AURELIAN_SEMANTIC_LOOK_CAPTURE_PASSED");
    }

    private static void Fit(string output, AurelianVulkanPlant plant, VulkanSolid3DRenderer renderer, LookView[] views, Graphics3DSettings authored)
    {
        Write(output, "evidence.json", new { Completed = false });
        var metrics = new[] { new SemanticLookMetric(output, "train0"), new SemanticLookMetric(output, "train1") };
        var previous = JsonSerializer.Deserialize<CloudstepLookProfile>(
            File.ReadAllText(Path.Combine(output, "previous-profile.json")), Json)!;
        CloudstepLookProfile previousPath = previous with
        {
            Graphics = previous.Graphics with { DirectDiffuseModel = DirectDiffuseModel3D.PathDiffuse, AntiAliasing = AntiAliasing3D.None },
        };
        CloudstepLookProfile Profile(Parameters parameters)
        {
            return new()
            {
                DecodePaletteSrgb = true,
                GrassTint = parameters.Grass,
                EarthTint = parameters.Earth,
                HeroTint = parameters.Hero,
                Graphics = authored with
                {
                    DirectDiffuseModel = DirectDiffuseModel3D.PathDiffuse,
                    SkyAmbient = authored.SkyAmbient * parameters.Sky,
                    GroundAmbient = authored.GroundAmbient * parameters.Ground,
                    SunIntensity = authored.SunIntensity * parameters.Sun,
                    Exposure = parameters.Exposure,
                    ToneMapColorRetention = parameters.Retention,
                    ToneMapGamutCompression = true,
                },
            };
        }
        SemanticLookScore[] Scores(CloudstepLookProfile profile)
        {
            renderer.Settings = profile.Graphics with { AntiAliasing = AntiAliasing3D.None };
            return metrics.Select((metric, index) => metric.Measure(Render(renderer, Apply(views[index], profile)))).ToArray();
        }
        var controls = new Dictionary<string, (CloudstepLookProfile Profile, SemanticLookScore[] Scores)>
        {
            ["default-path"] = (new() { Graphics = authored }, []),
            ["historical-lambert"] = (new() { Graphics = authored with { DirectDiffuseModel = DirectDiffuseModel3D.Lambert } }, []),
            ["previous-lambert"] = (previous, []),
            ["previous-path"] = (previousPath, []),
            ["decode-only-path"] = (new() { Graphics = authored, DecodePaletteSrgb = true }, []),
        };
        foreach (string key in controls.Keys.ToArray())
        {
            CloudstepLookProfile profile = controls[key].Profile;
            controls[key] = (profile, Scores(profile));
        }
        var history = new Dictionary<Parameters, Trial>();
        Trial Evaluate(Parameters parameters)
        {
            if (history.TryGetValue(parameters, out Trial? existing))
            {
                return existing;
            }
            Require(history.Count < Budget, "Frozen search budget exhausted.");
            CloudstepLookProfile profile = Profile(parameters);
            profile.Validate();
            var trial = new Trial(parameters, profile, Scores(profile));
            history.Add(parameters, trial);
            if (history.Count % 40 == 0)
            {
                Console.WriteLine($"SEMANTIC_LOOK_EVALUATIONS {history.Count} best={history.Values.Where(item => item.Eligible).Min(item => item.Loss):F4}");
            }
            return trial;
        }
        var prior = new Parameters(.8187308f, .8187308f, 1, 1.2f, 0, previous.GrassTint, previous.EarthTint, previous.HeroTint);
        Evaluate(prior);
        foreach (float sky in new[] { .4f, .8f, 1.2f })
        {
            foreach (float ground in new[] { .3f, 1 })
            {
                foreach (float sun in new[] { .75f, 1, 1.25f })
                {
                    foreach (float exposure in new[] { .85f, 1.2f })
                    {
                        foreach (float retention in new[] { 0f, .35f, .7f })
                        {
                            Evaluate(prior with { Sky = sky, Ground = ground, Sun = sun, Exposure = exposure, Retention = retention });
                        }
                    }
                }
            }
        }
        Trial[] seeds = history.Values.Where(item => item.Eligible).OrderBy(item => item.Loss).Take(2).ToArray();
        Require(seeds.Length == 2, "Fewer than two eligible coarse calibration seeds.");
        foreach (Trial seed in seeds)
        {
            Trial current = seed;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int dimension = 0; dimension < 14; dimension++)
                {
                    Parameters anchor = current.Parameters;
                    foreach (int direction in new[] { -1, 1 })
                    {
                        Trial trial = Evaluate(Perturb(anchor, dimension, direction, pass));
                        if (trial.Eligible && trial.Loss < current.Loss)
                        {
                            current = trial;
                        }
                    }
                }
            }
        }
        Trial[] finalists = history.Values.Where(item => item.Eligible).OrderBy(item => item.Loss).Take(5).ToArray();
        Trial[] timed = new[] { new Trial(prior, controls["default-path"].Profile, controls["default-path"].Scores),
            new Trial(prior, previousPath, controls["previous-path"].Scores) }.Concat(finalists).ToArray();
        GameView[] timedViews = timed.Select(trial => Apply(views[0], trial.Profile)).ToArray();
        for (int cycle = 0; cycle < 35; cycle++)
        {
            for (int index = 0; index < timed.Length; index++)
            {
                int slot = (index + cycle) % timed.Length;
                renderer.Settings = timed[slot].Profile.Graphics;
                var frame = renderer.Render(timedViews[slot].Scene, timedViews[slot].Camera, timedViews[slot].Eye,
                    new(.56f, .74f, .93f, 1));
                if (cycle >= 5)
                {
                    timed[slot].GpuMilliseconds.Add(frame.GpuPassTimes.Sum(pass => pass.Milliseconds));
                }
            }
        }
        Require(timed[0].Median > 0, "GPU timestamps unavailable.");
        Trial winner = finalists.First(item => item.Median <= timed[0].Median * 1.25 + .15);
        Write(output, "selection.json", new
        {
            winner.Parameters,
            winner.Loss,
            winner.Median,
            Evaluations = history.Count,
            Diffuse = "PathDiffuse",
            Scope = "Frozen before heldout reference is loaded for scoring."
        });
        Write(output, "selected-profile.json", winner.Profile with { Graphics = winner.Profile.Graphics with { AntiAliasing = AntiAliasing3D.Temporal } });
        string profileHash = Hash(Path.Combine(output, "selected-profile.json"));
        var heldout = new SemanticLookMetric(output, "heldout");
        var comparisons = new List<object>();
        var rows = new List<byte[][]>();
        SemanticLookScore[] heldoutScores = new SemanticLookScore[3];
        for (int index = 0; index < views.Length; index++)
        {
            SemanticLookMetric metric = index < 2 ? metrics[index] : heldout;
            var images = new List<byte[]>();
            CloudstepLookProfile[] profiles = [controls["default-path"].Profile, previousPath, winner.Profile];
            for (int policy = 0; policy < profiles.Length; policy++)
            {
                renderer.Settings = profiles[policy].Graphics;
                byte[] pixels = Render(renderer, Apply(views[index], profiles[policy]));
                images.Add(pixels);
                string name = new[] { "default-path", "previous-path", "selected" }[policy];
                Save(output, Splits[index] + "-" + name + ".png", pixels);
                SemanticLookScore score = metric.Measure(pixels);
                comparisons.Add(new { Split = Splits[index], Policy = name, Score = score });
                if (index == 2)
                {
                    heldoutScores[policy] = score;
                }
            }
            rows.Add([images[0], images[1], metric.Target, images[2]]);
        }
        var stress = new List<object>();
        foreach (float exposure in new[] { .5f, 1, 2 })
        {
            foreach (float sun in new[] { .25f, 1, 2 })
            {
                renderer.Settings = winner.Profile.Graphics with
                {
                    Exposure = winner.Profile.Graphics.Exposure * exposure,
                    SunIntensity = winner.Profile.Graphics.SunIntensity * sun,
                };
                byte[] pixels = Render(renderer, Apply(views[0], winner.Profile));
                Save(output, $"stress-e{exposure}-s{sun}.png", pixels);
                stress.Add(new { Exposure = exposure, Sun = sun, Score = metrics[0].Measure(pixels) });
            }
        }
        double trainingGain = 1 - winner.Loss / controls["previous-path"].Scores.Average(score => score.Loss);
        double transferGain = 1 - heldoutScores[2].Loss / heldoutScores[1].Loss;
        bool allRegions = heldoutScores[2].Regions.Zip(heldoutScores[0].Regions).All(pair => pair.First < pair.Second);
        Require(profileHash == Hash(Path.Combine(output, "selected-profile.json")), "Heldout scoring changed the selected profile.");
        Sheet(Path.Combine(output, "comparison.png"), rows, ["DEFAULT PATH", "PREVIOUS + PATH", "AI SWATCH REFERENCE", "CALIBRATED PATH"]);
        Write(output, "evidence.json", new
        {
            Completed = true,
            CriterionPassed = trainingGain >= .1 && transferGain >= .05 && allRegions && heldoutScores[2].ClippedChannelFraction <= .001,
            Device = plant.Facts.PhysicalDeviceName,
            plant.Facts.EnabledValidationLayers,
            TrainingGainOverPreviousPath = trainingGain,
            HeldoutGainOverPreviousPath = transferGain,
            EveryHeldoutRegionImprovesOverDefault = allRegions,
            Evaluations = history.Count,
            Selected = winner.Parameters,
            SelectedProfileSha256 = profileHash,
            RegionOrder = new[] { "Hero", "Grass", "Earth", "Structure" },
            Controls = controls.Select(pair => new
            {
                Name = pair.Key,
                pair.Value.Profile,
                pair.Value.Scores,
                Loss = pair.Value.Scores.Average(score => score.Loss)
            }),
            Comparisons = comparisons,
            MetricControls = metrics.Append(heldout).Select(metric => metric.Qualify()).ToArray(),
            Timings = timed.Select(item => new { item.Profile, item.Median, Samples = item.GpuMilliseconds }),
            Stress = stress,
            History = history.Values.Select(item => new { item.Parameters, item.Loss, item.Eligible }),
            ReferenceHashes = ReferenceHashes(output),
            Limits = "Independent manually labelled material swatches. Marginal Lab distributions do not prove spatial shading, realism, texture quality or human preference. One fresh generated reference; annotations are not independent human labels. GPU timing excludes CPU presentation work. PathDiffuse fixed; no diffuse ranking or runtime inference.",
        });
        Console.WriteLine($"AURELIAN_SEMANTIC_LOOK_COMPLETED evaluations={history.Count} trainGain={trainingGain:P2} heldoutGain={transferGain:P2}");
    }

    private static void VerifyOcclusion(string output, VulkanSolid3DRenderer renderer, LookView[] views, Graphics3DSettings authored)
    {
        string profileHash = Hash(Path.Combine(output, "selected-profile.json"));
        var selected = JsonSerializer.Deserialize<CloudstepLookProfile>(File.ReadAllText(Path.Combine(output, "selected-profile.json")), Json)!;
        var previous = JsonSerializer.Deserialize<CloudstepLookProfile>(File.ReadAllText(Path.Combine(output, "previous-profile.json")), Json)!;
        CloudstepLookProfile[] profiles =
        [
            new() { Graphics = authored },
            previous with { Graphics = previous.Graphics with { DirectDiffuseModel = DirectDiffuseModel3D.PathDiffuse } },
            selected,
        ];
        var scores = new List<SemanticLookScore[]>();
        var controls = new List<object>();
        var maskHashes = new List<string>();
        for (int index = 0; index < views.Length; index++)
        {
            renderer.Settings = Graphics3DSettings.Basic with { SunDirection = Vector3.UnitZ };
            byte[] mask = renderer.Render(RegionScene(views[index].Frame, includeBackgroundOccluders: true), views[index].View.Camera,
                views[index].View.Eye, new(0, 0, 0, 1), capture: true).Pixels!;
            string maskName = Splits[index] + "-occlusion-regions.png";
            Save(output, maskName, mask);
            maskHashes.Add(Hash(Path.Combine(output, maskName)));
            var metric = new SemanticLookMetric(output, Splits[index], mask);
            controls.Add(metric.Qualify());
            var viewScores = new List<SemanticLookScore>();
            foreach (CloudstepLookProfile profile in profiles)
            {
                renderer.Settings = profile.Graphics with { AntiAliasing = AntiAliasing3D.None };
                viewScores.Add(metric.Measure(Render(renderer, Apply(views[index], profile))));
            }
            scores.Add(viewScores.ToArray());
        }
        double trainGain = 1 - scores.Take(2).Average(view => view[2].Loss) / scores.Take(2).Average(view => view[1].Loss);
        double heldoutGain = 1 - scores[2][2].Loss / scores[2][1].Loss;
        bool allRegions = scores[2][2].Regions.Zip(scores[2][0].Regions).All(pair => pair.First < pair.Second);
        Require(profileHash == Hash(Path.Combine(output, "selected-profile.json")), "Occlusion confirmation changed the selected profile.");
        Write(output, "occlusion-confirmation.json", new
        {
            Completed = true,
            CriterionPassed = trainGain >= .1 && heldoutGain >= .05 && allRegions && scores[2][2].ClippedChannelFraction <= .001,
            TrainingGainOverPreviousPath = trainGain,
            HeldoutGainOverPreviousPath = heldoutGain,
            EveryHeldoutRegionImprovesOverDefault = allRegions,
            ProfileSha256 = profileHash,
            SearchOrReranking = false,
            SourceMaskHashes = maskHashes,
            Scores = scores,
            MetricControls = controls,
            Scope = "Post-selection fixed-policy confirmation after correcting source-mask occlusion. Original frozen search and evidence are preserved. Not an independent new trial.",
        });
        Console.WriteLine($"AURELIAN_OCCLUSION_CONFIRMATION_COMPLETED trainGain={trainGain:P2} heldoutGain={heldoutGain:P2}");
    }

    private static Parameters Perturb(Parameters source, int dimension, int direction, int pass)
    {
        float factor = MathF.Exp(direction * .3f * MathF.Pow(.5f, pass));
        if (dimension == 0)
        {
            return source with { Sky = Math.Clamp(source.Sky * factor, .05f, 1.5f) };
        }
        if (dimension == 1)
        {
            return source with { Ground = Math.Clamp(source.Ground * factor, .05f, 1.5f) };
        }
        if (dimension == 2)
        {
            return source with { Sun = Math.Clamp(source.Sun * factor, .25f, 2) };
        }
        if (dimension == 3)
        {
            return source with { Exposure = Math.Clamp(source.Exposure * factor, .3f, 1.8f) };
        }
        if (dimension == 4)
        {
            return source with { Retention = Math.Clamp(source.Retention + direction * .2f * MathF.Pow(.5f, pass), 0, 1) };
        }
        int group = (dimension - 5) / 3;
        int channel = (dimension - 5) % 3;
        Vector3 tint = group switch
        {
            0 => source.Grass,
            1 => source.Earth,
            _ => source.Hero,
        };
        if (channel == 0)
        {
            tint.X = Math.Clamp(tint.X * factor, .25f, 4);
        }
        if (channel == 1)
        {
            tint.Y = Math.Clamp(tint.Y * factor, .25f, 4);
        }
        if (channel == 2)
        {
            tint.Z = Math.Clamp(tint.Z * factor, .25f, 4);
        }
        return group switch
        {
            0 => source with { Grass = tint },
            1 => source with { Earth = tint },
            _ => source with { Hero = tint },
        };
    }

    private static Dictionary<string, string> ReferenceHashes(string output)
    {
        var files = Splits.SelectMany(split => new[] { split + "-target.png", split + "-patches.json", split + "-regions.png" })
            .Concat(["protocol.json", "reference-review.json", "previous-profile.json"]);
        return files.ToDictionary(name => name, name => Hash(Path.Combine(output, name)));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Write(string output, string name, object value) => ImageGuidedLookResearch.WriteJson(Path.Combine(output, name), value);
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
    private sealed record Parameters(float Sky, float Ground, float Sun, float Exposure, float Retention,
        Vector3 Grass, Vector3 Earth, Vector3 Hero);
    private sealed record Trial(Parameters Parameters, CloudstepLookProfile Profile, SemanticLookScore[] Scores)
    {
        public double Loss => Scores.Average(score => score.Loss);
        public bool Eligible => Scores.All(score => score.ClippedChannelFraction <= .001);
        public List<double> GpuMilliseconds { get; } = [];
        public double Median => GpuMilliseconds.Order().ElementAt(GpuMilliseconds.Count / 2);
    }
}
