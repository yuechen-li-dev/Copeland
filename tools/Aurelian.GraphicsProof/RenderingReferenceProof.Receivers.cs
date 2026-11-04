using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Lighting;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

internal static partial class RenderingReferenceProof
{
    private static object QualifyFurnace(AurelianVulkanPlant plant, GameAssets assets)
    {
        var expected = DiffuseReference.Furnace();
        float[] input = new float[expected.Count * 4];
        for (int index = 0; index < expected.Count; index++)
        {
            input[index * 4] = (float)Enum.Parse<DirectDiffuseModel3D>(expected[index].Model);
            input[index * 4 + 1] = (float)expected[index].Roughness;
            input[index * 4 + 2] = (float)expected[index].ViewCosine;
        }
        using var probe = new VulkanScalarComputeProbe(plant, assets.ComputeShader("ReferenceFurnace3D.v.ts"), "ReferenceFurnaceMain", input);
        float[] actual = probe.Execute(2);
        var rows = new List<object>();
        for (int index = 0; index < expected.Count; index++)
        {
            double error = Math.Abs(actual[index * 4] - expected[index].Reflectance);
            Require(float.IsFinite(actual[index * 4]) && error < .005 && actual[index * 4 + 3] == 1,
                $"GPU furnace differs from independent quadrature: {expected[index]}; GPU={actual[index * 4]}; error={error}.");
            rows.Add(new { expected[index].Model, expected[index].Roughness, expected[index].ViewCosine,
                CpuReflectance = expected[index].Reflectance, GpuReflectance = actual[index * 4], AbsoluteError = error,
                EnergyGain = Math.Max(0, actual[index * 4] - 1) });
        }
        return rows;
    }

