using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using InputMan.Core;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

const int Width = 960;
const int Height = 600;
const float StepSeconds = 1f / 60;
bool proof = args.Contains("--proof", StringComparer.Ordinal);
bool visible = !proof || args.Contains("--visible", StringComparer.Ordinal);
string output = Path.GetFullPath(GetOption(args, "--output") ?? "artifacts/aurelian-beacon3d");
Directory.CreateDirectory(output);

string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Solid3D.v.ts"));
var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("Solid3D.v.ts", source)]));
Require(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => item.Message)));
VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
CompiledGraphicsProgram program = CompiledGraphicsProgramExporter.Export(module, backend);
File.WriteAllText(Path.Combine(output, "solid3d.hlsl"), backend.Hlsl);

WindowOptions options = WindowOptions.DefaultVulkan;
options.Size = new Vector2D<int>(Width, Height);
options.Title = "BEACON RUN | Aurelian Vulkan 3D | WASD move, arrows look, Space jump, R restart, Esc quit";
options.IsVisible = visible;
options.WindowBorder = WindowBorder.Fixed;
options.VSync = true;
using IWindow window = Window.Create(options);
window.Initialize();
using IInputContext input = window.CreateInput();
using var controls = new BeaconControls();
using var nativeInput = new BeaconNativeInput(window, input, controls.Adapter, manageFocus: !proof);
var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(
    EnableValidation: true, ApplicationName: "Beacon Run", EnablePresentation: true,
    RequiredPresentationInstanceExtensions: ReadRequiredExtensions(window)));
Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
using AurelianVulkanPlant plant = init.Plant!;
var created = VulkanSwapchainFactory.Create(plant, window,
    new VulkanSwapchainCreateOptions(Width, Height, VSync: true, "Beacon Run", Visible: visible));
Require(created.Success, string.Join("; ", created.Diagnostics.Select(item => item.Message)));
using AurelianVulkanSurface surface = created.Surface!;
using AurelianVulkanSwapchain swapchain = created.Swapchain!;
VulkanTextureFormat format = swapchain.Facts.SelectedFormat switch
{
    "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
    "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
    "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
    "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
    _ => throw new InvalidOperationException("Unsupported swapchain format: " + swapchain.Facts.SelectedFormat),
};
using var target = new VulkanNativeFrameTarget(plant, swapchain.Facts.Width, swapchain.Facts.Height, format);
using var renderer = new VulkanSolid3DRenderer(plant, program, target);
using var presenter = new VulkanNativeSwapchainPresenter(plant, target, swapchain);
var clear = new NativeFrameClearColor(0.055f, 0.095f, 0.15f, 1);
var game = new BeaconGame();
ulong frames = 0;
int captures = 0;
Console.WriteLine($"BEACON3D_READY gpu={plant.Facts.PhysicalDeviceName} extent={target.Width}x{target.Height}");

