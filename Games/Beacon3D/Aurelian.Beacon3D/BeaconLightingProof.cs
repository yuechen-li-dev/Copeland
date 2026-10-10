using System.Numerics;
using System.Text.Json;
using Aurelian.Assets.Lighting;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Beacon3D;

/// <summary>Finite native acceptance through the same asset, controller and renderer used by play.</summary>
internal static class BeaconLightingProof
{
    public static void Run(NativeGameGraphics graphics, BeaconGame game, string path, string output)
    {
        string key = BeaconLighting.Recipe().ContentKey;
        var asset = StaticDiffuseLightingAsset.Load(path, key);
        Require(graphics.LightingStatus?.Phase == "Ready", "The normal game loader did not publish lighting.");
        var renderer = graphics.Renderer;
        Native3DVertex[] geometry = BeaconScene.Build(game);
        Matrix4x4 camera = game.Camera((float)graphics.Target.Width / graphics.Target.Height);
        var settings = renderer.Settings;
        Native3DFrameResult Capture(string name)
        {
            var result = renderer.Render(geometry, camera, graphics.Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), (int)graphics.Target.Width,
                (int)graphics.Target.Height, result.Pixels!);
            return result;
        }

        var lit = Capture("compiled");
        Require(renderer.StaticDiffuseFallbackReason is null, "Validated lighting was not used.");
        Require(lit.PixelSha256 == Capture("repeat").PixelSha256, "Lighting is not repeatable.");
        double[] Measure()
        {
            for (int frame = 0; frame < 8; frame++) renderer.Render(geometry, camera, graphics.Clear);
            var times = new List<double>();
            for (int frame = 0; frame < 32; frame++)
            {
                var result = renderer.Render(geometry, camera, graphics.Clear);
                times.Add(result.GpuPassTimes.Single(item => item.Pass == "linear-lighting").Milliseconds);
            }
            times.Sort();
            return [times[times.Count / 2], times[(int)(times.Count * .95)]];
        }
        double[] compiledTimes = Measure();
        renderer.StaticEmissionIntensity = 0;
        var sunOnly = Capture("sun-only");
        Require(lit.PixelSha256 != sunOnly.PixelSha256, "Emitter coefficient had no effect.");
        renderer.StaticEmissionIntensity = 1;
        renderer.Settings = settings with { SunIntensity = settings.SunIntensity * .5f };
        Require(lit.PixelSha256 != Capture("half-sun").PixelSha256, "Sun coefficient had no effect.");
        renderer.Settings = settings;

        graphics.InvalidateLighting("AcceptanceStaticGeometryChanged");
        Require(graphics.LightingStatus?.Published is null, "Invalidation retained a published ticket.");
        var baseline = Capture("fallback");
        Require(lit.PixelSha256 != baseline.PixelSha256, "Compiled lighting did not change the game.");
        double[] baselineTimes = Measure();
        Require(!graphics.LoadLighting(path, new string('0', 64)), "Stale scene was accepted.");
        string? staleDiagnostic = graphics.LightingDiagnostic;
        Require(baseline.PixelSha256 == Capture("stale-fallback").PixelSha256, "Rejected asset did not fall back.");
        string corrupt = Path.Combine(output, "corrupt.alight");
        File.WriteAllText(corrupt, "{\"schema\":\"aurelian.static-diffuse.asset/1\",\"content\":\"{}\",\"sha256\":\"wrong\"}");
        Require(!graphics.LoadLighting(corrupt, key), "Bad checksum was accepted.");
        string? checksumDiagnostic = graphics.LightingDiagnostic;
        Require(!graphics.LoadLighting(Path.Combine(output, "missing.alight"), key), "Missing asset was accepted.");
        Require(graphics.LoadLighting(path, key), "Valid replacement did not publish.");
        Require(lit.PixelSha256 == Capture("reloaded").PixelSha256, "Reload changed identical lighting.");
        renderer.Settings = settings with { SunDirection = Vector3.UnitY };
        Capture("changed-sun-fallback");
        Require(renderer.StaticDiffuseFallbackReason == "UnsupportedMaterialOrChangedSunDirection", "Changed sun direction was used with stale transfer.");
        renderer.Settings = settings;
        graphics.Settings = settings with { SunDirection = Vector3.UnitY };
        Require(graphics.LightingStatus?.Published is null && graphics.LightingDiagnostic == "StaticSunDirectionChanged",
            "Host sun-direction edit did not withdraw publication.");
        graphics.Settings = settings;
        Require(graphics.LoadLighting(path, key), "Reload after host sun invalidation failed.");
        Require(lit.PixelSha256 == Capture("host-reloaded").PixelSha256, "Host invalidation changed valid replacement output.");

