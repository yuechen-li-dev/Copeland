using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Composition;
using Aurelian.GameMenus;
using Aurelian.Machina;
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
using Machina.Runtime.Input;
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
options.Title = "BEACON RUN | Aurelian Vulkan 3D | Menu: arrows + Enter or click";
options.IsVisible = visible;
options.WindowBorder = WindowBorder.Fixed;
options.VSync = true;
using IWindow window = Window.Create(options);
window.Initialize();
using IInputContext input = window.CreateInput();
using var controls = new BeaconControls();
var app = new BeaconApplication();
var font = AurelianNativeUiFont.Create(Path.Combine(AppContext.BaseDirectory, "Assets"));
var menuView = new GameMenuView(font);
var hud = new BeaconHud(font);
var menuEvents = new Queue<LayerInputEvent>();
using var nativeInput = new BeaconNativeInput(window, input, controls.Adapter, manageFocus: !proof,
    routeInput: menuEvents.Enqueue, cancelPointer: () =>
    {
        menuEvents.Clear();
        menuView.CancelPointer();
    }, focusLost: () =>
    {
        if (app.Screen == BeaconScreen.Playing)
        {
            app.Update(new BeaconCommands(default, true), StepSeconds);
        }
    });
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
using var menuPresenter = new GameMenuNativePresenter(plant, target,
    CompileAsset("AnalyticShape2D.v.ts"), CompileAsset("MsdfText.v.ts"), font);
var clear = new NativeFrameClearColor(0.055f, 0.095f, 0.15f, 1);
var game = app.Game;
ulong frames = 0;
int captures = 0;
Console.WriteLine($"BEACON3D_READY gpu={plant.Facts.PhysicalDeviceName} extent={target.Width}x{target.Height}");

if (proof)
{
    object depthProof = ProveDepth(renderer, target, plant, program, clear, output);
    object menuProof = ProveMenus();
    game = app.Game;
    object fpsProof = ProveFps();
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
        app.Update(controls.Tick(StepSeconds, menuActive: app.Menu is not null), StepSeconds);
        bool capture = game.CollectedCount != previousCollected || game.Won;
        Native3DFrameResult frame = RenderApplication(capture);
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
        fpsProof,
        menuProof,
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
        boundaries = "Geometric FPS with InputMan mouse deltas and authored agents; fixed-size window. No mesh import, shadows, animation rigs, or general 3D physics engine.",
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
        while (menuEvents.TryDequeue(out LayerInputEvent? menuEvent))
        {
            RouteMenuEvent(menuEvent);
        }
        nativeInput.SetGameplayCapture(app.Screen == BeaconScreen.Playing);
        double now = stopwatch.Elapsed.TotalSeconds;
        accumulator += Math.Min(now - previous, 0.1);
        previous = now;
        while (accumulator >= StepSeconds)
        {
            app.Update(controls.Tick(StepSeconds, menuActive: app.Menu is not null), StepSeconds);
            if (app.ExitRequested)
            {
                window.Close();
                break;
            }
            accumulator -= StepSeconds;
        }
        nativeInput.SetGameplayCapture(app.Screen == BeaconScreen.Playing);
        if (window.IsClosing)
        {
            break;
        }
        RenderApplication();
        presenter.Present(++frames);
        window.Title = app.Menu is not null
            ? $"BEACON RUN | {app.Screen} | Arrows + Enter or click; ESC back"
            : $"BEACON RUN | Wave {app.Game.Wave}/3 | Health {app.Game.Health} | Mouse aim, LMB fire, R reload, ESC pause";
    }
}

Native3DFrameResult RenderApplication(bool capture = false)
{
    BeaconGame current = app.Game;
    var world = renderer.Render(BeaconScene.Build(current), current.Camera((float)target.Width / target.Height),
        clear, capture: false);
    if (app.Menu is { } menu)
    {
        var overlay = menuPresenter.Render(menuView, menu, app.SelectedIndex, capture);
        return new Native3DFrameResult(world.TriangleCount, overlay.Pixels, overlay.PixelSha256);
    }
    var hudFrame = menuPresenter.RenderPrepared(hud.Prepare(current), capture);
    return new Native3DFrameResult(world.TriangleCount, hudFrame.Pixels, hudFrame.PixelSha256);
}