if (proof)
{
    object depthProof = ProveDepth(renderer, target, plant, program, clear, output);
    Native3DFrameResult initial = renderer.Render(BeaconScene.Build(game), game.Camera((float)target.Width / target.Height), clear, capture: true);
    SavePng("start.png", initial);
    Native3DFrameResult repeat = renderer.Render(BeaconScene.Build(game), game.Camera((float)target.Width / target.Height), clear, capture: true);
    Require(initial.PixelSha256 == repeat.PixelSha256, "Identical initial scene is not repeatable.");
    presenter.Present(++frames);
    Vector2[] route = [new(-7, 9), new(-7, 3), new(-7, -8), new(0, -8), new(0, -7), new(7, -8), new(7, 1), new(6, 1), new(7, -9), BeaconGame.Exit];
    int waypoint = 0;
    int previousCollected = 0;
    var trace = new List<object>();
    for (int tick = 0; tick < 7200 && !game.Won; tick++)
    {
        window.DoEvents();
        Require(!window.IsClosing, "Proof window was closed before completion.");
        if (waypoint < route.Length && Vector2.Distance(game.Position, route[waypoint]) < 0.18f)
        {
            waypoint++;
        }
        Require(waypoint < route.Length, "Route ended before the game won.");
        Vector2 delta = route[waypoint] - game.Position;
        float desiredYaw = MathF.Atan2(delta.X, -delta.Y);
        float yawError = MathF.IEEERemainder(desiredYaw - game.Yaw, MathF.Tau);
        // Inject physical key state through the same InputMan bindings used by native callbacks.
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), MathF.Abs(yawError) < 0.08f);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.ArrowRight), yawError > 0.02f);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.ArrowLeft), yawError < -0.02f);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.Space), tick == 5);
        game.Step(controls.Tick(StepSeconds).Movement, StepSeconds);
        bool capture = game.CollectedCount != previousCollected || game.Won;
        Native3DFrameResult frame = renderer.Render(BeaconScene.Build(game), game.Camera((float)target.Width / target.Height), clear, capture);
        presenter.Present(++frames);
        if (capture)
        {
            SavePng(game.Won ? "win.png" : $"collected-{game.CollectedCount}.png", frame);
            trace.Add(new { tick, x = game.Position.X, z = game.Position.Y, collected = game.CollectedCount, game.Won, frame.PixelSha256 });
        }
        previousCollected = game.CollectedCount;
    }
    Require(game.Won && game.CollectedCount == 3, "Scripted movement did not collect all beacons and reach the exit.");
    File.WriteAllText(Path.Combine(output, "proof.json"), JsonSerializer.Serialize(new
    {
        schema = "aurelian.beacon3d.proof.v1",
        outcome = "Success",
        gpu = plant.Facts,
        camera = "GPU world-to-clip rows; perspective; near=0.1, far=80; Vulkan depth [0,1]",
        depthFormat = "D32_SFLOAT",
        depthProof,
        repeatedInitialFrame = initial.PixelSha256 == repeat.PixelSha256,
        framesPresented = frames,
        captureCount = captures,
        gameTimeSeconds = game.Time,
        collected = game.CollectedCount,
        game.Won,
        inputAuthority = "InputMan bindings via AurelianInputAdapter; scripted physical key snapshots and native SilkInputBridge use the same path",
        trace,
        shader = new { program.VdMirSha256, backend.HlslSha256, backend.Vertex.SpirvSha256, pixelSpirvSha256 = backend.Pixel.SpirvSha256 },
        validationLayers = plant.Facts.EnabledValidationLayers,
        validationNote = "Layer availability is recorded; this sample does not count debug-messenger callbacks. Vulkan results and pixel assertions are enforced.",
        boundaries = "Opaque untextured triangle lists and keyboard input; fixed-size window. No general mesh assets, shadows, animation rigs, or 3D physics engine.",
    }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    Console.WriteLine($"BEACON3D_PROOF_PASSED frames={frames} collected=3 won=true artifacts={output}");
}
else
{
    var stopwatch = Stopwatch.StartNew();
    double previous = stopwatch.Elapsed.TotalSeconds;
    double accumulator = 0;
    while (!window.IsClosing)
    {
        window.DoEvents();
        double now = stopwatch.Elapsed.TotalSeconds;
        accumulator += Math.Min(now - previous, 0.1);
        previous = now;
        while (accumulator >= StepSeconds)
        {
            BeaconCommands commands = controls.Tick(StepSeconds);
            if (commands.Quit)
            {
                window.Close();
                break;
            }
            if (commands.Restart)
            {
                game = new BeaconGame();
            }
            game.Step(commands.Movement, StepSeconds);
            accumulator -= StepSeconds;
        }
        if (window.IsClosing)
        {
            break;
        }
        renderer.Render(BeaconScene.Build(game), game.Camera((float)target.Width / target.Height), clear);
        presenter.Present(++frames);
        window.Title = game.Won
            ? $"BEACON RUN | YOU WIN! {game.Time:F1}s | R restart, Esc quit"
            : $"BEACON RUN | {game.CollectedCount}/3 beacons — then enter the gate | WASD move, arrows look, Space jump, R restart, Esc quit";
    }
}

void SavePng(string name, Native3DFrameResult frame)
{
    WritePng(Path.Combine(output, name), (int)target.Width, (int)target.Height, frame.Pixels!);
    captures++;
}

static object ProveDepth(VulkanSolid3DRenderer renderer, VulkanNativeFrameTarget target,
    AurelianVulkanPlant plant, CompiledGraphicsProgram program, NativeFrameClearColor clear, string output)
{
    var near = new List<Native3DVertex>();
    var far = new List<Native3DVertex>();
    BeaconScene.AddBox(near, new(0, 0, -3), new(0.7f, 0.7f, 0.7f), new(0.1f, 1, 0.2f, 1));
    BeaconScene.AddBox(far, new(0.4f, 0, -5), new(1.4f, 1.4f, 0.5f), new(1, 0.1f, 0.15f, 1));
    Matrix4x4 camera = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, (float)target.Width / target.Height, 0.1f, 80);
    camera.M22 = -camera.M22;
    Native3DVertex[] nearFirst = [.. near, .. far];
    Native3DVertex[] farFirst = [.. far, .. near];
    var first = renderer.Render(nearFirst, camera, clear, capture: true);
    var reverse = renderer.Render(farFirst, camera, clear, capture: true);
    Require(first.PixelSha256 == reverse.PixelSha256, "Depth-enabled cube overlap depends on submission order.");
    int center = ((int)target.Height / 2 * (int)target.Width + (int)target.Width / 2) * 4;
    Require(first.Pixels![center + 1] > first.Pixels[center] * 2, "Near green cube did not occlude far red cube at the center.");
    int greenPixels = 0;
    int redPixels = 0;
    for (int offset = 0; offset < first.Pixels.Length; offset += 4)
    {
        if (first.Pixels[offset + 1] > first.Pixels[offset] * 2)
        {
            greenPixels++;
        }
        if (first.Pixels[offset] > first.Pixels[offset + 1] * 2)
        {
            redPixels++;
        }
    }
    Require(greenPixels > 1000 && redPixels > 1000, "Both overlapping 3D objects must contribute visible pixels.");
    WritePng(Path.Combine(output, "depth-enabled.png"), (int)target.Width, (int)target.Height, first.Pixels);
    using var noDepth = new VulkanSolid3DRenderer(plant, program, target, enableDepth: false);
    var disabled = noDepth.Render(nearFirst, camera, clear, capture: true);
    var disabledReverse = noDepth.Render(farFirst, camera, clear, capture: true);
    Require(disabled.PixelSha256 != disabledReverse.PixelSha256, "Depth-disabled control did not detect order-dependent overlap.");
    Require(disabled.Pixels![center] > disabled.Pixels[center + 1] * 2, "Depth-disabled far red cube should overwrite the center.");
    WritePng(Path.Combine(output, "depth-disabled.png"), (int)target.Width, (int)target.Height, disabled.Pixels);
    // A changed camera must move the projected geometry without altering the world vertices.
    var moved = renderer.Render(nearFirst, Matrix4x4.CreateTranslation(-1, 0, 0) * camera, clear, capture: true);
    Require(moved.PixelSha256 != first.PixelSha256, "Changing the camera did not change the GPU projection.");
    return new
    {
        orderIndependentWithDepth = true,
        orderDependentWithoutDepth = true,
        nearOccludesFar = true,
        cameraUniformChangesPixels = true,
        greenPixels,
        redPixels,
        enabledHash = first.PixelSha256,
        disabledHash = disabled.PixelSha256,
        movedCameraHash = moved.PixelSha256,
    };
}

static void WritePng(string path, int width, int height, byte[] pixels)
{
    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
    Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
    using SKImage image = SKImage.FromBitmap(bitmap);
    using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
    using FileStream file = File.Create(path);
    data.SaveTo(file);
}

static unsafe IReadOnlyList<string> ReadRequiredExtensions(IWindow window)
{
    var surface = window.VkSurface ?? throw new InvalidOperationException("Window has no Vulkan surface.");
    byte** extensions = surface.GetRequiredExtensions(out uint count);
    var names = new List<string>();
    for (int index = 0; index < count; index++)
    {
        string? name = SilkMarshal.PtrToString((nint)extensions[index], NativeStringEncoding.UTF8);
        if (name is not null)
        {
            names.Add(name);
        }
    }
    return names;
}

static string? GetOption(string[] arguments, string name)
{
    int index = Array.IndexOf(arguments, name);
    if (index < 0)
    {
        return null;
    }
    if (index + 1 == arguments.Length)
    {
        throw new ArgumentException(name + " requires a value.");
    }
    return arguments[index + 1];
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
