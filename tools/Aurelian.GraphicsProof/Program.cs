using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aetheris.Humanoid;
using Aurelian.Assets.Models;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Humanoid;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.World.Scenes;
using Silk.NET.Maths;
using Silk.NET.Input;
using Silk.NET.Windowing;

string defaultOutput = "artifacts/local/graphics-starter";
if (args.Contains("--shadow-experiment", StringComparer.Ordinal))
{
    defaultOutput = "artifacts/aurelian-shadow-distance";
}
if (args.Contains("--field-lighting", StringComparer.Ordinal))
{
    defaultOutput = "artifacts/local/aetheris-field-lighting";
}
string output = Path.GetFullPath(Option("--output") ?? defaultOutput);
if (args.Contains("--lighting-experts", StringComparer.Ordinal) || args.Contains("--local-lighting-experts", StringComparer.Ordinal)
    || args.Contains("--constrained-lighting-experts", StringComparer.Ordinal)
    || args.Contains("--continuous-lighting-experts", StringComparer.Ordinal)
    || args.Contains("--adaptive-lighting-experts", StringComparer.Ordinal))
{
    bool adaptiveExperts = args.Contains("--adaptive-lighting-experts", StringComparer.Ordinal);
    bool continuousExperts = adaptiveExperts || args.Contains("--continuous-lighting-experts", StringComparer.Ordinal);
    bool constrainedExperts = continuousExperts || args.Contains("--constrained-lighting-experts", StringComparer.Ordinal);
    bool localExperts = constrainedExperts || args.Contains("--local-lighting-experts", StringComparer.Ordinal);
    string expertOutput = localExperts ? "artifacts/local/local-lighting-experts" : "artifacts/local/lighting-experts";
    output = Path.GetFullPath(Option("--output") ?? expertOutput);
    LightingExpertExperiment.Run(output, Option("--blender"), args.Contains("--reuse-reference", StringComparer.Ordinal),
        localExperts, constrainedExperts, continuousExperts, adaptiveExperts);
    return;
}
if (args.Contains("--lighting-compilation", StringComparer.Ordinal))
{
    output = Path.GetFullPath(Option("--output") ?? "artifacts/local/lighting-compilation");
    LightingCompilationExperiment.Run(output);
    return;
}
if (args.Contains("--field-lighting", StringComparer.Ordinal))
{
    FieldLightingExperiment.Run(output);
    return;
}
if (args.Contains("--shadow-experiment", StringComparer.Ordinal))
{
    ShadowDistanceExperiment.Run(output);
    return;
}
if (args.Contains("--language-proof", StringComparer.Ordinal) || args.Contains("--shape-proof", StringComparer.Ordinal)
    || args.Contains("--generic-proof", StringComparer.Ordinal))
{
    LanguagePortProof.Run(output, args.Contains("--shape-proof", StringComparer.Ordinal), args.Contains("--generic-proof", StringComparer.Ordinal));
    return;
}
string assetDirectory = Path.GetFullPath(Option("--assets") ?? "Games/Starter/Aurelian.Starter/Assets");
Directory.CreateDirectory(output);
var assets = new GameAssets();
var catalog = ModelAssetCatalog.Load(Path.Combine(assetDirectory, "assets.toml"));
var crate = catalog.Model("starter.crate");
SceneGroup gallery = Scene.World("graphics-gallery",
[
    Scene.Box("floor", new(16, .2f, 16), new(.38f, .42f, .46f, 1), at: new(0, -.1f, 0)),
    Scene.Box("back-wall", new(16, 4, .36f), new(.6f, .55f, .47f, 1), at: new(0, 2, -5)),
    Scene.Box("left-wall", new(.36f, 4, 10), new(.3f, .38f, .44f, 1), at: new(-6, 2, 0)),
    Scene.Box("pillar-left", new(.44f, 3.8f, .44f), new(.66f, .62f, .54f, 1), at: new(-2.6f, 1.9f, -1.8f)),
    Scene.Box("pillar-right", new(.44f, 3.8f, .44f), new(.66f, .62f, .54f, 1), at: new(2.6f, 1.9f, -1.8f)),
    Scene.Box("beam", new(5.64f, .4f, .64f), new(.66f, .62f, .54f, 1), at: new(0, 3.6f, -1.8f)),
    Scene.Box("red-block", new(1.3f, 1.3f, 1.3f), new(.64f, .10f, .065f, 1), at: new(2.7f, .65f, 1.7f)),
    Scene.Model("textured-crate", crate, new(-2.4f, 0, 1.6f)),
]);
using var mounted = SceneCompiler.Compile(gallery).Mount();
Native3DScene scene = SceneGeometry3D.BuildScene(mounted.Project());
// Ordinary material overrides exercise the same textured path as game assets.
ModelMaterial bronze = new("bronze") { BaseColor = new(.68f, .32f, .09f, 1), Metallic = 1, Roughness = .22f };
ModelMaterial paint = new("paint") { BaseColor = new(.045f, .25f, .44f, 1), Metallic = 0, Roughness = .3f };
ModelMaterial lamp = new("lamp") { BaseColor = new(.04f, .04f, .04f, 1), Metallic = 0, Emissive = new(1, .5f, .12f) };
var batches = scene.Models.ToList();
batches.Add(Sphere(new(-1.35f, .8f, -2.7f), .8f, bronze));
batches.Add(Sphere(new(1.35f, .8f, -2.7f), .8f, paint));
batches.Add(Sphere(new(0, 3.2f, -2.4f), .12f, lamp));
scene = scene with { Models = batches };
Vector3 eye = new(5.2f, 3.1f, 7.5f);
Matrix4x4 camera = Camera3D.Matrix(eye, Vector3.Normalize(new Vector3(0, 1.3f, -.5f) - eye), 1.6f);
NativeFrameClearColor clear = new(.18f, .27f, .38f, 1);
HumanoidPlayerOptions? body = Option("--body") is { } bodyPath ? HumanoidPlayerOptions.Load(bodyPath) : null;
SolvedHumanoidPose? pose = null;
if (body is not null)
{
    if (Option("--locomotion") is { } bankPath)
    {
        var bank = HumanoidLocomotionBank.Load(bankPath, body.Body);
        var clip = bank.Clip(HumanoidGait.Walk);
        var sample = clip.Sample(clip.Duration * .22);
        var solved = HumanoidKinematicSolver.SolveAuthored(body.Body.Skeleton,
            new("graphics-walk", body.Body.Skeleton.SkeletonId, body.Body.Skeleton.RestPoseId, sample.Rotations, sample.OffsetMm));
        if (!solved.IsSolved)
        {
            throw new InvalidDataException("Gallery walking pose could not be solved.");
        }
        pose = solved.Pose!;
    }
    else
    {
        pose = body.Body.Solve("graphics-rest", []).Pose!;
    }
}

