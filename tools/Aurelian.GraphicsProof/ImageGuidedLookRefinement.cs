using System.Collections.Immutable;
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
using Aurelian.World.Scenes;
using Cloudstep;
using SkiaSharp;

/// <summary>Second, material-balanced trial. Two previous views train; a new view is held out.</summary>
internal static class ImageGuidedLookRefinement
{
    private static readonly NativeFrameClearColor Clear = new(.56f, .74f, .93f, 1);
    private static readonly JsonSerializerOptions Json = ImageGuidedLookResearch.Json;
    private static readonly string[] Splits = ["train0", "train1", "heldout"];
    private const int Width = ImageGuidedLookResearch.Width;
    private const int Height = ImageGuidedLookResearch.Height;

    public static void Run(string output, bool fit)
    {
        Directory.CreateDirectory(output);
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian look refinement"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = ImageGuidedLookResearch.CreateRenderer(plant, assets, target);
        using var original = new CloudstepGame();
        Graphics3DSettings baseline = original.Manifest.Graphics with
        {
            AntiAliasing = AntiAliasing3D.None,
            DirectDiffuseModel = DirectDiffuseModel3D.Lambert,
        };
        LookView[] views = [View(0), View(1), View(2)];
        if (!fit)
        {
            Require(!File.Exists(Path.Combine(output, "reference-lock.json")), "Use a new capture directory for another experiment.");
            Write(output, "protocol.json", new
            {
                Version = 2,
                Training = "Previous checkpoint 0 and 1 targets, both now explicitly training data.",
                Heldout = "New checkpoint 2 camera: yaw +0.6 radians, pitch 0.45, simulation time zero. Never scored during selection.",
                Objective = "Equal weight for hero, grass, earth and structure. Each region averages 24x14 patch Lab RMSE (0.8) and its 10th/90th percentile L error (0.2).",
                Search = "216 coarse profiles: decode on/off, Lambert/Frostbite/PathDiffuse, ambient 0.12/0.35/0.65/1, exposure 0.55/0.85/1.2, retention 0/0.5/1. Soft gamut compression enabled. Then three coordinate passes on two finalists; palette multipliers 0.25..4. Hard budget 420 evaluations.",
                RefineBounds = "Ambient 0.05..1.25, exposure 0.3..1.8, retention 0..1, each material RGB multiplier 0.25..4. Best two distinct diffuse models seed refinement. Step sizes shrink each pass.",
                Gates = "Foreground clipped-channel fraction <= 0.001 on both training views. Winner timing <= original * 1.25 + 0.15 ms, 30 rotating samples after warmup. Heldout checked only after selection.",
                Success = "At least 30% balanced-error improvement over the previous look on training and 20% on the new heldout; each heldout material must improve over the original. No human perceptual claim.",
                Stress = "Exposure 0.5x/1x/2x and sun 0.25x/1x/2x after selection; native complete-course replay must retain simulation observations.",
                Selection = "CPU derivative-free search through actual Vulkan captures. Stable softmax over distinct finalists at temperature 1 reports score ambiguity, not a learned neural model or stochastic gameplay selection.",
                ViewHashes = views.Select(ViewHash).ToArray(),
            });
            for (int index = 0; index < views.Length; index++)
            {
                renderer.Settings = baseline;
                Save(output, Splits[index] + "-baseline.png", Render(renderer, views[index].View));
                renderer.Settings = Graphics3DSettings.Basic with { SunDirection = Vector3.UnitZ };
                var mask = RegionScene(views[index].Frame);
                byte[] pixels = renderer.Render(mask, views[index].View.Camera, views[index].View.Eye,
                    new(0, 0, 0, 1), capture: true).Pixels!;
                Save(output, Splits[index] + "-regions.png", pixels);
            }
            File.Copy("docs/Aurelian/assets/image-guided-look/train-target.png", Path.Combine(output, "train0-target.png"), overwrite: false);
            File.Copy("docs/Aurelian/assets/image-guided-look/heldout-target.png", Path.Combine(output, "train1-target.png"), overwrite: false);
            Console.WriteLine("AURELIAN_LOOK_REFINEMENT_CAPTURE_PASSED " + output);
            return;
        }
        Fit(output, plant, renderer, views, baseline);
    }