        object decoder = Probe(graphics, asset);
        var evidence = new
        {
            Outcome = "Success", SceneKey = key, Gpu = graphics.Plant.Facts.PhysicalDeviceName,
            DecoderVersion = StaticDiffuseLighting.DecoderVersion,
            NumericPayloadBytes = (asset.Mesh.Vertices.Length + asset.Mesh.Triangles.Length + asset.Mesh.Weights.Length) * 4,
            UploadedTextureBytes = asset.TextureData().Length * 4,
            AssetFileBytes = new FileInfo(path).Length,
            asset.Quality,
            asset.Mesh.TriangleCount, asset.Mesh.CoefficientCount,
            MaximumCpuEdgeMismatch = asset.Mesh.MaximumEdgeMismatch(),
            CompiledHdrMilliseconds = new { Median = compiledTimes[0], P95 = compiledTimes[1] },
            BaselineHdrMilliseconds = new { Median = baselineTimes[0], P95 = baselineTimes[1] },
            StaleDiagnostic = staleDiagnostic, ChecksumDiagnostic = checksumDiagnostic,
            LoadingStatus = graphics.LightingStatus, Trace = graphics.LightingInspector.Observe().Trace,
            StableRuntimeShader = "One compiled Solid3D program for all loads and coefficient changes",
            Decoder = decoder,
            Limits = "Static horizontal opaque Lambertian receiver; two light bases; no dynamic indirect transport, specular GI or reflection support"
        };
        File.WriteAllText(Path.Combine(output, "lighting-proof.json"), JsonSerializer.Serialize(evidence,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BEACON_COMPILED_LIGHTING_PROOF_PASSED " + Path.Combine(output, "lighting-proof.json"));
    }

    private static object Probe(NativeGameGraphics graphics, StaticDiffuseLighting asset)
    {
        const int resolution = 64;
        string root = Path.Combine(AppContext.BaseDirectory, "Assets");
        string? ReadSource(string name)
        {
            string file = Path.Combine(root, name);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        var module = GpuGraphicsBinder.Compile(new(GpuSourceLoader.Load("CompiledDiffuseProbe.v.ts",
            ReadSource)));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, targetEnvironment: "vulkan1.2");
        CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, backend);
        using var target = new VulkanNativeFrameTarget(graphics.Plant, resolution, resolution, VulkanTextureFormat.Rgba8Unorm);
        using var renderer = new VulkanSolid3DRenderer(graphics.Plant, program, target, enableDepth: false);
        renderer.UploadStaticDiffuseLighting(asset);
        renderer.PublishStaticDiffuseLighting(true);
        Vector2[] coefficients = [new(1, 0), new(0, 1), new(1, 1), new(.5f, 2)];
        var results = new List<object>();
        foreach (Vector2 coefficient in coefficients)
        {
            var expected = new Vector3[resolution * resolution];
            float maximum = .001f;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var p = new Vector2((x + .5f) * 2 / resolution - 1, (y + .5f) * 2 / resolution - 1);
                    var value = asset.Mesh.EvaluateNormalized(p, coefficient.X, coefficient.Y);
                    expected[y * resolution + x] = value;
                    maximum = Math.Max(maximum, Math.Max(value.X, Math.Max(value.Y, value.Z)));
                }
            }
            float scale = .9f / maximum;
            var color = new Vector4(scale, 0, 0, 1);
            Native3DVertex V(float x, float z) => new(new(x, 0, z), Vector3.UnitY, color);
            Native3DVertex[] quad = [V(-1, -1), V(1, -1), V(1, 1), V(-1, -1), V(1, 1), V(-1, 1)];
            renderer.Settings = Graphics3DSettings.Basic with
            {
                SolidPbr = true, SunDirection = asset.SunDirection, SunColor = Vector3.One, SunIntensity = coefficient.X
            };
            renderer.StaticEmissionIntensity = coefficient.Y;
            var frame = renderer.Render(quad, Matrix4x4.Identity, new(0, 0, 0, 1), capture: true);
            byte[] pixels = frame.Pixels!;
            double maxEncodedError = 0;
            for (int pixel = 0; pixel < expected.Length; pixel++)
            {
                Require(pixels[pixel * 4 + 3] == 255, "GPU decoder left a receiver hole.");
                float[] channels = [expected[pixel].X, expected[pixel].Y, expected[pixel].Z];
                for (int channel = 0; channel < 3; channel++)
                    maxEncodedError = Math.Max(maxEncodedError, Math.Abs(pixels[pixel * 4 + channel] / 255.0 - channels[channel] * scale));
            }
            Require(maxEncodedError < .6 / 255, "GPU/CPU decoder mismatch exceeds the RGBA8 quantization allowance: " + maxEncodedError);
            Require(frame.PixelSha256 == renderer.Render(quad, Matrix4x4.Identity, new(0, 0, 0, 1), capture: true).PixelSha256,
                "GPU decoder probe is not repeatable.");
            results.Add(new { Sun = coefficient.X, Emission = coefficient.Y, Samples = expected.Length,
                MaximumEncodedError = maxEncodedError, MaximumLinearErrorBound = maxEncodedError / scale,
                QuantizationAllowance = .6 / 255, Scale = scale });
        }
        return results;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
