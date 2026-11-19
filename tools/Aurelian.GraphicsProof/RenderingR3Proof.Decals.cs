using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const string DecalReceiver = "secondary-source";

    public static void RunDecals(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R8 surface projectors"));
        Require(initialized.Success, "Decal Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);

        Vector3 center = ReflectionDirection * 4.5f;
        Vector3 normal = -ReflectionDirection;
        Vector3 up = Vector3.Cross(normal, Vector3.UnitX);
        var dry = new ModelMaterial("R8/dry-stone") { BaseColor = new(.72f, .61f, .50f, 1), Roughness = .72f };
        var mirror = new ModelMaterial("R8/mirror") { BaseColor = new(.88f, .92f, .97f, 1), Roughness = .045f, Metallic = 1 };
        var orientation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(1, 0, 0, 0, up.X, up.Y, up.Z, 0,
            normal.X, normal.Y, normal.Z, 0, 0, 0, 0, 1));
        var burn = new SurfaceDecal3D(DecalReceiver, center - Vector3.UnitX * .56f,
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -.4f), orientation),
            new(.55f, .39f, .22f), new(.17f, .063f, .032f), Roughness: .97f);
        var wet = new SurfaceDecal3D(DecalReceiver, center + Vector3.UnitX * .55f,
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -.16f), orientation),
            new(.52f, .41f, .22f), new(.54f, .78f, .91f), Roughness: .14f, NormalFlattening: .83f);
        var panel = ReflectionQuad("R8/decal-wall", Vector3.UnitX * 1.46f, up * .80f);
        var floor = ReflectionQuad("R8/decal-mirror", Vector3.UnitX * 1.72f, -Vector3.UnitZ * 1.85f);
        var scene = new Native3DScene([], [])
        {
            Instances = new NativeInstanceSet3D([
                new("mirror", floor, mirror, Matrix4x4.Identity),
                new(DecalReceiver, panel, dry, Matrix4x4.CreateTranslation(center)),
            ]),
            Decals = [burn, wet],
        };
        var baseline = scene with { Decals = [] };
        LocalLight3D[] lights = [new(center + normal * 2.7f + Vector3.UnitX * .40f, Vector3.One, 12, 6)];
        renderer.LocalLights = lights;
        renderer.Settings = Settings();

        Vector3 eye = center + normal * 3.6f;
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(3.2f, 1.92f, .1f, 10);
        projection.M22 *= -1;
        Matrix4x4 wallCamera = Matrix4x4.CreateLookAt(eye, center, up) * projection;
        var detail = DecalPrimary(plant, assets, output, renderer, scene, baseline, lights, wallCamera, eye, normal);
        var reflected = DecalReflection(plant, assets, output, renderer, scene, baseline, lights);
        var bounce = DecalBounce(plant, assets, output, renderer, scene, baseline, lights);
        var timing = MeasureDecalResolve(plant, assets, scene, baseline, lights, wallCamera, eye);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Primary = detail,
            Reflections = reflected,
            DiffuseGi = bounce,
            ResolveTiming = timing,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R8_DECALS_PASSED " + output);
    }

    private static object DecalPrimary(AurelianVulkanPlant plant, GameAssets assets, string output,
        VulkanSolid3DRenderer renderer, Native3DScene scene, Native3DScene baseline, LocalLight3D[] lights,
        Matrix4x4 camera, Vector3 eye, Vector3 view)
    {
        var source = (NativeInstanceSet3D)scene.Instances;
        renderer.Render(baseline, camera, eye, Clear, capture: true);
        var dry = renderer.ReadLightingForQualification();
        var projected = renderer.Render(scene, camera, eye, Clear, capture: true);
        var actual = renderer.ReadLightingForQualification();
        SaveSamplingImage(plant, assets, output, "primary-dry", dry);
        SaveSamplingImage(plant, assets, output, "primary-decals", actual);
        Matrix4x4.Invert(camera, out var inverse);
        int burnPixels = 0, wetPixels = 0, cleanPixels = 0;
        double expectedError = 0, undecaledError = 0;
        for (int pixel = 0; pixel < ReflectionWidth * ReflectionHeight; pixel++)
        {
            var hit = TraceReflectionOracle(source, DecalCameraStart(inverse, pixel), -view);
            if (hit.Identity < 0 || source.PackedInstances[hit.Identity].Identity != DecalReceiver) continue;
            Vector3 point = DecalCameraStart(inverse, pixel) - view * hit.Distance;
            var drySurface = SecondaryOracle(source.PackedInstances[hit.Identity], point, hit.Normal, true);
            var decorated = DecalOracle(scene.Decals, DecalReceiver, point, drySurface);
            Vector3 gpu = DecalImagePixel(actual, pixel);
            Vector3 control = DecalImagePixel(dry, pixel);
            if (decorated == drySurface)
            {
                cleanPixels++;
                Require(Vector3.Distance(gpu, control) <= .004f, "A projector changed the unmarked wall.");
                continue;
            }
            if (decorated.Roughness > drySurface.Roughness) burnPixels++;
            else wetPixels++;
            Vector3 expected = SecondaryPointResponse(decorated, point, view, hit.Normal, lights);
            expectedError += Vector3.DistanceSquared(expected, gpu);
            undecaledError += Vector3.DistanceSquared(expected, control);
        }
        int marked = burnPixels + wetPixels;
        double error = Math.Sqrt(expectedError / (3 * marked));
        double controlError = Math.Sqrt(undecaledError / (3 * marked));
        Console.WriteLine($"R8 primary projectors {burnPixels}/{wetPixels} marked pixels, HDR error {error:F6} vs dry {controlError:F6}.");
        Require(projected.Retained is { VisibilityBuffer: true } && burnPixels > 400 && wetPixels > 400 && cleanPixels > 3000
            && error < .008 && error < controlError * .1, "Primary visibility did not reproduce the independent projected materials.");

        renderer.EnableVisibilityBuffer = false;
        var raster = renderer.Render(scene, camera, eye, Clear, capture: true);
        double rasterError = Rmse(actual.LinearRgb, renderer.ReadLightingForQualification().LinearRgb);
        Require(raster.Retained is { BindlessMaterials: true, VisibilityBuffer: false } && rasterError < .002,
            "Bindless raster and visibility resolution disagree on the projected materials.");
        renderer.EnableVisibilityBuffer = true;
        return new { BurnPixels = burnPixels, WetPixels = wetPixels, CleanPixels = cleanPixels,
            Rmse = error, DryRmse = controlError, BindlessRasterRmse = rasterError };
    }

    private static object DecalReflection(AurelianVulkanPlant plant, GameAssets assets, string output,
        VulkanSolid3DRenderer renderer, Native3DScene scene, Native3DScene baseline, LocalLight3D[] lights)
    {
        var source = (NativeInstanceSet3D)scene.Instances;
        Matrix4x4 camera = ReflectionCamera(ReflectionWidth, ReflectionHeight);
        renderer.Settings = renderer.Settings with { SharpReflections = true };
        renderer.Render(baseline, camera, ReflectionEye, Clear, capture: true);
        var dry = renderer.ReadReflectionsForQualification();
        SaveSamplingImage(plant, assets, output, "mirror-dry", renderer.ReadLightingForQualification());
        var before = renderer.RaySceneStatistics!;
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var actual = renderer.ReadReflectionsForQualification();
        SaveSamplingImage(plant, assets, output, "mirror-decals", renderer.ReadLightingForQualification());
        Require(renderer.CurrentFramePlan!.Graph.Passes.Any(pass => pass.Name == "sharp-reflections"),
            "The decal rig must dispatch the hardware reflection pass.");
        Matrix4x4.Invert(camera, out var inverse);
        int burnRays = 0, wetRays = 0;
        double expectedError = 0, undecaledError = 0;
        for (int pixel = 0; pixel < ReflectionWidth * ReflectionHeight; pixel++)
        {
            Vector3 start = DecalCameraStart(inverse, pixel);
            var originHit = TraceReflectionOracle(source, start, -ReflectionView);
            if (originHit.Identity < 0) continue;
            Require(source.PackedInstances[originHit.Identity].Identity == "mirror",
                "Decal receiver entered the camera in the offscreen reflection rig.");
            Vector3 origin = start - ReflectionView * originHit.Distance + Vector3.UnitY * .0005f;
            var hit = TraceReflectionOracle(source, origin, ReflectionDirection);
            if (hit.Identity < 0 || source.PackedInstances[hit.Identity].Identity != DecalReceiver) continue;
            Vector3 point = origin + ReflectionDirection * hit.Distance;
            var material = SecondaryOracle(source.PackedInstances[hit.Identity], point, hit.Normal, true);
            var decorated = DecalOracle(scene.Decals, DecalReceiver, point, material);
            if (decorated == material) continue;
            if (decorated.Roughness > material.Roughness) burnRays++;
            else wetRays++;
            int packet = pixel * 16;
            Require(actual.Radiance[pixel * 4 + 3] > .99f && actual.Hits[packet + 3] == hit.Identity
                && MathF.Abs(actual.Hits[packet + 4] - hit.Distance) < .012f,
                "The hardware ray disagreed with the offscreen CPU surface hit.");
            Vector3 expected = SecondaryPointResponse(decorated, point, -ReflectionDirection, hit.Normal, lights);
            expectedError += Vector3.DistanceSquared(expected, ReadRgb(actual.Radiance, pixel));
            undecaledError += Vector3.DistanceSquared(expected, ReadRgb(dry.Radiance, pixel));
        }
        int marked = burnRays + wetRays;
        double error = Math.Sqrt(expectedError / (3 * marked));
        double controlError = Math.Sqrt(undecaledError / (3 * marked));
        Console.WriteLine($"R8 reflected projectors {burnRays}/{wetRays} hardware hits, HDR error {error:F6} vs dry {controlError:F6}.");
        Require(burnRays > 200 && wetRays > 200 && error < .008 && error < controlError * .1,
            "Hardware reflections lost the offscreen decal material.");
        // Check both frame slots, including the slot that held the decal table.
        var restoredSlots = new HashSet<int>();
        for (int frame = 0; frame < 2; frame++)
        {
            var removed = renderer.Render(baseline, camera, ReflectionEye, Clear, capture: true);
            restoredSlots.Add(removed.CpuTimings!.Slot);
            var restored = renderer.ReadReflectionsForQualification();
            Require(restored.Radiance.AsSpan().SequenceEqual(dry.Radiance), "Removing a decal left stale reflected radiance.");
        }
        var after = renderer.RaySceneStatistics!;
        Require(restoredSlots.Count == 2 && after.MeshBuilds == before.MeshBuilds && after.TopLevelBuilds == before.TopLevelBuilds
            && after.TopLevelUpdates == before.TopLevelUpdates, "Decal edits must reuse geometry across both frame slots.");
        return new { BurnRays = burnRays, WetRays = wetRays, Rmse = error, DryRmse = controlError,
            RestoredSlots = restoredSlots.Count, AccelerationBuilds = after.TopLevelBuilds, AccelerationUpdates = after.TopLevelUpdates };
    }

    private static object DecalBounce(AurelianVulkanPlant plant, GameAssets assets, string output,
        VulkanSolid3DRenderer renderer, Native3DScene scene, Native3DScene baseline, LocalLight3D[] lights)
    {
        var grid = new DiffuseProbeGrid(new(-1.7f, .2f, -3.35f), new(1.7f, 2.5f, 1.15f), 7, 5, 7, 128);
        renderer.Settings = renderer.Settings with { SharpReflections = false };
        renderer.DynamicDiffuse = new() { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid,
            RaysPerProbePerFrame = 128, CaptureSecondaryWitness = true };
        Matrix4x4 camera = ReflectionCamera(ReflectionWidth, ReflectionHeight);
        // A rough dielectric floor receives diffuse bounce and keeps the
        // offscreen projected wall as the only source of a changed contribution.
        var source = new NativeInstanceSet3D(scene.Instances.Select(instance => instance.Identity == "mirror"
            ? instance with { Material = instance.Material with { Metallic = 0, Roughness = .79f } }
            : instance));
        baseline = baseline with { Instances = source };
        scene = scene with { Instances = source };
        renderer.Render(baseline, camera, ReflectionEye, Clear, capture: true);
        var dry = renderer.ReadLightingForQualification();
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var actual = renderer.ReadLightingForQualification();
        float[] witness = renderer.ReadSecondaryWitnessForQualification();
        int burnRays = 0, wetRays = 0;
        var (oracle, attributes) = SecondaryOracleField(source, grid, witness, lights, (instance, point, material) =>
        {
            var result = DecalOracle(scene.Decals, instance.Identity, point, material);
            if (result.Roughness > material.Roughness) burnRays++;
            if (result.Roughness < material.Roughness) wetRays++;
            return result;
        });
        Require(burnRays > 35 && wetRays > 35, $"The sampled bounce basis only hit {burnRays} scorch and {wetRays} wetness points.");
        renderer.SetCompiledDiffuseProbes(oracle);
        renderer.DynamicDiffuse = renderer.DynamicDiffuse with { Mode = DynamicDiffuseMode3D.CompiledOnly };
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var expected = renderer.ReadLightingForQualification();
        double error = Rmse(actual.LinearRgb, expected.LinearRgb);
        double control = Rmse(dry.LinearRgb, expected.LinearRgb);
        Console.WriteLine($"R8 diffuse bounce {burnRays}/{wetRays} decorated hits, HDR error {error:F6} vs dry {control:F6}.");
        Require(error < .003 && error < control * .15 && control > .001,
            "The rendered bounce did not match independent CPU material response and probe convolution.");
        SaveSamplingImage(plant, assets, output, "bounce-dry", dry);
        SaveSamplingImage(plant, assets, output, "bounce-decals", actual);
        SaveSamplingImage(plant, assets, output, "bounce-cpu-reference", expected);
        return new { BurnRays = burnRays, WetRays = wetRays, Rmse = error, DryRmse = control, Attributes = attributes };
    }

    private static CpuSecondary DecalOracle(ImmutableArray<SurfaceDecal3D> decals,
        string receiver, Vector3 point, CpuSecondary material)
    {
        Vector3 originalNormal = material.Normal;
        foreach (var decal in decals)
        {
            if (decal.Receiver != receiver) continue;
            Vector3 axis = Vector3.Transform(Vector3.UnitZ, decal.Orientation);
            Vector3 p = Vector3.Transform(point - decal.Center, Quaternion.Conjugate(decal.Orientation)) / decal.HalfExtent;
            float outer = p.X * p.X + p.Y * p.Y;
            float edge = Math.Clamp((1 - outer) / .2f, 0, 1);
            float support = Math.Clamp((1 - Math.Abs(p.Z)) / .2f, 0, 1);
            float incidence = Math.Clamp((Vector3.Dot(originalNormal, axis) - .35f) / .25f, 0, 1);
            float alpha = decal.Opacity * support * incidence * edge * edge * (3 - 2 * edge);
            if (alpha <= 0) continue;
            material = material with
            {
                Color = material.Color * Vector3.Lerp(Vector3.One, decal.ColorMultiplier, alpha),
                Roughness = float.Lerp(material.Roughness, decal.Roughness ?? material.Roughness, alpha),
                Normal = decal.NormalFlattening > 0
                    ? Vector3.Normalize(Vector3.Lerp(material.Normal, axis, alpha * decal.NormalFlattening))
                    : material.Normal,
            };
        }
        return material;
    }

    private static object MeasureDecalResolve(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene,
        Native3DScene baseline, LocalLight3D[] lights, Matrix4x4 camera, Vector3 eye)
    {
        const int width = 1280;
        const int height = 768;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.EnableVisibilityBuffer = true;
        renderer.Settings = Settings();
        renderer.LocalLights = lights;
        var before = CollectReflectionTiming(_ => renderer.Render(baseline, camera, eye, Clear));
        renderer.WaitForPendingFrames();
        var projected = CollectReflectionTiming(_ => renderer.Render(scene, camera, eye, Clear));
        renderer.WaitForPendingFrames();
        var after = CollectReflectionTiming(_ => renderer.Render(baseline, camera, eye, Clear));
        renderer.WaitForPendingFrames();
        Console.WriteLine($"R8 decal resolve {width}x{height}, dry {before}, two decals {projected}, dry repeat {after}.");
        return new { Width = width, Height = height, Dry = before, TwoDecals = projected, DryRepeat = after };
    }

    private static Vector3 DecalCameraStart(Matrix4x4 inverse, int pixel) =>
        Vector3.Transform(new(2 * (pixel % ReflectionWidth + .5f) / ReflectionWidth - 1,
            2 * (pixel / ReflectionWidth + .5f) / ReflectionHeight - 1, 0), inverse);

    private static Vector3 DecalImagePixel(ReferenceImage3D image, int pixel)
        => new(image.LinearRgb[pixel * 3], image.LinearRgb[pixel * 3 + 1], image.LinearRgb[pixel * 3 + 2]);
}