    internal static LookView View(int checkpoint)
    {
        using var game = new CloudstepGame();
        game.NewGame();
        Require(game.Command($"checkpoint {checkpoint}"), "Invalid checkpoint.");
        if (checkpoint == 2)
        {
            game.World.CameraYaw += .6f;
            game.World.CameraPitch = .45f;
        }
        GameView view = game.View(new(Width, Height))!;
        return new(view, game.InspectFrame());
    }

    internal static string ViewHash(LookView view)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(view.View, Json)));
    }

    internal static Native3DScene RegionScene(SceneFrame frame, bool includeBackgroundOccluders = false)
    {
        Vector4 Code(CloudstepLookRegion region) => new((int)region / 8f, 0, 0, 1);
        var boxes = frame.Boxes.Where(box => includeBackgroundOccluders || CloudstepPalette.Region(box.Id, box.Color, box.Material) != CloudstepLookRegion.Background)
            .Select(box => box with { Color = Code(CloudstepPalette.Region(box.Id, box.Color, box.Material)), Material = null }).ToImmutableArray();
        var meshes = frame.Meshes.Where(mesh => includeBackgroundOccluders || CloudstepPalette.Region(mesh.Id, mesh.Vertices[0].Color, mesh.Material) != CloudstepLookRegion.Background)
            .Select(mesh => mesh with
            {
                Vertices = mesh.Vertices.Select(vertex => vertex with
                {
                    Color = Code(CloudstepPalette.Region(mesh.Id, mesh.Vertices[0].Color, mesh.Material)),
                }).ToImmutableArray(),
                Material = null,
            }).ToImmutableArray();
        Native3DScene scene = SceneGeometry3D.BuildScene(new(boxes, meshes));
        return scene with { Geometry = scene.Geometry.Select(vertex => vertex with { Normal = Vector3.UnitZ }).ToArray() };
    }

    internal static GameView Apply(LookView view, CloudstepLookProfile profile)
    {
        return view.View with { Scene = SceneGeometry3D.BuildScene(new CloudstepPalette(profile).Apply(view.Frame)) };
    }

    internal static byte[] Render(VulkanSolid3DRenderer renderer, GameView view)
    {
        return renderer.Render(view.Scene, view.Camera, view.Eye, Clear, capture: true).Pixels!;
    }

    private static void Fit(string output, AurelianVulkanPlant plant, VulkanSolid3DRenderer renderer, LookView[] views, Graphics3DSettings baseline)
    {
        Write(output, "evidence.json", new { Completed = false });
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "protocol.json")));
        string[] viewHashes = protocol.RootElement.GetProperty("ViewHashes").Deserialize<string[]>()!;
        Require(viewHashes.SequenceEqual(views.Select(ViewHash)), "Game view geometry or camera changed after capture.");
        using var review = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "reference-review.json")));
        Require(review.RootElement.GetProperty("Admitted").GetBoolean(), "Reference admission is required.");
        var referenceHashes = Splits.ToDictionary(split => split, split => Hash(Path.Combine(output, split + "-target.png")));
        referenceHashes["protocol"] = Hash(Path.Combine(output, "protocol.json"));
        referenceHashes["review"] = Hash(Path.Combine(output, "reference-review.json"));
        string lockPath = Path.Combine(output, "reference-lock.json");
        if (File.Exists(lockPath))
        {
            var locked = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(lockPath))!;
            Require(referenceHashes.All(pair => locked.TryGetValue(pair.Key, out string? value) && value == pair.Value), "Frozen inputs changed.");
        }
        else
        {
            Write(output, "reference-lock.json", referenceHashes);
        }
        var metrics = new[] { new MaterialLookMetric(output, "train0"), new MaterialLookMetric(output, "train1") };
        object[] metricControls = metrics.Select(metric => metric.Qualify()).ToArray();
        var history = new Dictionary<LookParameters, LookTrial>();
        CloudstepLookProfile Profile(LookParameters parameters)
        {
            return new()
            {
                DecodePaletteSrgb = parameters.Decode,
                GrassTint = parameters.Grass,
                EarthTint = parameters.Earth,
                HeroTint = parameters.Hero,
                Graphics = baseline with
                {
                    DirectDiffuseModel = parameters.Diffuse,
                    SkyAmbient = baseline.SkyAmbient * parameters.Ambient,
                    GroundAmbient = baseline.GroundAmbient * parameters.Ambient,
                    Exposure = parameters.Exposure,
                    ToneMapColorRetention = parameters.Retention,
                    ToneMapGamutCompression = parameters.Compress,
                },
            };
        }
        LookTrial Evaluate(LookParameters parameters)
        {
            if (history.TryGetValue(parameters, out LookTrial? cached))
            {
                return cached;
            }
            Require(history.Count < 420, "The frozen evaluation budget was exhausted.");
            CloudstepLookProfile profile = Profile(parameters);
            profile.Validate();
            renderer.Settings = profile.Graphics;
            var scores = new MaterialLookScore[2];
            for (int index = 0; index < 2; index++)
            {
                byte[] pixels = Render(renderer, Apply(views[index], profile));
                scores[index] = metrics[index].Measure(pixels);
            }
            var result = new LookTrial(parameters, scores);
            history.Add(parameters, result);
            if (history.Count % 40 == 0)
            {
                Console.WriteLine($"LOOK_REFINEMENT_EVALUATIONS {history.Count} best={history.Values.Where(item => item.ClipEligible).Min(item => item.Loss):F4}");
            }
            return result;
        }
        var originalParameters = new LookParameters(false, DirectDiffuseModel3D.Lambert, 1, baseline.Exposure, 0, false,
            Vector3.One, Vector3.One, Vector3.One);
        LookTrial original = Evaluate(originalParameters);
        LookTrial previous = Evaluate(originalParameters with { Diffuse = DirectDiffuseModel3D.PathDiffuse, Retention = .7f });
        var ablations = new Dictionary<string, LookTrial>
        {
            ["original"] = original,
            ["previous"] = previous,
            ["decode-only"] = Evaluate(originalParameters with { Decode = true }),
            ["ambient-only"] = Evaluate(originalParameters with { Ambient = .35f }),
            ["gamut-only"] = Evaluate(previous.Parameters with { Compress = true }),
        };
        foreach (bool decode in new[] { false, true })
        {
            foreach (var model in new[] { DirectDiffuseModel3D.Lambert, DirectDiffuseModel3D.Frostbite, DirectDiffuseModel3D.PathDiffuse })
            {
                foreach (float ambient in new[] { .12f, .35f, .65f, 1 })
                {
                    foreach (float exposure in new[] { .55f, .85f, 1.2f })
                    {
                        foreach (float retention in new[] { 0f, .5f, 1 })
                        {
                            Evaluate(originalParameters with
                            {
                                Decode = decode,
                                Diffuse = model,
                                Ambient = ambient,
                                Exposure = exposure,
                                Retention = retention,
                                Compress = true
                            });
                        }
                    }
                }
            }
        }
        LookTrial[] seeds = history.Values.Where(item => item.ClipEligible).OrderBy(item => item.Loss)
            .DistinctBy(item => item.Parameters.Diffuse).Take(2).ToArray();
        foreach (LookTrial seed in seeds)
        {
            LookTrial current = seed;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int dimension = 0; dimension < 12; dimension++)
                {
                    LookParameters anchor = current.Parameters;
                    foreach (int direction in new[] { -1, 1 })
                    {
                        LookTrial trial = Evaluate(Perturb(anchor, dimension, direction, pass));
                        if (trial.ClipEligible && trial.Loss < current.Loss)
                        {
                            current = trial;
                        }
                    }
                }
            }
        }
        LookTrial[] finalists = history.Values.Where(item => item.ClipEligible).OrderBy(item => item.Loss).Take(8).ToArray();
        LookTrial[] timed = new[] { original, previous }.Concat(finalists).Distinct().ToArray();
        GameView[] timedViews = timed.Select(trial => Apply(views[0], Profile(trial.Parameters))).ToArray();
        for (int cycle = 0; cycle < 35; cycle++)
        {
            for (int index = 0; index < timed.Length; index++)
            {
                int slot = (index + cycle) % timed.Length;
                renderer.Settings = Profile(timed[slot].Parameters).Graphics;
                var frame = renderer.Render(timedViews[slot].Scene, timedViews[slot].Camera, timedViews[slot].Eye, Clear);
                if (cycle >= 5)
                {
                    timed[slot].GpuMilliseconds.Add(frame.GpuPassTimes.Sum(pass => pass.Milliseconds));
                }
            }
        }
        Require(original.MedianGpuMilliseconds > 0, "GPU timestamps unavailable.");
        LookTrial winner = finalists.First(item => item.MedianGpuMilliseconds <= original.MedianGpuMilliseconds * 1.25 + .15);
        double minimum = finalists.Min(item => item.Loss);
        double[] probabilities = finalists.Select(item => Math.Exp(-(item.Loss - minimum))).ToArray();
        double normalization = probabilities.Sum();
        probabilities = probabilities.Select(value => value / normalization).ToArray();
        Write(output, "selection.json", new
        {
            winner.Parameters,
            winner.Loss,
            SoftmaxTemperature = 1,
            Probabilities = probabilities,
            Finalists = finalists.Select(item => new { item.Parameters, item.Loss, item.MedianGpuMilliseconds }),
            Meaning = "CPU score confidence among searched finalists, not a trained neural network or stochastic gameplay choice."
        });
        CloudstepLookProfile selected = Profile(winner.Parameters);
        Write(output, "selected-profile.json", selected with { Graphics = selected.Graphics with { AntiAliasing = AntiAliasing3D.Temporal } });
        // Only now admit the new camera's target to the measurement code.
        var heldout = new MaterialLookMetric(output, "heldout");
        var metricResults = new List<object>(metricControls) { heldout.Qualify() };
        var comparisons = new List<object>();
        var sheetRows = new List<byte[][]>();
        MaterialLookScore? newHeldout = null;
        MaterialLookScore? originalHeldout = null;
        MaterialLookScore? previousHeldout = null;
        for (int index = 0; index < 3; index++)
        {
            MaterialLookMetric metric = index < 2 ? metrics[index] : heldout;
            var images = new List<byte[]>();
            foreach (var entry in new[] { (Label: "original", Trial: original), (Label: "previous", Trial: previous), (Label: "selected", Trial: winner) })
            {
                LookTrial trial = entry.Trial;
                CloudstepLookProfile profile = Profile(trial.Parameters);
                renderer.Settings = profile.Graphics;
                byte[] pixels = Render(renderer, Apply(views[index], profile));
                images.Add(pixels);
                string label = entry.Label;
                Save(output, Splits[index] + "-" + label + ".png", pixels);
                MaterialLookScore score = metric.Measure(pixels);
                comparisons.Add(new { Split = Splits[index], Policy = label, Score = score });
                if (index == 2)
                {
                    if (label == "original")
                    {
                        originalHeldout = score;
                    }
                    if (label == "previous")
                    {
                        previousHeldout = score;
                    }
                    if (label == "selected")
                    {
                        newHeldout = score;
                    }
                }
            }
            sheetRows.Add([images[0], images[1], metric.Target, images[2]]);
        }
        var stress = new List<object>();
        foreach (float exposureScale in new[] { .5f, 1, 2 })
        {
            foreach (float sunScale in new[] { .25f, 1, 2 })
            {
                renderer.Settings = selected.Graphics with
                {
                    Exposure = selected.Graphics.Exposure * exposureScale,
                    SunIntensity = selected.Graphics.SunIntensity * sunScale,
                };
                byte[] pixels = Render(renderer, Apply(views[0], selected));
                Save(output, $"stress-e{exposureScale}-s{sunScale}.png", pixels);
                stress.Add(new { ExposureScale = exposureScale, SunScale = sunScale, Score = metrics[0].Measure(pixels) });
            }
        }
        double trainGain = 1 - winner.Loss / previous.Loss;
        double heldoutGain = 1 - newHeldout!.Loss / previousHeldout!.Loss;
        bool regionsImprove = newHeldout.Regions.Zip(originalHeldout!.Regions).All(pair => pair.First < pair.Second);
        Sheet(Path.Combine(output, "comparison.png"), sheetRows);
        Write(output, "evidence.json", new
        {
            Completed = true,
            CriterionPassed = trainGain >= .3 && heldoutGain >= .2 && regionsImprove && newHeldout.ClippedChannelFraction <= .001,
            Device = plant.Facts.PhysicalDeviceName,
            plant.Facts.EnabledValidationLayers,
            TrainingGainOverPrevious = trainGain,
            HeldoutGainOverPrevious = heldoutGain,
            EachHeldoutRegionImprovedOverOriginal = regionsImprove,
            Evaluations = history.Count,
            RegionOrder = new[] { "Hero", "Grass", "Earth", "Structure" },
            Selected = winner.Parameters,
            TrainingLoss = winner.Loss,
            Ablations = ablations.Select(pair => new { Name = pair.Key, pair.Value.Parameters, pair.Value.Loss, pair.Value.Scores }),
            Timings = timed.Select(item => new { item.Parameters, item.MedianGpuMilliseconds, Samples = item.GpuMilliseconds }),
            MetricControls = metricResults,
            Comparisons = comparisons,
            Stress = stress,
            History = history.Values.Select(item => new { item.Parameters, item.Loss, item.ClipEligible }),
            ReferenceHashes = referenceHashes,
            Limits = "Two training cameras, one new heldout camera; color/contrast proxy rather than human realism. No generated textures, baked target pixels, neural runtime renderer or new geometry. Soft gamut compression is a small linear-RGB surrogate, not ACES 2.",
        });
        Console.WriteLine($"AURELIAN_LOOK_REFINEMENT_COMPLETED evaluations={history.Count} trainGain={trainGain:P2} heldoutGain={heldoutGain:P2} model={winner.Parameters.Diffuse}");
    }

    private static LookParameters Perturb(LookParameters source, int dimension, int direction, int pass)
    {
        float factor = MathF.Exp(direction * .4f * MathF.Pow(.5f, pass));
        if (dimension == 0)
        {
            return source with { Ambient = Math.Clamp(source.Ambient * factor, .05f, 1.25f) };
        }
        if (dimension == 1)
        {
            return source with { Exposure = Math.Clamp(source.Exposure * factor, .3f, 1.8f) };
        }
        if (dimension == 2)
        {
            return source with { Retention = Math.Clamp(source.Retention + direction * .25f * MathF.Pow(.5f, pass), 0, 1) };
        }
        int color = (dimension - 3) / 3;
        int channel = (dimension - 3) % 3;
        Vector3 tint = color switch
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
        return color switch
        {
            0 => source with { Grass = tint },
            1 => source with { Earth = tint },
            _ => source with { Hero = tint },
        };
    }

    internal static void Sheet(string path, List<byte[][]> rows, string[]? columnTitles = null)
    {
        const int TileWidth = 480;
        const int TileHeight = 270;
        using var sheet = new SKBitmap(TileWidth * 4, (TileHeight + 36) * rows.Count);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(16, 20, 27));
        using var font = new SKFont(SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        string[] titles = columnTitles ?? ["ORIGINAL", "PREVIOUS TRIAL", "AI TARGET", "NEW VULKAN LOOK"];
        for (int row = 0; row < rows.Count; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                System.Runtime.InteropServices.Marshal.Copy(rows[row][column], 0, bitmap.GetPixels(), rows[row][column].Length);
                int x = column * TileWidth;
                int y = row * (TileHeight + 36);
                string split = row == 2 ? "HELDOUT" : "TRAIN";
                canvas.DrawText(split + " / " + titles[column], x + 10, y + 25, font, paint);
                canvas.DrawBitmap(bitmap, new SKRect(x, y + 36, x + TileWidth, y + 36 + TileHeight));
            }
        }
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Write(string output, string name, object value) => ImageGuidedLookResearch.WriteJson(Path.Combine(output, name), value);
    internal static void Save(string output, string name, byte[] pixels) => ImageGuidedLookResearch.Save(output, name, pixels);
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
    internal sealed record LookView(GameView View, SceneFrame Frame);
    private sealed record LookParameters(bool Decode, DirectDiffuseModel3D Diffuse, float Ambient, float Exposure,
        float Retention, bool Compress, Vector3 Grass, Vector3 Earth, Vector3 Hero);
    private sealed record LookTrial(LookParameters Parameters, MaterialLookScore[] Scores)
    {
        public double Loss => Scores.Average(score => score.Loss);
        public bool ClipEligible => Scores.All(score => score.ClippedChannelFraction <= .001);
        public List<double> GpuMilliseconds { get; } = [];
        public double MedianGpuMilliseconds => GpuMilliseconds.Order().ElementAt(GpuMilliseconds.Count / 2);
    }
}