void RouteMenuEvent(LayerInputEvent inputEvent)
{
    if (app.Menu is not { } menu)
    {
        menuView.CancelPointer();
        return;
    }
    if (inputEvent is LayerPointerButtonChanged { Button: LayerPointerButton.Primary } pointer)
    {
        string? action = menuView.Pointer(menu, app.SelectedIndex,
            new PointerPoint(pointer.Position.X, pointer.Position.Y), pointer.IsPressed, (int)target.Width, (int)target.Height);
        if (action is not null)
        {
            app.Activate(action);
        }
    }
}

void AdvanceApplication()
{
    app.Update(controls.Tick(StepSeconds, menuActive: app.Menu is not null), StepSeconds);
}

void Press(KeyboardKey key)
{
    controls.Adapter.RecordButton(Controls.Key(key), true);
    AdvanceApplication();
    controls.Adapter.RecordButton(Controls.Key(key), false);
    AdvanceApplication();
}

object ProveFps()
{
    var current = app.Game;
    float yaw = current.Yaw;
    controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 20);
    controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 30);
    AdvanceApplication();
    Require(MathF.Abs(current.Yaw - yaw - 50 * BeaconControls.MouseSensitivity) < 0.00001f,
        "InputMan did not accumulate mouse motion before the tick.");
    float after = current.Yaw;
    AdvanceApplication();
    Require(current.Yaw == after, "Mouse delta was replayed on a later tick.");
    nativeInput.SetGameplayCapture(true);
    nativeInput.SetGameplayCapture(false);
    bool capturedFight = false;
    int ticks = 0;
    for (; ticks < 18000 && current.WavesCleared < 3 && !current.Dead; ticks++)
    {
        window.DoEvents();
        Require(!window.IsClosing, "FPS proof window closed before completion.");
        BeaconProofDriver.Fight(current, controls);
        AdvanceApplication();
        bool capture = !capturedFight && current.Shots > 0 && current.Bolts.Count > 0;
        var frame = RenderApplication(capture);
        presenter.Present(++frames);
        if (capture)
        {
            SavePng("fps-combat.png", frame);
            capturedFight = true;
        }
    }
    controls.Adapter.RecordButton(Controls.Mouse(InputMan.Core.MouseButton.Primary), false);
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), false);
    Require(!current.Dead && current.WavesCleared == 3 && current.Kills == 9,
        $"FPS proof failed: health={current.Health}, waves={current.WavesCleared}, kills={current.Kills}, shots={current.Shots}.");
    Require(current.AgentTicks > 0 && current.ActiveCreatureBrains == 0 && current.Shots >= 18 && capturedFight,
        "Combat did not exercise live agent decisions and projectile rendering.");
    // Restore the route camera using mouse input, without writing camera state.
    BeaconProofDriver.Aim(current, controls, current.Eye + new Vector3(0, -0.08f, -1));
    AdvanceApplication();
    SavePng("fps-cleared.png", RenderApplication(capture: true));
    return new
    {
        accumulatedMouseDeltas = true,
        mouseDeltaConsumedOnce = true,
        nativeCaptureTransitions = true,
        current.WavesCleared,
        current.Kills,
        current.Shots,
        current.Health,
        current.AgentTicks,
        current.ActiveCreatureBrains,
        ticks,
        characterAgents = current.Agents.Count(agent => agent.Template.Kind == Aurelian.World.Agents.AgentKind.Character),
        objectAgents = current.Agents.Count(agent => agent.Template.Kind == Aurelian.World.Agents.AgentKind.Object),
        creatureAgents = current.Creatures.Count,
    };
}