if (args.Contains("--view", StringComparer.Ordinal) || args.Contains("--launch-smoke", StringComparer.Ordinal))
{
    View();
    return;
}
File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"accepted\":false}");
var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Graphics Starter Proof"));
Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
using var plant = initialized.Plant!;
using var target = new VulkanNativeFrameTarget(plant, 1280, 800, VulkanTextureFormat.Rgba8Srgb);
using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
    modelProgram: assets.Shader("StaticModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
using var gpu = CreateBody(plant);
var captures = new List<object>();
var basic = Capture("basic", Graphics3DSettings.Basic);
var modern = Capture("modern", Graphics3DSettings.Default);
var repeat = Capture("repeat", Graphics3DSettings.Default);
var noShadow = Capture("no-shadows", Graphics3DSettings.Default with { Shadows = false });
var exposure = Capture("low-exposure", Graphics3DSettings.Default with { Exposure = .35f });
var opposite = Capture("opposite-sun", Graphics3DSettings.Default with { SunDirection = new(-.6f, .6f, -.5f) });
Require(modern.PixelSha256 == repeat.PixelSha256, "Identical scene settings did not reproduce identical pixels.");
int shadowPixels = Changed(modern.Pixels!, noShadow.Pixels!);
int exposurePixels = Changed(modern.Pixels!, exposure.Pixels!);
Require(Changed(basic.Pixels!, modern.Pixels!) > 10_000, "Modern lighting did not materially change the scene.");
Require(shadowPixels > 1_000, "Shadow toggle did not affect enough visible pixels.");
Require(exposurePixels > 10_000, "HDR exposure did not affect the scene.");
Require(Changed(modern.Pixels!, opposite.Pixels!) > 10_000, "Scene sun direction was not configurable.");
renderer.Settings = Graphics3DSettings.Default;
var receiverVertices = new List<Native3DVertex>();
PrimitiveGeometry3D.AddBox(receiverVertices, new(0, -.1f, 0), new(8, .1f, 8), new(.38f, .42f, .46f, 1));
var receiverOnly = new Native3DScene(receiverVertices.ToArray(), []);
var receiverShadow = renderer.Render(receiverOnly, camera, eye, clear, capture: true);
renderer.Settings = Graphics3DSettings.Default with { Shadows = false };
var receiverNoShadow = renderer.Render(receiverOnly, camera, eye, clear, capture: true);
int receiverAcne = Changed(receiverShadow.Pixels!, receiverNoShadow.Pixels!);
NativeGameGraphics.WritePng(Path.Combine(output, "receiver-only.png"), 1280, 800, receiverShadow.Pixels!);
Require(receiverAcne < 100, "Receiver-only floor self-shadowed " + receiverAcne + " pixels.");
captures.Add(new { Name = "receiver-only", SelfShadowPixels = receiverAcne });
renderer.Settings = Graphics3DSettings.Default;
var withoutBody = renderer.Render(scene, camera, eye, clear, capture: true);
if (gpu is not null)
{
    Require(Changed(withoutBody.Pixels!, modern.Pixels!) > 500, "GPU-skinned body was not visible.");
    renderer.Settings = Graphics3DSettings.Default with { Shadows = false };
    var withoutBodyOrShadows = renderer.Render(scene, camera, eye, clear, capture: true);
    int bodyShadowPixels = ChangedOutsideBody(modern.Pixels!, noShadow.Pixels!, withoutBody.Pixels!, withoutBodyOrShadows.Pixels!);
    Require(bodyShadowPixels > 50, "GPU-skinned geometry did not cast a visible shadow outside its silhouette.");
    captures.Add(new { Name = "gpu-body-shadow", ChangedPixels = bodyShadowPixels });
}
// Both target encodings must display the same image; UNORM requires explicit shader encoding.
using (var unormTarget = new VulkanNativeFrameTarget(plant, 1280, 800))
using (var unormRenderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), unormTarget,
    modelProgram: assets.Shader("StaticModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts")))
{
    var unorm = unormRenderer.Render(scene, camera, eye, clear, capture: true, gpuGeometry: gpu?.Geometry);
    int maximumError = modern.Pixels!.Zip(unorm.Pixels!, (a, b) => Math.Abs(a - b)).Max();
    Require(maximumError <= 2, "sRGB and UNORM outputs disagree: " + maximumError);
    captures.Add(new { Name = "output-color-space", MaximumByteError = maximumError });
}
File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
{
    Accepted = true, Device = plant.Facts.PhysicalDeviceName, TriangleCount = modern.TriangleCount,
    ShadowPixels = shadowPixels, ExposurePixels = exposurePixels, GpuSkinnedBody = gpu is not null,
    HdrFormat = "R16G16B16A16_SFLOAT", ShadowFormat = "R32_SFLOAT", Captures = captures,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("AURELIAN_GRAPHICS_STARTER_PROOF_PASSED " + plant.Facts.PhysicalDeviceName);

Native3DFrameResult Capture(string name, Graphics3DSettings settings)
{
    renderer.Settings = settings;
    var watch = Stopwatch.StartNew();
    var result = renderer.Render(scene, camera, eye, clear, capture: true, gpuGeometry: gpu?.Geometry);
    watch.Stop();
    NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 1280, 800, result.Pixels!);
    captures.Add(new { Name = name, result.PixelSha256, result.GpuPassTimes, SubmitAndReadbackMilliseconds = watch.Elapsed.TotalMilliseconds });
    return result;
}

