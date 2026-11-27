using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const int DenoisingWidth = 192, DenoisingHeight = 128;
    private const int DenoisingWarmup = 12, DenoisingFrames = 24;
    private sealed record DenoisingVariant(string Name, bool Enabled = false, float HistoryCap = 1);
    private sealed record DenoisingFrame(Native3DScene Scene, ReferenceCamera3D Camera, Matrix4x4 Clip);
    private sealed record DenoisingCapture(List<byte[]> Images, string SampleHash, List<uint[]> Identities);
    private sealed record DenoisingMetrics(double DisplayRmse, double ResidualDeltaRmse, double GhostEnergy,
        double ExcessHistoryGhostEnergy, int VacatedPixels);

    public static void RunLightDenoising(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Sampled direct light denoising"));
        Require(initialized.Success, "Light denoising needs Vulkan ray queries.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("denoising-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var scene = Scene(box, sphere, 0);
        DenoisingVariant[] variants = [new("taa"), new("taa-50", HistoryCap: .5f), new("denoised", Enabled: true)];
        var rows = new List<object>();
        var pairs = new List<object>();
        bool accepted = true;
        foreach (var (specimen, mode) in new[]
        {
            ("static", LocalLightSamplingMode3D.Importance),
            ("camera-pan", LocalLightSamplingMode3D.SpatiotemporalReservoir),
            ("moving-receiver", LocalLightSamplingMode3D.Importance),
            ("moving-shadow", LocalLightSamplingMode3D.Importance),
        })
        {
            var sequence = DenoisingSequence(scene, specimen);
            var lights = Lights();
            if (specimen == "moving-shadow")
            {
                lights = [..lights.Take(64).Select(light => light with { Intensity = .3f }),
                    new(new(-2, 4, 2), new(1, .8f, .6f), 26, 14) { CastShadows = true }];
            }
            bool isolateShadow = specimen == "moving-shadow";
            var exact = CaptureDenoising(plant, assets, sequence, lights, LocalLightSamplingMode3D.Exact, variants[0],
                cold: true, readIdentities: isolateShadow);
            foreach (int frame in new[] { 8, 16, 24 })
                WriteDenoisingImage($"{specimen}-reference-{frame}.png", exact, frame);
            var shadowMasks = StableFloorPixels(exact.Identities);
            DenoisingMetrics? baseline = null;
            DenoisingMetrics? baselineShadow = null;
            string? sampleHash = null;
            foreach (var variant in variants)
            {
                var warm = CaptureDenoising(plant, assets, sequence, lights, mode, variant, cold: false);
                var cold = CaptureDenoising(plant, assets, sequence, lights, mode, variant, cold: true);
                sampleHash ??= warm.SampleHash;
                Require(warm.SampleHash == sampleHash && cold.SampleHash == sampleHash,
                    "The sampled-light packet changed between matched denoiser controls.");
                var metrics = MeasureDenoising(warm.Images, exact.Images, cold.Images);
                baseline ??= metrics;
                var shadow = isolateShadow ? MeasureDenoising(warm.Images, exact.Images, cold.Images, shadowMasks) : null;
                baselineShadow ??= shadow;
                if (variant.Enabled)
                {
                    accepted &= metrics.DisplayRmse < baseline.DisplayRmse * .98
                        && metrics.ResidualDeltaRmse < baseline.ResidualDeltaRmse * .98;
                    accepted &= PassesGhostGate(metrics, baseline);
                    if (shadow is not null) accepted &= PassesGhostGate(shadow, baselineShadow!);
                }
                rows.Add(new { Specimen = specimen, Mode = mode.ToString(), Variant = variant.Name, Metrics = metrics, StableFloor = shadow, warm.SampleHash });
                string name = specimen + "-" + variant.Name;
                foreach (int frame in new[] { 8, 16, 24 })
                {
                    string file = $"{name}-{frame}.png";
                    WriteDenoisingImage(file, warm, frame);
                    pairs.Add(new { Name = $"{name}-{frame}", Reference = $"{specimen}-reference-{frame}.png", Test = file, Group = name });
                }
                WriteDenoisingImage(name + "-cold.png", cold, DenoisingFrames);
                Console.WriteLine($"Denoising {name}: {metrics}");
                if (shadow is not null) Console.WriteLine($"Denoising {name} stable floor: {shadow}");
            }
        }
        pairs.Add(new { Name = "identity", Reference = "static-reference-24.png", Test = "static-reference-24.png", Group = "identity" });
        object correctness = CheckDenoisingHeadAndReset(plant, assets, scene);
        object timing = MeasureDenoisingCost(plant, assets, scene);
        validation.RequireClean();
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = accepted,
            Device = plant.Facts.PhysicalDeviceName,
            Width = DenoisingWidth, Height = DenoisingHeight, Warmup = DenoisingWarmup, Frames = DenoisingFrames,
            RaysPerAllocatedPixel = 2, Quality = rows, Correctness = correctness, Timing = timing,
        }, json));
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "sampled-light-denoising", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));
        Require(accepted, "The spatial denoiser failed an image error, noise or ghost-energy gate.");
        Console.WriteLine("AURELIAN_LIGHT_DENOISING_PASSED " + output);

        void WriteDenoisingImage(string file, DenoisingCapture capture, int frame)
            => NativeGameGraphics.WritePng(Path.Combine(output, file), DenoisingWidth, DenoisingHeight, capture.Images[frame]);
    }

    private static bool PassesGhostGate(DenoisingMetrics actual, DenoisingMetrics baseline)
        => actual.VacatedPixels > 100
            && actual.GhostEnergy <= baseline.GhostEnergy + .000001
            && actual.ExcessHistoryGhostEnergy <= baseline.ExcessHistoryGhostEnergy + .000001;

    private static List<DenoisingFrame> DenoisingSequence(Native3DScene scene, string specimen)
    {
        var sequence = new List<DenoisingFrame>();
        NativeInstanceSet3D? previous = null;
        for (int frame = -DenoisingWarmup; frame < DenoisingFrames; frame++)
        {
            int step = Math.Max(frame, 0);
            var camera = Camera;
            if (specimen == "camera-pan")
            {
                var displacement = Vector3.UnitX * (.95f * MathF.Sin(step * .24f));
                camera = camera with { Eye = camera.Eye + displacement, Target = camera.Target + displacement };
            }
            var items = scene.Instances.Select(instance =>
            {
                if (specimen == "moving-receiver" && instance.Identity == "moving")
                {
                    var motion = Matrix4x4.CreateTranslation(.18f * step, 0, 0);
                    return instance with { Transform = instance.Transform * motion };
                }
                if (specimen == "moving-shadow" && instance.Identity == "column-2")
                {
                    return instance with { RayMask = step / 4 % 2 == 0 ? uint.MaxValue : 0 };
                }
                return instance;
            });
            previous = new NativeInstanceSet3D(items, previous);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians,
                (float)DenoisingWidth / DenoisingHeight, .1f, 100);
            projection.M22 *= -1;
            var clip = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
            sequence.Add(new(scene with { Instances = previous }, camera, clip));
        }
        return sequence;
    }

    private static DenoisingCapture CaptureDenoising(AurelianVulkanPlant plant, GameAssets assets, List<DenoisingFrame> sequence,
        LocalLight3D[] lights, LocalLightSamplingMode3D mode, DenoisingVariant variant, bool cold, bool readIdentities = false)
    {
        using var target = new VulkanNativeFrameTarget(plant, DenoisingWidth, DenoisingHeight);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, mode);
        renderer.LocalLights = lights;
        renderer.Settings = Settings() with
        {
            AntiAliasing = AntiAliasing3D.Temporal,
            TemporalHistoryWeight = cold ? 0 : Math.Min(variant.HistoryCap, Graphics3DSettings.Default.TemporalHistoryWeight),
        };
        int budget = 2 * DenoisingWidth * DenoisingHeight;
        renderer.LocalLightSampling = renderer.LocalLightSampling with { FrameRayBudget = budget, DenoiseSampledLighting = variant.Enabled };
        var images = new List<byte[]>();
        var identities = new List<uint[]>();
        using var packetHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int index = 0; index < sequence.Count; index++)
        {
            var frame = sequence[index];
            bool capture = index >= DenoisingWarmup - 1;
            var rendered = renderer.Render(frame.Scene, frame.Clip, frame.Camera.Eye, Clear, capture: capture);
            if (!capture) continue;
            images.Add(rendered.Pixels!);
            if (readIdentities) identities.Add(renderer.ReadIdentitiesForQualification().Ids);
            if (mode == LocalLightSamplingMode3D.Exact) continue;
            float[] samples = renderer.ReadLightSamplesForQualification();
            Require(SumLane(samples, 9) == budget && SumLane(samples, 8) <= budget, "Denoising exceeded the shared ray budget.");
            packetHash.AppendData(MemoryMarshal.AsBytes(samples.AsSpan()));
        }
        return new(images, Convert.ToHexString(packetHash.GetHashAndReset()), identities);
    }

    private static List<bool[]> StableFloorPixels(List<uint[]> identities)
    {
        var masks = new List<bool[]>();
        for (int frame = 1; frame < identities.Count; frame++)
        {
            var mask = new bool[DenoisingWidth * DenoisingHeight];
            for (int pixel = 0; pixel < mask.Length; pixel++)
                mask[pixel] = identities[frame][pixel * 2] == 1 && identities[frame - 1][pixel * 2] == 1;
            masks.Add(mask);
        }
        return masks;
    }

    private static DenoisingMetrics MeasureDenoising(List<byte[]> actual, List<byte[]> reference, List<byte[]> cold,
        List<bool[]>? ghostMasks = null)
    {
        double rmseSquared = 0, residualDeltaSquared = 0, ghost = 0, historyGhost = 0;
        int vacated = 0;
        for (int frame = 1; frame < actual.Count; frame++)
        {
            var metrics = ScalingError(actual[frame], reference[frame], reference[frame - 1], cold[frame],
                ghostMasks is null ? default : ghostMasks[frame - 1]);
            rmseSquared += metrics.LinearDisplayRmse * metrics.LinearDisplayRmse;
            ghost += metrics.GhostEnergy * metrics.VacatedPixels;
            historyGhost += metrics.ExcessHistoryGhostEnergy!.Value * metrics.VacatedPixels;
            vacated += metrics.VacatedPixels;
            for (int offset = 0; offset < actual[frame].Length; offset += 4)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int at = offset + channel;
                    double residual = ScalingSrgb[actual[frame][at]] - ScalingSrgb[reference[frame][at]];
                    double priorResidual = ScalingSrgb[actual[frame - 1][at]] - ScalingSrgb[reference[frame - 1][at]];
                    double delta = residual - priorResidual;
                    residualDeltaSquared += delta * delta;
                }
            }
        }
        return new(Math.Sqrt(rmseSquared / DenoisingFrames),
            Math.Sqrt(residualDeltaSquared / (DenoisingFrames * DenoisingWidth * DenoisingHeight * 3)),
            ghost / Math.Max(vacated, 1), historyGhost / Math.Max(vacated, 1), vacated);
    }

    private static object CheckDenoisingHeadAndReset(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene)
    {
        using var target = new VulkanNativeFrameTarget(plant, DenoisingWidth, DenoisingHeight);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Hybrid);
        renderer.Settings = Settings() with { AntiAliasing = AntiAliasing3D.Temporal };
        renderer.LocalLights = [new(new(0, 3, 1), Vector3.One, 6, 10) { CastShadows = true }];
        renderer.LocalLightSampling = renderer.LocalLightSampling with { DenoiseSampledLighting = true };
        var clip = Matrix(DenoisingWidth, DenoisingHeight);
        renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
        float[] head = renderer.ReadLocalHeadForQualification().LinearRgb;
        Require(head.Any(value => value > 0) && head.SequenceEqual(renderer.ReadLightingForQualification().LinearRgb),
            "The spatial filter changed the exact light head when there was no stochastic contribution.");

        renderer.LocalLights = Lights();
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, clip, Camera.Eye, Clear);
        renderer.ResetTemporalHistory();
        byte[] cut = renderer.Render(scene, clip, Camera.Eye, Clear, capture: true).Pixels!;
        renderer.ResetTemporalHistory();
        byte[] repeat = renderer.Render(scene, clip, Camera.Eye, Clear, capture: true).Pixels!;
        Require(cut.SequenceEqual(repeat), "Camera-cut reset changed the denoised first frame.");
        return new { ExactHeadIdentical = true, CameraCutReset = true };
    }

    private static object MeasureDenoisingCost(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene)
    {
        const int width = 1280, height = 720, warmup = 24, measured = 48;
        var clip = Matrix(width, height);
        var rows = new List<object>();
        foreach (bool filter in new[] { false, true, true, false })
        {
            using var target = new VulkanNativeFrameTarget(plant, width, height);
            using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Importance);
            renderer.Settings = Settings() with { AntiAliasing = AntiAliasing3D.Temporal };
            renderer.LocalLightSampling = renderer.LocalLightSampling with
            {
                FrameRayBudget = 2 * width * height,
                DenoiseSampledLighting = filter,
            };
            var milliseconds = new List<double>(measured);
            for (int frame = 0; frame < warmup + measured + 2; frame++)
            {
                var result = renderer.Render(scene, clip, Camera.Eye, Clear);
                if (result.CompletedGpuFrame is { Sequence: >= warmup } completed && completed.Sequence < warmup + measured)
                    milliseconds.Add(completed.ElapsedMilliseconds);
            }
            Require(milliseconds.Count == measured, "The lighting timing run missed Vulkan frame timestamps.");
            renderer.WaitForPendingFrames();
            double median = Median(milliseconds);
            rows.Add(new { Denoising = filter, Width = width, Height = height, MedianGpuMs = median, FrameGpuMs = milliseconds });
            Console.WriteLine($"Denoising 720p filter={filter}: {median:F3} ms GPU median.");
        }
        return rows;
    }
}