object ProveMenus()
{
    Require(app.Screen == BeaconScreen.Title, "The game did not boot into the title menu.");
    var title = RenderApplication(capture: true);
    SavePng("menu-title.png", title);
    presenter.Present(++frames);
    int uploads = menuPresenter.FontUploads;
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), true);
    for (int tick = 0; tick < 60; tick++)
    {
        AdvanceApplication();
    }
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), false);
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), false);
    Require(app.Game.Time == 0 && app.Game.Position == new Vector2(0, 9), "Gameplay input leaked through the title menu.");
    Press(KeyboardKey.ArrowDown);
    Press(KeyboardKey.Enter);
    Require(app.Screen == BeaconScreen.Controls, "InputMan navigation did not open Controls.");
    SavePng("menu-controls.png", RenderApplication(capture: true));
    presenter.Present(++frames);
    Press(KeyboardKey.Escape);
    Require(app.Screen == BeaconScreen.Title, "Controls did not return to its originating screen.");
    PointerPoint start = menuView.ActionCenter(app.Menu!, app.SelectedIndex, "new-game", (int)target.Width, (int)target.Height);
    var point = new LayerPoint(start.X, start.Y);
    RouteMenuEvent(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
    RouteMenuEvent(new LayerPointerButtonChanged(new LayerPoint(1, 1), LayerPointerButton.Primary, false));
    Require(app.Screen == BeaconScreen.Title, "Releasing outside the pressed button activated Start.");
    RouteMenuEvent(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
    RouteMenuEvent(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, false));
    Require(app.Screen == BeaconScreen.Playing, "Clicking the rendered Start button did not start the game.");
    Press(KeyboardKey.Escape);
    Require(app.Screen == BeaconScreen.Paused, "Escape did not pause gameplay.");
    float pausedTime = app.Game.Time;
    Vector2 pausedPosition = app.Game.Position;
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
    for (int tick = 0; tick < 60; tick++)
    {
        AdvanceApplication();
    }
    controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), false);
    Require(app.Game.Time == pausedTime && app.Game.Position == pausedPosition, "Pause did not freeze game state.");
    var paused = RenderApplication(capture: true);
    SavePng("menu-paused.png", paused);
    presenter.Present(++frames);
    var repeated = RenderApplication(capture: true);
    Require(paused.PixelSha256 == repeated.PixelSha256, "An unchanged pause menu is not repeatable.");
    Require(menuPresenter.FontUploads == uploads, "Menu navigation reuploaded font textures.");
    PointerPoint restart = menuView.ActionCenter(app.Menu!, app.SelectedIndex, "restart", (int)target.Width, (int)target.Height);
    var restartPoint = new LayerPoint(restart.X, restart.Y);
    RouteMenuEvent(new LayerPointerButtonChanged(restartPoint, LayerPointerButton.Primary, true));
    controls.Adapter.OnFocusChanged(false);
    menuView.CancelPointer();
    controls.Adapter.OnFocusChanged(true);
    RouteMenuEvent(new LayerPointerButtonChanged(restartPoint, LayerPointerButton.Primary, false));
    Require(app.Screen == BeaconScreen.Paused, "Focus loss retained a stale pointer press.");
    Press(KeyboardKey.Space);
    Require(app.Screen == BeaconScreen.Playing && app.Game.Height == 0, "Resume failed or menu confirm leaked into Jump.");
    return new
    {
        bootedToTitle = true,
        inputManNavigation = true,
        renderedButtonPointerActivation = true,
        mismatchedReleaseCancelled = true,
        controlsReturnsToOrigin = true,
        gameStateFrozenInMenus = true,
        focusLossCancelsPointer = true,
        confirmDoesNotLeakIntoJump = true,
        retainedFontUploads = uploads,
        titleHash = title.PixelSha256,
        pauseHash = paused.PixelSha256,
    };
}

CompiledGraphicsProgram CompileAsset(string name)
{
    string shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name));
    var shaderModule = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(name, shader)]));
    Require(shaderModule.Success, string.Join("; ", shaderModule.Diagnostics.Select(item => item.Message)));
    return CompiledGraphicsProgramExporter.Export(shaderModule, VdMirGraphicsBackend.Compile(shaderModule));
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