HumanoidGpuBody? CreateBody(AurelianVulkanPlant device)
{
    if (body is null)
    {
        return null;
    }
    var result = new HumanoidGpuBody(device, assets.ComputeShader("HumanoidSkinning.v.ts"), body.Body);
    result.Present(pose!, Matrix4x4.Identity);
    return result;
}

void View()
{
    var options = WindowOptions.DefaultVulkan;
    options.Size = new Vector2D<int>(1280, 800);
    options.Title = "Aurelian Graphics Gallery — V compares basic/modern, Esc closes";
    bool smoke = args.Contains("--launch-smoke", StringComparer.Ordinal);
    options.IsVisible = !smoke;
    using var window = Window.Create(options);
    window.Initialize();
    using var input = window.CreateInput();
    using var controls = new GameControls();
    using var captured = new CapturedGameInput(window, input, controls.Adapter, manageFocus: !smoke);
    using var graphics = new NativeGameGraphics(window, options.Title, !smoke, assets);
    using var character = CreateBody(graphics.Plant);
    bool modern = true;
    ulong frame = 0;
    var hashes = new List<string>();
    while (!window.IsClosing)
    {
        window.DoEvents();
        var commands = controls.Tick(1f / 60);
        if (commands.Pause)
        {
            window.Close();
        }
        if (commands.SwitchView)
        {
            modern = !modern;
            window.Title = "Aurelian Graphics Gallery — " + (modern ? "modern" : "basic") + " — V compares, Esc closes";
        }
        graphics.Settings = modern ? Graphics3DSettings.Default : Graphics3DSettings.Basic;
        graphics.Clear = clear;
        var rendered = graphics.Render(scene, camera, eye, capture: smoke, gpuGeometry: character?.Geometry);
        graphics.Presenter.Present(++frame);
        if (smoke)
        {
            hashes.Add(rendered.PixelSha256!);
            if (frame == 1 || frame == 3)
            {
                controls.Adapter.RecordButton(InputMan.Core.Controls.Key(InputMan.Core.KeyboardKey.V), true);
            }
            else if (frame == 2)
            {
                controls.Adapter.RecordButton(InputMan.Core.Controls.Key(InputMan.Core.KeyboardKey.V), false);
            }
            else if (frame == 4)
            {
                Require(hashes[0] == hashes[3] && hashes[0] != hashes[1], "Native gallery did not switch and restore graphics through InputMan.");
                Console.WriteLine("AURELIAN_GRAPHICS_GALLERY_NATIVE_SMOKE_PASSED frames=4");
                window.Close();
            }
        }
    }
}