    private static object QualifyCompiledReceiver(AurelianVulkanPlant plant, byte[] program, string output, string baseline)
    {
        string recipePath = Path.Combine(baseline, "recipe.json");
        string assetPath = Path.Combine(baseline, "lighting.alight");
        Require(File.Exists(recipePath) && File.Exists(assetPath),
            "Build Beacon's existing compiled diffuse lighting fixture before reference qualification; see compiled-diffuse-lighting.md.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(recipePath));
        var recipe = document.RootElement.GetProperty("Recipe").Deserialize<StaticDiffuseLightingRecipe>()!;
        string declaredKey = document.RootElement.GetProperty("SceneKey").GetString()!;
        Require(recipe.ContentKey == declaredKey, "Compiled receiver recipe identity differs.");
        StaticDiffuseLighting asset = StaticDiffuseLightingAsset.Load(assetPath, declaredKey);
        HorizontalDiffuseReceiver domain = recipe.Receiver;
        Require(domain.Width == domain.Depth, "This qualification uses a square receiver grid.");
        ReferenceTriangle3D[] geometry = ReferenceDiffuseScene3D.Describe(recipe);
        var camera = new ReferenceCamera3D(new(domain.MinimumX + domain.Width / 2, domain.Height + .005f,
            domain.MinimumZ + domain.Depth / 2), new(domain.MinimumX + domain.Width / 2, domain.Height - 1,
            domain.MinimumZ + domain.Depth / 2), -Vector3.UnitZ, 1, domain.Depth);
        var settings = new ReferenceCapture3D(64, 64, 8, 243, PixelJitter: false);
        using var integrator = new ReferenceIntegrator3D(plant, program, settings, camera, geometry, []);
        AccumulateTo(integrator, 4096);
        ReferenceImage3D actual = integrator.Capture();
        WriteImage(output, "beacon-emission-reference", actual);
        var decoded = CaptureCompiledReceiver(plant, asset);
        WriteImage(output, "beacon-compiled-response", new(64, 64, 1, decoded.LinearRgb));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(baseline, "reference-manifest.json")));
        Require(manifest.RootElement.GetProperty("SceneKey").GetString() == declaredKey, "Cycles reference scene identity differs.");
        float[] ReadRetained(string name)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(baseline, name + ".bin"));
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Require(hash == manifest.RootElement.GetProperty(name).GetString(), "Retained Cycles reference checksum mismatch.");
            Require(bytes.Length == 64 * 64 * 6 * 4, "Retained Cycles reference dimensions differ.");
            return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        }
        float[] cycles = ReadRetained("testing");
        float[] repeat = ReadRetained("repeat");
        var receiver = recipe.Contributors.Single(item => item.Id == recipe.ReceiverId);
        var masked = new List<(Vector3 Minimum, Vector3 Maximum)>();
        foreach (var contributor in recipe.Contributors.Where(item => item.Id != recipe.ReceiverId))
        {
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            for (int offset = 0; offset < contributor.Positions.Length; offset += 3)
            {
                Vector3 point = Read3(contributor.Positions, offset);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
            if (minimum.Y <= domain.Height + .01f && maximum.Y > domain.Height + .01f)
                masked.Add((minimum, maximum));
        }
        double error = 0;
        double energy = 0;
        double cpuError = 0;
        double cyclesError = 0;
        double cyclesNoise = 0;
        double fitError = 0;
        double rawError = 0;
        double rawCyclesError = 0;
        int sites = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                float u = (x + .5f) / actual.Width;
                float v = (y + .5f) / actual.Height;
                Vector3 point = new(domain.MinimumX + u * domain.Width, domain.Height, domain.MinimumZ + v * domain.Depth);
                if (masked.Any(bounds => point.X >= bounds.Minimum.X && point.X <= bounds.Maximum.X
                    && point.Z >= bounds.Minimum.Z && point.Z <= bounds.Maximum.Z)) continue;
                Vector3 albedo = ReceiverAlbedo(receiver, point);
                int offset = (y * actual.Width + x) * 3;
                Vector3 reference = new(actual.LinearRgb[offset] / albedo.X, actual.LinearRgb[offset + 1] / albedo.Y,
                    actual.LinearRgb[offset + 2] / albedo.Z);
                Vector3 compiled = new(decoded.LinearRgb[offset], decoded.LinearRgb[offset + 1], decoded.LinearRgb[offset + 2]);
                Vector3 cpu = asset.Mesh.EvaluateNormalized(new(u * 2 - 1, v * 2 - 1), 0, 1);
                int retainedOffset = (y * actual.Width + x) * 6 + 3;
                Vector3 retained = Read3(cycles, retainedOffset) / albedo;
                Vector3 retainedRepeat = Read3(repeat, retainedOffset) / albedo;
                cpuError += Vector3.DistanceSquared(reference, cpu);
                cyclesError += Vector3.DistanceSquared(reference, retained);
                cyclesNoise += Vector3.DistanceSquared(retained, retainedRepeat);
                fitError += Vector3.DistanceSquared(cpu, retained);
                error += Vector3.DistanceSquared(reference, compiled);
                rawError += Vector3.DistanceSquared(reference * albedo, compiled * albedo);
                rawCyclesError += Vector3.DistanceSquared(reference * albedo, retained * albedo);
                energy += reference.LengthSquared();
                sites++;
            }
        }
        double rmse = Math.Sqrt(error / (sites * 3));
        double radianceRmse = Math.Sqrt(rawError / (sites * 3));
        Require(sites > 3500 && energy > .01 && rmse < .02, $"Compiled receiver comparison failed: {sites} sites, RMSE {rmse}.");
        File.Copy(recipePath, Path.Combine(output, "beacon-recipe.json"), true);
        File.Copy(assetPath, Path.Combine(output, "beacon-lighting.alight"), true);
        Console.WriteLine($"Beacon compiled emission receiver: {sites} sites; radiance RMSE {radianceRmse:F7}; per-unit-albedo RMSE {rmse:F7}");
        return new { SceneKey = declaredKey, Samples = actual.Samples, Sites = sites, LinearHdrRmse = radianceRmse,
            PerUnitAlbedoRmse = rmse, ReferenceVsRetainedCyclesRadianceRmse = Math.Sqrt(rawCyclesError / (sites * 3)),
            AssetCoefficientsVsReferenceRmse = Math.Sqrt(cpuError / (sites * 3)),
            ReferenceVsRetainedCyclesRmse = Math.Sqrt(cyclesError / (sites * 3)),
            AssetCoefficientsVsRetainedCyclesRmse = Math.Sqrt(fitError / (sites * 3)),
            RetainedCyclesRepeatRmse = Math.Sqrt(cyclesNoise / (sites * 3)),
            decoded.QuantizationBound, decoded.MaximumDecodeError,
            Basis = "emission-total; per-unit-receiver-albedo", Decode = "Actual CompiledDiffuse GPU module and uploaded asset; scaled RGBA8 readback",
            Mask = "Same conservative contributor AABB ground-contact exclusion as the asset compiler", SunBasisQualified = false };
    }

    private static (float[] LinearRgb, double QuantizationBound, double MaximumDecodeError) CaptureCompiledReceiver(AurelianVulkanPlant plant, StaticDiffuseLighting asset)
    {
        string? ReadSource(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        var module = GpuGraphicsBinder.Compile(new(GpuSourceLoader.Load("CompiledDiffuseProbe.v.ts", ReadSource)));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        var program = CompiledGraphicsProgramExporter.Export(module, backend);
        using var target = new VulkanNativeFrameTarget(plant, 64, 64, VulkanTextureFormat.Rgba8Unorm);
        using var renderer = new VulkanSolid3DRenderer(plant, program, target, enableDepth: false);
        renderer.UploadStaticDiffuseLighting(asset);
        renderer.PublishStaticDiffuseLighting(true);
        float maximum = .001f;
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Vector3 value = asset.Mesh.EvaluateNormalized(new((x + .5f) / 32 - 1, (y + .5f) / 32 - 1), 0, 1);
                maximum = Math.Max(maximum, Math.Max(value.X, Math.Max(value.Y, value.Z)));
            }
        }
        float scale = .9f / maximum;
        Native3DVertex Vertex(float x, float z) => new(new(x, 0, z), Vector3.UnitY, new(scale, 0, 0, 1));
        Native3DVertex[] quad = [Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)];
        renderer.Settings = Graphics3DSettings.Basic with { SolidPbr = true, SunDirection = asset.SunDirection,
            SunIntensity = 0, DirectDiffuseModel = DirectDiffuseModel3D.Lambert };
        renderer.StaticEmissionIntensity = 1;
        byte[] pixels = renderer.Render(quad, Matrix4x4.Identity, new(0, 0, 0, 1), capture: true).Pixels!;
        float[] rgb = new float[64 * 64 * 3];
        double maximumError = 0;
        for (int pixel = 0; pixel < 64 * 64; pixel++)
        {
            Require(pixels[pixel * 4 + 3] == 255, "Compiled decoder left a receiver hole.");
            int x = pixel % 64;
            int y = pixel / 64;
            Vector3 expected = asset.Mesh.EvaluateNormalized(new((x + .5f) / 32 - 1, (y + .5f) / 32 - 1), 0, 1);
            for (int channel = 0; channel < 3; channel++)
            {
                float value = pixels[pixel * 4 + channel] / 255f;
                rgb[pixel * 3 + channel] = value / scale;
                maximumError = Math.Max(maximumError, Math.Abs(value - expected[channel] * scale));
            }
        }
        Require(maximumError < .6 / 255, "Compiled GPU decoder differs from asset coefficients.");
        return (rgb, .6 / 255 / scale, maximumError / scale);
    }

    private static Vector3 ReceiverAlbedo(StaticDiffuseContributor receiver, Vector3 point)
    {
        for (int offset = 0; offset < receiver.Positions.Length; offset += 9)
        {
            Vector3 a = Read3(receiver.Positions, offset);
            Vector3 b = Read3(receiver.Positions, offset + 3);
            Vector3 c = Read3(receiver.Positions, offset + 6);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.Y <= 0 || Math.Abs(a.Y - point.Y) > .001f) continue;
            Vector3 first = Vector3.Cross(b - a, point - a);
            Vector3 second = Vector3.Cross(c - b, point - b);
            Vector3 third = Vector3.Cross(a - c, point - c);
            if (first.Y >= -1e-6f && second.Y >= -1e-6f && third.Y >= -1e-6f)
                return receiver.FaceAlbedos.Length == 0 ? Read3(receiver.Albedo, 0) : Read3(receiver.FaceAlbedos, offset / 3);
        }
        throw new InvalidOperationException("Receiver has no owning face at " + point);
    }

    private static Vector3 Read3(float[] values, int offset) => new(values[offset], values[offset + 1], values[offset + 2]);
}
