using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using SkiaSharp;

internal static class DiffuseGraphicsProof
{
    private const int Width = 960;
    private const int Height = 640;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var furnace = DiffuseReference.Furnace();
        Require(furnace.Where(sample => sample.Model == "Eon").All(sample => Math.Abs(sample.Reflectance - 1) < .004),
            "EON failed the unit-albedo white-furnace reference.");
        WriteFurnace(output, furnace);
        var pathQualification = DiffuseReference.QualifyPath();
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian diffuse comparison"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        var diffuseLobe = ProveDiffuseLobe(plant, assets, output);
        var oracle = ProveGpu(plant, assets);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = CreateRenderer(plant, assets, target, surface: true);
        var scene = DiffuseLabGame.CreateScene();
        var comparisons = new List<object>();
        var timingSamples = new Dictionary<DirectDiffuseModel3D, List<double>>();
        foreach (var model in Enum.GetValues<DirectDiffuseModel3D>())
        {
            timingSamples[model] = [];
        }
        for (int lighting = 0; lighting < 3; lighting++)
        {
            renderer.LocalLights = lighting == 2 ? DiffuseLabGame.Lights : [];
            for (int shot = 0; shot < 2; shot++)
            {
                (Vector3 eye, Vector3 targetPoint) = DiffuseLabGame.Camera(shot);
                var camera = DiffuseLabGame.CameraMatrix(eye, targetPoint, (float)Width / Height);
                var captures = new Dictionary<DirectDiffuseModel3D, byte[]>();
                foreach (var model in Enum.GetValues<DirectDiffuseModel3D>())
                {
                    renderer.Settings = DiffuseLabGame.Settings(lighting) with { DirectDiffuseModel = model };
                    var frame = renderer.Render(scene, camera, eye, new(.018f, .022f, .03f, 1), capture: true);
                    byte[] pixels = frame.Pixels!;
                    captures[model] = pixels;
                    NativeGameGraphics.WritePng(Path.Combine(output, $"lighting-{lighting}-shot-{shot}-{model}.png"), Width, Height, pixels);
                    if (model != DirectDiffuseModel3D.Lambert)
                    {
                        var difference = Difference(captures[DirectDiffuseModel3D.Lambert], pixels);
                        Require(difference.ChangedPixels > 100, $"{model} did not affect the production lighting path.");
                        comparisons.Add(new
                        {
                            Lighting = lighting,
                            Shot = shot,
                            Model = model.ToString(),
                            difference.ChangedPixels,
                            difference.MeanLinearDifference,
                            difference.MaximumLinearDifference
                        });
                    }
                }
                WriteSheet(Path.Combine(output, $"lighting-{lighting}-shot-{shot}-comparison.png"), captures);
                WriteSheet(Path.Combine(output, $"lighting-{lighting}-shot-{shot}-path-vs-frostbite.png"),
                    new Dictionary<DirectDiffuseModel3D, byte[]>
                    {
                        [DirectDiffuseModel3D.Frostbite] = captures[DirectDiffuseModel3D.Frostbite],
                        [DirectDiffuseModel3D.PathDiffuse] = captures[DirectDiffuseModel3D.PathDiffuse],
                    });
            }
        }
        renderer.LocalLights = [];
        (Vector3 timingEye, Vector3 timingTarget) = DiffuseLabGame.Camera(0);
        Matrix4x4 timingCamera = DiffuseLabGame.CameraMatrix(timingEye, timingTarget, (float)Width / Height);
        var models = Enum.GetValues<DirectDiffuseModel3D>();
        // Same compiled shader, balanced rotating order and 50 samples after 10 warmup cycles.
        // These are local GPU pass observations, not a benchmark claim.
        for (int cycle = 0; cycle < 60; cycle++)
        {
            for (int index = 0; index < models.Length; index++)
            {
                var model = models[(cycle + index) % models.Length];
                renderer.Settings = DiffuseLabGame.Settings(0) with { DirectDiffuseModel = model };
                var frame = renderer.Render(scene, timingCamera, timingEye, new(.018f, .022f, .03f, 1));
                if (cycle >= 10)
                {
                    double milliseconds = frame.GpuPassTimes.Where(pass => pass.Pass == "surface-lighting").Sum(pass => pass.Milliseconds);
                    timingSamples[model].Add(milliseconds);
                }
            }
        }
        var timings = timingSamples.Select(pair => new
        {
            Model = pair.Key.ToString(),
            Samples = pair.Value,
            MedianMilliseconds = pair.Value.Order().ElementAt(pair.Value.Count / 2),
        }).ToArray();
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            Oracle = oracle,
            IsolatedDiffuseLobe = diffuseLobe,
            Furnace = furnace,
            PathQualification = pathQualification,
            Comparisons = comparisons,
            Timings = timings,
            Scope = "Direct-light lobe ablation in production forward and surface shaders; fixed GGX, materials, lighting, camera and output. Ambient/IBL unchanged. EON uses its white lobe multiplied by authored base color; PathDiffuse uses single-bounce color semantics.",
            Limits = "No measured-material fit, perceptual user study, complete rough-diffuse IBL, new GI bake, or cross-engine attribution. Timings are local, dynamically branched and clock-uncontrolled.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AURELIAN_DIFFUSE_PROOF_PASSED output={output}");
    }

    private static object ProveDiffuseLobe(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        // Instrument the ordinary forward shader by bypassing its specular/Fresnel mixture.
        // The diffuse function itself is the production Lighting3D module, never a test copy.
        string source = BuiltinShaderLibrary.Read("Solid3D.v.ts");
        const string original = "DirectLightModel(base, normal, view, light, resources.camera.surface.y, resources.camera.surface.x, resources.camera.ground.w)";
        Require(source.Contains(original, StringComparison.Ordinal), "The production diffuse probe seam changed.");
        source = source.Replace("import { Unit,", "import { DiffuseResponse, Unit,", StringComparison.Ordinal)
            .Replace(original, "Scale3(DiffuseResponse(base, normal, view, light, resources.camera.surface.x, resources.camera.ground.w), Max(Dot3(normal, light), 0.0) / 3.14159265)", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(output, "instrumented-forward-probe.v.ts"), source);
        var sources = BuiltinShaderLibrary.LoadSources("Solid3D.v.ts").Select(file =>
            file.Path == "Solid3D.v.ts" ? new GpuSourceFile(file.Path, source) : file).ToArray();
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest(sources));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module);
        Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, "The instrumented diffuse shader failed SPIR-V validation.");
        var program = CompiledGraphicsProgramExporter.Export(module, backend);
        using var target = new VulkanNativeFrameTarget(plant, 65, 65);
        using var renderer = new VulkanSolid3DRenderer(plant, program, target,
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
        float maximumError = 0;
        int cases = 0;
        foreach (var model in Enum.GetValues<DirectDiffuseModel3D>())
        {
            foreach (Vector3 color in new[] { new Vector3(.25f), new Vector3(.42f, .075f, .025f), new Vector3(.025f, .12f, .36f) })
            {
                Native3DVertex[] geometry = ProbeGeometry(color);
                foreach (float roughness in new[] { .045f, .25f, .65f, 1 })
                {
                    foreach (float viewDegrees in new[] { 0f, 45, 75, 85 })
                    {
                        Vector3 view = Direction(viewDegrees);
                        Vector3 eye = view * 6;
                        Matrix4x4 camera = Camera3D.Matrix(eye, -view, 1);
                        foreach (float lightDegrees in new[] { -60f, 0, 65, 85 })
                        {
                            Vector3 light = Direction(lightDegrees);
                            renderer.Settings = Graphics3DSettings.Default with
                            {
                                DirectDiffuseModel = model,
                                SolidRoughness = roughness,
                                AntiAliasing = AntiAliasing3D.None,
                                ToneMapping = false,
                                BloomIntensity = 0,
                                Shadows = false,
                                SunColor = Vector3.One,
                                SunDirection = light,
                                SunIntensity = 3,
                                SkyAmbient = Vector3.Zero,
                                GroundAmbient = Vector3.Zero,
                            };
                            var frame = renderer.Render(geometry, camera, new(0, 0, 0, 1), capture: true, eye: eye);
                            Vector3 actual = CenterColor(frame.Pixels!);
                            float Expected(float albedo) => (float)(DiffuseReference.Response(model, albedo,
                                light.Z, view.Z, Vector3.Dot(light, view), roughness) * light.Z * 3 / Math.PI);
                            Vector3 expected = new(Expected(color.X), Expected(color.Y), Expected(color.Z));
                            float error = Vector3.Distance(actual, expected);
                            maximumError = Math.Max(maximumError, error);
                            Require(error < .005f, $"Isolated diffuse lobe mismatch {model} color={color} r={roughness} v={viewDegrees} l={lightDegrees}: {actual} vs {expected}.");
                            cases++;
                        }
                    }
                }
            }
        }
        var richness = ProveRichness(renderer, output);
        return new
        {
            Cases = cases,
            MaximumLinearError = maximumError,
            program.VdMirSha256,
            Richness = richness,
            Reference = "Production DiffuseResponse with specular mixture bypassed, versus double-precision equations with exact EON integral and independently summed path series; three colors and directions to 85 degrees."
        };
    }

    private static object ProveGpu(AurelianVulkanPlant plant, GameAssets assets)
    {
        using var target = new VulkanNativeFrameTarget(plant, 65, 65);
        float maximumError = 0;
        int cases = 0;
        foreach (bool surface in new[] { false, true })
        {
            using var renderer = CreateRenderer(plant, assets, target, surface);
            foreach (float metallic in new[] { 0f, 1f })
            {
                foreach (float roughness in new[] { .25f, .65f, 1 })
                {
                    foreach (float viewDegrees in new[] { 0f, 55f, 80f })
                    {
                        Vector3 view = Direction(viewDegrees);
                        Vector3 eye = view * 6;
                        Matrix4x4 camera = Camera3D.Matrix(eye, -view, 1);
                        foreach (float lightDegrees in new[] { -55f, 0, 70 })
                        {
                            Vector3 light = Direction(lightDegrees);
                            byte[]? metalControl = null;
                            foreach (var model in Enum.GetValues<DirectDiffuseModel3D>())
                            {
                                renderer.Settings = Graphics3DSettings.Default with
                                {
                                    DirectDiffuseModel = model,
                                    AntiAliasing = AntiAliasing3D.None,
                                    ToneMapping = false,
                                    Shadows = false,
                                    BloomIntensity = 0,
                                    SunColor = Vector3.One,
                                    SunIntensity = .05f,
                                    SunDirection = light,
                                    SkyAmbient = Vector3.Zero,
                                    GroundAmbient = Vector3.Zero,
                                };
                                Vector3 baseColor = new(.18f, .12f, .06f);
                                var material = new ModelMaterial("oracle")
                                {
                                    Metallic = metallic,
                                    Roughness = roughness,
                                    BaseColor = new(baseColor, 1),
                                    DoubleSided = true
                                };
                                var frame = renderer.Render(new Native3DScene([], [Quad(material)]), camera, eye, new(0, 0, 0, 1), capture: true);
                                byte[] pixels = frame.Pixels!;
                                int offset = (32 * 65 + 32) * 4;
                                Vector3 actual = new(NativeSrgbTransfer.Decode(pixels[offset] / 255f),
                                    NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
                                Vector3 expected = DiffuseReference.Direct(model, baseColor, light, view, roughness, metallic) * .05f;
                                float error = Vector3.Distance(actual, expected);
                                maximumError = Math.Max(maximumError, error);
                                Require(error < .008f, $"Diffuse oracle mismatch surface={surface} model={model} roughness={roughness} view={viewDegrees} light={lightDegrees}: {actual} vs {expected} error={error}.");
                                if (metallic == 1)
                                {
                                    metalControl ??= pixels;
                                    Require(metalControl.SequenceEqual(pixels), "Diffuse selection changed a pure metal control.");
                                }
                                cases++;
                            }
                        }
                    }
                }
            }
        }
        return new
        {
            Cases = cases,
            MaximumLinearRgbError = maximumError,
            Reference = "Double-precision paper equations and exact EON directional integrals versus sRGB-decoded native center pixels. Both forward and surface model paths. Pure metals must remain byte-identical."
        };
    }

    private static VulkanSolid3DRenderer CreateRenderer(AurelianVulkanPlant plant, GameAssets assets, VulkanNativeFrameTarget target, bool surface)
    {
        return new(plant, assets.Shader(surface ? "SurfaceSolid3D.v.ts" : "Solid3D.v.ts"), target,
            modelProgram: assets.Shader(surface ? "SurfaceModel3D.v.ts" : "StaticModel3D.v.ts"),
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"),
            surfacePrograms: surface ? new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")) : null);
    }

    private static Vector3 Direction(float degrees)
    {
        float radians = degrees * MathF.PI / 180;
        return new(MathF.Sin(radians), 0, MathF.Cos(radians));
    }

    private static Native3DVertex[] ProbeGeometry(Vector3 color)
    {
        return Quad(new ModelMaterial("probe")).Vertices.Select(vertex =>
            new Native3DVertex(vertex.Position, vertex.Normal, new(color, 1))).ToArray();
    }

    private static Vector3 CenterColor(byte[] pixels)
    {
        int offset = (32 * 65 + 32) * 4;
        return new(NativeSrgbTransfer.Decode(pixels[offset] / 255f),
            NativeSrgbTransfer.Decode(pixels[offset + 1] / 255f), NativeSrgbTransfer.Decode(pixels[offset + 2] / 255f));
    }

    private static double Luminance(Vector3 color)
    {
        return .2126 * color.X + .7152 * color.Y + .0722 * color.Z;
    }

    private static double Chroma(Vector3 color)
    {
        float maximum = Math.Max(color.X, Math.Max(color.Y, color.Z));
        float minimum = Math.Min(color.X, Math.Min(color.Y, color.Z));
        return (maximum - minimum) / Math.Max(maximum, .000001f);
    }

    private static object ProveRichness(VulkanSolid3DRenderer renderer, string output)
    {
        Vector3 red = new(.42f, .075f, .025f);
        double Response(DirectDiffuseModel3D model, float albedo) => DiffuseReference.Response(model, albedo, 1, 1, 1, 1);
        Vector3 Expected(DirectDiffuseModel3D model) => new((float)Response(model, red.X),
            (float)Response(model, red.Y), (float)Response(model, red.Z));
        double target = Luminance(Expected(DirectDiffuseModel3D.Frostbite));
        var captures = new Dictionary<string, byte[]>();
        var observations = new List<object>();
        Vector3 baseline = Vector3.Zero;
        Vector3 candidate = Vector3.Zero;
        foreach (var model in new[] { DirectDiffuseModel3D.Frostbite, DirectDiffuseModel3D.PathDiffuse })
        {
            float gain = (float)(target / Luminance(Expected(model)));
            renderer.Settings = Graphics3DSettings.Default with
            {
                DirectDiffuseModel = model,
                SolidRoughness = 1,
                AntiAliasing = AntiAliasing3D.None,
                ToneMapping = false,
                BloomIntensity = 0,
                Shadows = false,
                SunColor = Vector3.One,
                SunDirection = Vector3.UnitZ,
                SunIntensity = 3 * gain,
                SkyAmbient = Vector3.Zero,
                GroundAmbient = Vector3.Zero,
            };
            Vector3 eye = new(0, 0, 6);
            var frame = renderer.Render(ProbeGeometry(red), Camera3D.Matrix(eye, -Vector3.UnitZ, 1),
                new(0, 0, 0, 1), capture: true, eye: eye);
            Vector3 actual = CenterColor(frame.Pixels!);
            captures[model.ToString()] = frame.Pixels!;
            observations.Add(new
            {
                Model = model.ToString(),
                LightGain = gain,
                LinearRgb = new[] { actual.X, actual.Y, actual.Z },
                Luminance = Luminance(actual),
                Chroma = Chroma(actual)
            });
            if (model == DirectDiffuseModel3D.Frostbite)
            {
                baseline = actual;
            }
            else
            {
                candidate = actual;
            }
        }
        double luminanceError = Math.Abs(Luminance(candidate) / Luminance(baseline) - 1);
        double chromaIncrease = Chroma(candidate) - Chroma(baseline);
        Require(luminanceError < .02, "The red richness control was not matched in linear luminance.");
        Require(chromaIncrease > .005, "Path diffuse did not enrich the red control at matched luminance.");
        using var sheet = new SKBitmap(640, 250);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(16, 20, 27));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 19);
        int index = 0;
        foreach (var capture in captures)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(65, 65, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(capture.Value, 0, bitmap.GetPixels(), capture.Value.Length);
            canvas.DrawText(capture.Key, index * 320 + 20, 30, font, paint);
            canvas.DrawBitmap(bitmap, new SKRect(index * 320 + 20, 48, index * 320 + 300, 205));
            index++;
        }
        canvas.DrawText("Equal linear luminance / no tone mapping / diffuse only", 20, 235, font, paint);
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, "matched-red-control.png"));
        data.SaveTo(file);
        return new
        {
            Observations = observations,
            RelativeLuminanceError = luminanceError,
            ChromaIncrease = chromaIncrease,
            Metric = "(max RGB - min RGB) / max RGB in linear light; an engineering control, not a perceptual colorfulness score."
        };
    }

    private static NativeModel3DBatch Quad(ModelMaterial material)
    {
        NativeModel3DVertex Vertex(float x, float y) => new(new(x * 20, y * 20, 0), Vector3.UnitZ,
            Vector4.One, new((x + 1) / 2, (y + 1) / 2), new(1, 0, 0, 1));
        return new([Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)], material);
    }

    private static (int ChangedPixels, double MeanLinearDifference, float MaximumLinearDifference) Difference(byte[] baseline, byte[] candidate)
    {
        int changed = 0;
        double total = 0;
        float maximum = 0;
        for (int pixel = 0; pixel < baseline.Length; pixel += 4)
        {
            bool different = false;
            for (int channel = 0; channel < 3; channel++)
            {
                float difference = Math.Abs(NativeSrgbTransfer.Decode(baseline[pixel + channel] / 255f)
                    - NativeSrgbTransfer.Decode(candidate[pixel + channel] / 255f));
                total += difference;
                maximum = Math.Max(maximum, difference);
                different |= Math.Abs(baseline[pixel + channel] - candidate[pixel + channel]) > 1;
            }
            if (different)
            {
                changed++;
            }
        }
        return (changed, total / (baseline.Length / 4 * 3), maximum);
    }

    private static void WriteSheet(string path, IReadOnlyDictionary<DirectDiffuseModel3D, byte[]> captures)
    {
        int rows = (captures.Count + 1) / 2;
        using var sheet = new SKBitmap(Width * 2, (Height + 46) * rows);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(16, 20, 27));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 26);
        int index = 0;
        foreach (var model in captures.Keys)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(captures[model], 0, bitmap.GetPixels(), captures[model].Length);
            int x = index % 2 * Width;
            int y = index / 2 * (Height + 46);
            canvas.DrawText(model.ToString().ToUpperInvariant(), x + 20, y + 32, font, paint);
            canvas.DrawBitmap(bitmap, x, y + 46);
            index++;
        }
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private static void WriteFurnace(string output, IReadOnlyList<FurnaceSample> samples)
    {
        using var file = new StreamWriter(Path.Combine(output, "white-furnace.csv"));
        file.WriteLine("model,roughness,viewCosine,unitAlbedoReflectance");
        foreach (var sample in samples)
        {
            file.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{sample.Model},{sample.Roughness},{sample.ViewCosine},{sample.Reflectance:R}"));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
