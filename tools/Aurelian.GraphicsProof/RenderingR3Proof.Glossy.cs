using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const int GlossyReferenceSamples = 128;
    private const int GlossyWarmupFrames = 48;

    public static void RunGlossyReflections(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R5b glossy hardware reflections"));
        Require(initialized.Success, "Glossy reflection plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = new EnvironmentRadiance3D(1, 1, [ReflectionSky.X, ReflectionSky.Y, ReflectionSky.Z, 1]);
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        Native3DScene seed = ReflectionScene(curved: false);
        Matrix4x4 camera = ReflectionCamera(ReflectionWidth, ReflectionHeight);
        var pairs = new List<object>();
        var quality = new List<object>();
        object? cameraMotion = null;
        object? cpuReference = null;
        foreach (float roughness in new[] { .12f, .24f, .36f, .44f })
        {
            string name = $"roughness-{(int)MathF.Round(roughness * 100)}";
            var scene = SetReflectionRoughness(seed, roughness);
            using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
            using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
            renderer.Settings = renderer.Settings with { SharpReflections = false, GlossyReflections = GlossyReflectionMode3D.Raw };
            var (reference, one, four, rawDelta) = AccumulateGlossy(renderer, scene, camera);
            SaveSamplingImage(plant, assets, output, name + "-reference", reference);
            SaveSamplingImage(plant, assets, output, name + "-raw1", one);
            SaveSamplingImage(plant, assets, output, name + "-raw4", four);
            double singleRmse = Rmse(one.LinearRgb, reference.LinearRgb);
            double fourRmse = Rmse(four.LinearRgb, reference.LinearRgb);
            renderer.Settings = renderer.Settings with { SharpReflections = true, GlossyReflections = GlossyReflectionMode3D.Off };
            renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
            var sharp = renderer.ReadLightingForQualification();
            double sharpRmse = Rmse(sharp.LinearRgb, reference.LinearRgb);
            SaveSamplingImage(plant, assets, output, name + "-sharp", sharp);
            var variants = new List<object>();
            int fullSamples = 0;
            foreach (var (label, mode) in new[] { ("full", GlossyReflectionMode3D.FullResolution), ("half", GlossyReflectionMode3D.HalfResolution) })
            {
                renderer.Settings = renderer.Settings with { SharpReflections = false, GlossyReflections = mode };
                renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
                var cold = renderer.ReadLightingForQualification();
                for (int frame = 1; frame < GlossyWarmupFrames - 2; frame++)
                    renderer.Render(scene, camera, ReflectionEye, Clear);
                renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
                var previous = renderer.ReadLightingForQualification();
                renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
                var filtered = renderer.ReadLightingForQualification();
                var hits = QualifyGlossyHitPairs(renderer.ReadGlossyReflectionsForQualification());
                if (mode == GlossyReflectionMode3D.FullResolution) fullSamples = hits.Samples;
                else Require(hits.Samples > fullSamples * .24 && hits.Samples < fullSamples * .26,
                    "Quarter-rate mode did not rotate one glossy sample through each 2x2 group.");
                double coldRmse = Rmse(cold.LinearRgb, reference.LinearRgb);
                double filteredRmse = Rmse(filtered.LinearRgb, reference.LinearRgb);
                double filteredDelta = Rmse(filtered.LinearRgb, previous.LinearRgb);
                Console.WriteLine($"R5b r={roughness:F2} {label}: cold {coldRmse:F6}, filtered {filteredRmse:F6}, raw1 {singleRmse:F6}, raw4 {fourRmse:F6}, sharp {sharpRmse:F6}.");
                Require(renderer.CurrentFramePlan!.Graph.Passes.Any(pass => pass.Name == "glossy-reflection-reconstruction"),
                    "Glossy reconstruction bypassed the compiled frame plan.");
                Require(coldRmse < singleRmse * .65 && filteredRmse < singleRmse * .55 && filteredDelta < rawDelta * .3,
                    "Glossy reconstruction did not reduce single-frame error and flicker.");
                if (roughness is .24f or .36f)
                    Require(filteredRmse < fourRmse * .75 && filteredRmse < sharpRmse * .6,
                        "Glossy reconstruction did not beat the four-sample or sharp control.");
                SaveSamplingImage(plant, assets, output, name + "-" + label, filtered);
                SaveSamplingImage(plant, assets, output, name + "-" + label + "-cold", cold);
                variants.Add(new { Mode = label, HitPairs = hits, ColdRmse = coldRmse,
                    FilteredRmse = filteredRmse, TemporalDelta = filteredDelta });
            }
            foreach (string label in new[] { "raw1", "raw4", "sharp", "full", "half" })
                pairs.Add(new { Name = name + "-" + label, Reference = name + "-reference.png", Test = name + "-" + label + ".png", Group = name + "-" + label });
            quality.Add(new { Roughness = roughness, Raw1Rmse = singleRmse, Raw4Rmse = fourRmse, SharpRmse = sharpRmse, RawTemporalDelta = rawDelta, Variants = variants });
            if (roughness == .24f)
            {
                cpuReference = CheckGlossyIntegral((NativeInstanceSet3D)scene.Instances, camera, reference, roughness);
                cameraMotion = CheckGlossyCameraMotion(plant, assets, output, scene, camera, source, environment, reference);
            }
        }
        pairs.Add(new { Name = "identity", Reference = "roughness-24-reference.png", Test = "roughness-24-reference.png", Group = "identity" });
        Native3DScene motionScene = SetReflectionRoughness(seed, .28f);
        var secondHitMotion = new List<object>();
        foreach (var mode in new[] { GlossyReflectionMode3D.FullResolution, GlossyReflectionMode3D.HalfResolution })
            secondHitMotion.Add(CheckGlossySecondaryMotion(plant, assets, output, motionScene, camera, source, environment, mode));
        object sharpCoverage = CheckSharpInQuarterRate(plant, assets, seed, camera, source, environment);
        object performance = MeasureGlossyReflections(plant, assets, motionScene, source, environment);
        validation.RequireClean();
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "glossy-reflections", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, ReferenceSamples = GlossyReferenceSamples,
            ReconstructionFrames = GlossyWarmupFrames, Quality = quality, IndependentIntegral = cpuReference,
            CameraMotion = cameraMotion, SecondaryMotion = secondHitMotion, SharpCoverage = sharpCoverage, Performance = performance,
        }, json));
        Console.WriteLine("AURELIAN_R5B_PASSED " + output);
    }

    private static Native3DScene SetReflectionRoughness(Native3DScene scene, float roughness) => scene with
    {
        Instances = new NativeInstanceSet3D(scene.Instances.Select(instance => instance.Identity == "mirror"
            ? instance with { Material = instance.Material with { Roughness = roughness } }
            : instance)),
    };

    private sealed record GlossyHitCounts(int Samples, int ObjectHits, int SkyMisses, int RejectedDirections);

    private static GlossyHitCounts QualifyGlossyHitPairs((float[] Radiance, uint[] HitPairs) capture)
    {
        int hitCount = 0, misses = 0, rejected = 0;
        for (int pixel = 0; pixel < capture.Radiance.Length / 4; pixel++)
        {
            if (capture.Radiance[pixel * 4 + 3] >= 0) continue;
            uint instance = capture.HitPairs[pixel * 2];
            uint distance = capture.HitPairs[pixel * 2 + 1];
            if (instance == 0)
            {
                Require(distance <= 1, "A glossy miss carried an object distance.");
                if (distance == 0) misses++;
                else rejected++;
            }
            else
            {
                Require(instance is 2 or 3 && distance is > 200 and < 1200, "Compact glossy hit identity or distance escaped the fixture.");
                hitCount++;
            }
        }
        Require(hitCount > 150 && misses > 500, "Glossy finish did not observe both hit and miss packets.");
        return new(hitCount + misses + rejected, hitCount, misses, rejected);
    }

    private static (ReferenceImage3D Mean, ReferenceImage3D First, ReferenceImage3D Four, double Delta) AccumulateGlossy(
        VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera)
    {
        var sum = new double[ReflectionWidth * ReflectionHeight * 3];
        ReferenceImage3D? first = null;
        ReferenceImage3D? four = null;
        double delta = 0;
        for (int frame = 0; frame < GlossyReferenceSamples; frame++)
        {
            renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
            var image = renderer.ReadLightingForQualification();
            first ??= image;
            if (frame == 1) delta = Rmse(first.LinearRgb, image.LinearRgb);
            for (int index = 0; index < sum.Length; index++) sum[index] += image.LinearRgb[index];
            if (frame == 3) four = new(ReflectionWidth, ReflectionHeight, 1, sum.Select(value => (float)(value / 4)).ToArray());
        }
        return (new(ReflectionWidth, ReflectionHeight, 1, sum.Select(value => (float)(value / GlossyReferenceSamples)).ToArray()), first!, four!, delta);
    }

    private static object CheckGlossyIntegral(NativeInstanceSet3D scene, Matrix4x4 camera, ReferenceImage3D reference, float roughness)
    {
        // Independent NDF sampling (not VNDF), CPU intersection and material
        // evaluation. Its PDF is D(h) N.H / (4 V.H).
        const int samples = 8192;
        Matrix4x4.Invert(camera, out var inverse);
        var errors = new List<float>();
        Vector3 n = Vector3.UnitY;
        float nv = ReflectionView.Y;
        float alpha = roughness * roughness;
        float alphaSquared = alpha * alpha;
        float Lambda(float cosine) => .5f * (MathF.Sqrt(1 + alphaSquared * (1 / (cosine * cosine) - 1)) - 1);
        Vector4 color = scene[0].Material.BaseColor;
        Vector3 f0 = new(color.X, color.Y, color.Z);
        foreach (int y in new[] { 49, 71, 94 })
        {
            foreach (int x in new[] { 57, 88, 121, 159, 189 })
            {
                var ndc = new Vector3(2 * (x + .5f) / ReflectionWidth - 1, 2 * (y + .5f) / ReflectionHeight - 1, 0);
                Vector3 start = Vector3.Transform(ndc, inverse);
                var primary = TraceReflectionOracle(scene, start, -ReflectionView);
                Require(primary.Identity == 0, "Glossy integral probe missed its receiver.");
                Vector3 origin = start - ReflectionView * primary.Distance + n * .0005f;
                Vector3 sum = Vector3.Zero;
                for (int sample = 0; sample < samples; sample++)
                {
                    float u = (sample + .5f) / samples;
                    float v = RadicalInverse(sample);
                    float cos = MathF.Sqrt((1 - u) / (1 + (alphaSquared - 1) * u));
                    float sin = MathF.Sqrt(1 - cos * cos);
                    float angle = 2 * MathF.PI * v;
                    Vector3 half = new(sin * MathF.Cos(angle), cos, sin * MathF.Sin(angle));
                    float vh = Vector3.Dot(ReflectionView, half);
                    if (vh <= 0) continue;
                    Vector3 outgoing = Vector3.Reflect(-ReflectionView, half);
                    if (outgoing.Y <= 0) continue;
                    var hit = TraceReflectionOracle(scene, origin, outgoing);
                    Vector3 radiance = ReflectionSky;
                    if (hit.Identity >= 0)
                    {
                        var instance = scene.PackedInstances[hit.Identity];
                        radiance = Vector3.Dot(hit.Normal, -outgoing) > 0
                            ? SecondaryOracle(instance, origin + outgoing * hit.Distance, hit.Normal, true).Emission : Vector3.Zero;
                    }
                    Vector3 fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1 - vh, 5);
                    float ratio = vh / (nv * cos * (1 + Lambda(nv) + Lambda(outgoing.Y)));
                    sum += radiance * fresnel * ratio;
                }
                int offset = (y * ReflectionWidth + x) * 3;
                Vector3 gpu = new(reference.LinearRgb[offset], reference.LinearRgb[offset + 1], reference.LinearRgb[offset + 2]);
                errors.Add(Vector3.Distance(sum / samples, gpu));
            }
        }
        Require(errors.Average() < .045f && errors.Max() < .18f, "Native visible-normal estimator disagreed with the independent NDF integral.");
        Console.WriteLine($"R5b independent NDF quadrature: mean RGB distance {errors.Average():F6}, max {errors.Max():F6}.");
        return new { Probes = errors.Count, SamplesPerProbe = samples, MeanRgbDistance = errors.Average(), MaxRgbDistance = errors.Max() };

        static float RadicalInverse(int sample)
        {
            float value = 0, weight = .5f;
            while (sample != 0)
            {
                value += (sample & 1) * weight;
                sample >>= 1;
                weight *= .5f;
            }
            return value;
        }
    }
}