static NativeModel3DBatch Sphere(Vector3 center, float radius, ModelMaterial material)
{
    var vertices = new List<NativeModel3DVertex>();
    const int rows = 24;
    const int columns = 48;
    NativeModel3DVertex Vertex(int row, int column)
    {
        float latitude = MathF.PI * row / rows;
        float longitude = MathF.Tau * column / columns;
        Vector3 normal = new(MathF.Sin(latitude) * MathF.Cos(longitude), MathF.Cos(latitude), MathF.Sin(latitude) * MathF.Sin(longitude));
        Vector3 tangent = new(-MathF.Sin(longitude), 0, MathF.Cos(longitude));
        return new(center + normal * radius, normal, Vector4.One, new((float)column / columns, (float)row / rows), new(tangent, 1));
    }
    for (int row = 0; row < rows; row++)
    {
        for (int column = 0; column < columns; column++)
        {
            var a = Vertex(row, column);
            var b = Vertex(row + 1, column);
            var c = Vertex(row + 1, column + 1);
            var d = Vertex(row, column + 1);
            vertices.AddRange([a, d, c, a, c, b]);
        }
    }
    return new(vertices.ToArray(), material with { DoubleSided = true });
}

static int Changed(byte[] left, byte[] right)
{
    int count = 0;
    for (int index = 0; index < left.Length; index += 4)
    {
        if (Math.Abs(left[index] - right[index]) + Math.Abs(left[index + 1] - right[index + 1])
            + Math.Abs(left[index + 2] - right[index + 2]) > 8)
        {
            count++;
        }
    }
    return count;
}

static int ChangedOutsideBody(byte[] bodyShadow, byte[] bodyNoShadow, byte[] noBodyShadow, byte[] noBodyNoShadow)
{
    int count = 0;
    for (int index = 0; index < bodyShadow.Length; index += 4)
    {
        // Visible body pixels change without shadows too. Exclude those to isolate its cast shadow on the scene.
        int silhouette = 0;
        int shadow = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            silhouette += Math.Abs(bodyNoShadow[index + channel] - noBodyNoShadow[index + channel]);
            shadow += Math.Abs(bodyShadow[index + channel] - noBodyShadow[index + channel]);
        }
        if (silhouette <= 2 && shadow > 8)
        {
            count++;
        }
    }
    return count;
}

string? Option(string name)
{
    int index = Array.IndexOf(args, name);
    if (index < 0)
    {
        return null;
    }
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException("Missing value for " + name);
    }
    return args[index + 1];
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
