using System.Diagnostics;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Aurelian.Composition;
using Aurelian.GameHost;
using Aurelian.GameHost.Silk;
using Aurelian.GameMenus;
using Aurelian.Playtesting;
using Machina.Runtime.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Aurelian.World.Scenes;
using Aurelian.Spatial3D.Vulkan;
using Aurelian.Runtime;

namespace Aurelian.Games;

public static class GameStarter
{
    public static StarterGame Create(string id, IEnumerable<GameConcept> concepts, StarterOptions? options = null,
        string? saveDirectory = null, SceneGroup? sceneDocument = null, HumanoidPlayerOptions? humanoid = null)
    {
        return new(id, new GameDefinition(concepts), options, saveDirectory, sceneDocument: sceneDocument, humanoid: humanoid);
    }

    /// <summary>One explicit bootstrap for native, scripted native and deterministic headless execution.</summary>
    public static void Run(string id, IEnumerable<GameConcept> concepts, string[] args, StarterOptions? options = null,
        Action<StarterGame>? configure = null, SceneGroup? sceneDocument = null, HumanoidPlayerOptions? humanoid = null)
    {
        GameDefinition definition = new(concepts);
        options ??= new();
        bool headless = args.Contains("--headless", StringComparer.Ordinal);
        string? script = Option(args, "--playtest-script");
        bool stdio = args.Contains("--playtest-stdio", StringComparer.Ordinal);
        bool playtest = script is not null || stdio;
        bool smoke = args.Contains("--launch-smoke", StringComparer.Ordinal);
        if (headless && !playtest) throw new ArgumentException("Headless execution requires a playtest script or stdin session.");
        if (script is not null && stdio) throw new ArgumentException("Select one playtest input source.");
        string output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine("artifacts", id));
        IAudioOutputBackend backend = new NullAudioOutputBackend();
        if (!headless && !playtest)
        {
            if (NAudioOutputBackend.TryCreate(out NAudioOutputBackend? nativeAudio, out string? error)) backend = nativeAudio!;
            else Console.Error.WriteLine("Audio device unavailable; using null output: " + error);
        }
        using var game = new StarterGame(id, definition, options, Option(args, "--save-root"), backend, sceneDocument, humanoid);
        configure?.Invoke(game);
        if (headless)
        {
            RunPlaytest(new StarterPlaytestTarget(game), script, output);
            return;
        }
        bool visible = (!playtest && !smoke) || args.Contains("--visible", StringComparer.Ordinal);
        WindowOptions windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.Size = new Vector2D<int>(960, 600);
        windowOptions.Title = options.Title;
        windowOptions.WindowBorder = WindowBorder.Fixed;
        windowOptions.IsVisible = visible;
        windowOptions.VSync = true;
        using IWindow window = Window.Create(windowOptions);
        window.Initialize();
        using IInputContext input = window.CreateInput();
        bool gpuRays = args.Contains("--gpu-rays", StringComparer.Ordinal);
        using var graphics = new NativeGameGraphics(window, options.Title, visible, enableRayQueries: gpuRays);
        graphics.Settings = args.Contains("--basic-graphics", StringComparer.Ordinal)
            ? Aurelian.Rendering.Contracts.Models.Graphics3DSettings.Basic : options.Graphics;
        if (options.EnvironmentAsset is not null)
        {
            graphics.Environment = new GameAssets().LoadEnvironment(options.EnvironmentAsset);
        }
        graphics.ReflectionProbe = options.ReflectionProbe;
        graphics.LocalLights = options.LocalLights;
        using VulkanSpatialRayQueries3D? rayQueries = gpuRays
            && graphics.Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
            ? GameRayQueries.Create(graphics.Plant, game.SpatialWorld) : null;
        if (rayQueries is not null)
        {
            game.UseRayQueries(rayQueries);
        }
        var menus = new GameMenuView(graphics.Font);
        using var session = new NativeSession(game, window, input, graphics, menus, manageFocus: !playtest);
        Console.WriteLine($"AURELIAN_GAME_READY id={id} gpu={graphics.Plant.Facts.PhysicalDeviceName} rays={(rayQueries is null ? "CPU" : "Vulkan")}");
        if (playtest)
        {
            RunPlaytest(new StarterPlaytestTarget(game, menus, session.CaptureOrPresent), script, output);
            Console.WriteLine($"AURELIAN_RAY_QUERY_DISPATCHES count={rayQueries?.DispatchCount ?? 0}");
            Console.WriteLine($"AURELIAN_HUMANOID_GPU_DISPATCHES count={session.CharacterDispatches}");
            return;
        }
        using var host = new AurelianGameHost(new SilkGameWindowAdapter(window), session, session, session, id);
        var clock = Stopwatch.StartNew();
        TimeSpan previous = clock.Elapsed;
        int frames = 0;
        while (!window.IsClosing && !game.Quit)
        {
            TimeSpan now = clock.Elapsed;
            if (!host.RunFrame(now - previous)) break;
            previous = now;
            if (smoke && ++frames == 4)
            {
                Console.WriteLine("AURELIAN_GAME_HOST_SMOKE_PASSED frames=4");
                break;
            }
        }
    }

    private static void RunPlaytest(StarterPlaytestTarget target, string? script, string output)
    {
        using var runner = new PlaytestRunner<StarterObservation>(target, output, StarterJsonContext.Default.StarterObservation);
        if (script is null) PlaytestConsole.Shell(runner);
        else runner.Run(PlaytestScripts.Read(script));
        runner.Print(target.Observe());
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(name + " requires a value.");
        return args[index + 1];
    }

    private sealed class NativeSession : IAurelianGameApplication, IAurelianHostCompositor, IAurelianHostInput
    {
        private readonly StarterGame game;
        private readonly IWindow window;
        private readonly NativeGameGraphics graphics;
        private readonly GameMenuView menus;
        private readonly StarterHud hud;
        private readonly CapturedGameInput captured;
        private readonly HumanoidGamePresenter? humanoid;
        private readonly Queue<LayerInputEvent> events = new();
        private bool disposed;
        private ulong sequence;
        public int CharacterDispatches => humanoid?.DispatchCount ?? 0;

        public NativeSession(StarterGame game, IWindow window, IInputContext input, NativeGameGraphics graphics,
            GameMenuView menus, bool manageFocus)
        {
            this.game = game;
            this.window = window;
            this.graphics = graphics;
            this.menus = menus;
            hud = new(graphics.Font);
            if (game.HumanoidOptions is { } options)
            {
                humanoid = new(graphics.Plant, options);
            }
            captured = new CapturedGameInput(window, input, game.Controls.Adapter, manageFocus,
                events.Enqueue, () => { events.Clear(); menus.CancelPointer(); }, game.Pause);
        }

        public void BeginFrame(AurelianHostFrame frame)
        {
            while (events.TryDequeue(out LayerInputEvent? input))
            {
                if (game.Menu is { } menu && input is LayerPointerButtonChanged { Button: LayerPointerButton.Primary } pointer)
                {
                    string? action = menus.Pointer(menu, game.SelectedIndex,
                        new PointerPoint(pointer.Position.X, pointer.Position.Y), pointer.IsPressed, 960, 600);
                    if (action is not null) game.Activate(action);
                }
            }
            captured.SetGameplayCapture(game.Playing);
        }

        public void OnFocusChanged(bool focused)
        {
            game.Controls.Adapter.OnFocusChanged(focused);
            game.Audio.SetFocused(focused);
        }

        public void OnSimulationTick(AurelianHostFrame frame) => game.Advance(frame.Elapsed);
        public void OnRender(AurelianHostFrame frame)
        {
            captured.SetGameplayCapture(game.Playing);
            Render(null);
        }
        public void Present(AurelianHostFrame frame) => graphics.Presenter.Present(++sequence);
        public void OnResize(HostSurfaceSize size) => Resize(size);
        public void Resize(HostSurfaceSize size)
        {
            if (size.Width != graphics.Target.Width || size.Height != graphics.Target.Height)
                throw new NotSupportedException("Native game starter currently requires a fixed surface extent.");
        }

        public void CaptureOrPresent(string? path)
        {
            window.DoEvents();
            if (window.IsClosing) throw new InvalidOperationException("Native playtest window closed.");
            Render(path);
            graphics.Presenter.Present(++sequence);
        }

        private void Render(string? path)
        {
            var character = game.HumanoidPlayer;
            var gpuGeometry = character is null ? null : humanoid!.Present(game.CharacterPose!, character.WorldTransform,
                game.View == CameraView.ThirdPerson);
            graphics.Render(game.BuildRenderScene(), game.Camera((float)graphics.Target.Width / graphics.Target.Height),
                game.CameraEye, gpuGeometry: gpuGeometry);
            var frame = game.Menu is { } menu
                ? graphics.Menus.Render(menus, menu, game.SelectedIndex, capture: path is not null)
                : graphics.Menus.RenderPrepared(hud.Prepare(game), capture: path is not null);
            if (path is not null) NativeGameGraphics.WritePng(path, (int)graphics.Target.Width, (int)graphics.Target.Height, frame.Pixels!);
            window.Title = $"{game.Id} | {game.View} | ammo {game.Observe().Ammo} | V switch view / ESC menu";
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            captured.Dispose();
            humanoid?.Dispose();
        }
    }
}
