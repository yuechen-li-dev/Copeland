using System.Diagnostics;
using System.Globalization;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Aurelian.Composition;
using Aurelian.GameHost;
using Aurelian.GameHost.Silk;
using Aurelian.GameMenus;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Playtesting;
using Machina.Runtime.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aurelian.Games;

/// <summary>Launch overrides parsed from the command line. None of them are persisted.</summary>
public sealed record GameLaunchOptions
{
    public bool Headless { get; init; }
    public string? PlaytestScript { get; init; }
    public bool PlaytestStdio { get; init; }
    public bool LaunchSmoke { get; init; }
    public bool Visible { get; init; }
    public string? Output { get; init; }
    public string? SaveRoot { get; init; }
    public bool RayQueries { get; init; }
    public GameResolution? Resolution { get; init; }
    public GameWindowMode? Mode { get; init; }
    public GameGraphicsQuality? Quality { get; init; }

    public bool Playtest => PlaytestScript is not null || PlaytestStdio;

    /// <summary>
    /// --headless, --playtest-script PATH, --playtest-stdio, --launch-smoke, --visible, --output DIR, --save-root DIR,
    /// --gpu-rays, --resolution WxH, --windowed, --borderless, --fullscreen, --quality low|medium|high, --basic-graphics.
    /// Unknown arguments are ignored so games can add their own.
    /// </summary>
    public static GameLaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        GameWindowMode? mode = null;
        int modes = 0;
        foreach ((string flag, GameWindowMode value) in new[]
        {
            ("--windowed", GameWindowMode.Windowed), ("--borderless", GameWindowMode.Borderless), ("--fullscreen", GameWindowMode.Fullscreen),
        })
        {
            if (args.Contains(flag, StringComparer.Ordinal))
            {
                mode = value;
                modes++;
            }
        }
        if (modes > 1)
        {
            throw new ArgumentException("Choose one of --windowed, --borderless and --fullscreen.");
        }
        GameResolution? resolution = null;
        if (Option(args, "--resolution") is { } text)
        {
            string[] parts = text.Split('x', 'X');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            {
                throw new ArgumentException("--resolution expects WIDTHxHEIGHT, for example 1920x1080.");
            }
            resolution = new GameResolution(width, height);
        }
        GameGraphicsQuality? quality = args.Contains("--basic-graphics", StringComparer.Ordinal) ? GameGraphicsQuality.Low : null;
        if (Option(args, "--quality") is { } level)
        {
            quality = Enum.TryParse(level, ignoreCase: true, out GameGraphicsQuality parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException("--quality expects low, medium or high.");
        }
        var options = new GameLaunchOptions
        {
            Headless = args.Contains("--headless", StringComparer.Ordinal),
            PlaytestScript = Option(args, "--playtest-script"),
            PlaytestStdio = args.Contains("--playtest-stdio", StringComparer.Ordinal),
            LaunchSmoke = args.Contains("--launch-smoke", StringComparer.Ordinal),
            Visible = args.Contains("--visible", StringComparer.Ordinal),
            Output = Option(args, "--output"),
            SaveRoot = Option(args, "--save-root"),
            RayQueries = args.Contains("--gpu-rays", StringComparer.Ordinal),
            Resolution = resolution,
            Mode = mode,
            Quality = quality,
        };
        if (options.Headless && !options.Playtest)
        {
            throw new ArgumentException("Headless execution requires --playtest-script or --playtest-stdio.");
        }
        if (options.PlaytestScript is not null && options.PlaytestStdio)
        {
            throw new ArgumentException("Select one playtest input source.");
        }
        return options;
    }

    public GameDisplaySettings Override(GameDisplaySettings display)
    {
        GameDisplaySettings result = display;
        if (Resolution is { } resolution)
        {
            result = result with { Width = resolution.Width, Height = resolution.Height };
        }
        if (Mode is { } mode)
        {
            result = result with { Mode = mode };
        }
        if (Quality is { } quality)
        {
            result = result with { Quality = quality };
        }
        return result;
    }

    internal static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(name + " requires a value.");
        }
        return args[index + 1];
    }
}

/// <summary>
/// One call runs a game natively, as a scripted native playtest, or as a deterministic headless playtest:
/// <c>GameApp.Run(new MyGame(), args);</c>
/// </summary>
public static class GameApp
{
    public static void Run(IGame game, string[] args, Action<GameSession>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        GameLaunchOptions launch = GameLaunchOptions.Parse(args);
        game.Manifest.Validate();
        using var session = new GameSession(game, launch.SaveRoot, CreateAudioBackend(launch));
        configure?.Invoke(session);
        Run(session, launch);
    }

    /// <summary>Run an already-constructed session (for games that build their session themselves).</summary>
    public static void Run(GameSession session, GameLaunchOptions launch)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(launch);
        string output = Path.GetFullPath(launch.Output ?? Path.Combine("artifacts", session.Manifest.Id));
        if (launch.Headless)
        {
            RunPlaytest(new GamePlaytestTarget(session), launch.PlaytestScript, output);
            return;
        }
        RunNative(session, launch, output);
    }

    /// <summary>The platform audio device for interactive runs; deterministic null output for playtests and smoke runs.</summary>
    public static IAudioOutputBackend CreateAudioBackend(GameLaunchOptions launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.Headless || launch.Playtest || launch.LaunchSmoke)
        {
            return new NullAudioOutputBackend();
        }
        if (NAudioOutputBackend.TryCreate(out NAudioOutputBackend? nativeAudio, out string? error))
        {
            return nativeAudio!;
        }
        Console.Error.WriteLine("Audio device unavailable; using null output: " + error);
        return new NullAudioOutputBackend();
    }

    private static void RunNative(GameSession session, GameLaunchOptions launch, string output)
    {
        GameManifest manifest = session.Manifest;
        bool visible = (!launch.Playtest && !launch.LaunchSmoke) || launch.Visible;
        GameDisplaySettings initial = launch.Override(session.Settings.Display);
        WindowOptions windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.Size = new Vector2D<int>(initial.Width, initial.Height);
        windowOptions.Title = manifest.Title;
        windowOptions.WindowBorder = WindowBorder.Resizable;
        windowOptions.IsVisible = visible;
        windowOptions.VSync = false;
        using IWindow window = Window.Create(windowOptions);
        window.Initialize();
        using IInputContext input = window.CreateInput();
        using var graphics = new NativeGameGraphics(window, manifest.Title, visible, session.Context.Assets,
            enableRayQueries: launch.RayQueries, vSync: initial.VSync);
        var menus = new GameMenuView(graphics.Font);
        using var native = new NativeGameSession(session, window, input, graphics, menus, manageFocus: !launch.Playtest);
        var display = new WindowDisplay(window, graphics, manifest, native);
        session.Game.AttachGraphics(graphics);
        try
        {
            // Launch overrides apply to this run only; the menu still edits (and persists) the player's settings.
            display.Apply(initial);
            session.AttachDisplay(display, applyNow: false);
            native.SyncSurface();
            Console.WriteLine($"AURELIAN_GAME_READY id={manifest.Id} gpu={graphics.Plant.Facts.PhysicalDeviceName} " +
                $"extent={graphics.Width}x{graphics.Height} mode={initial.Mode}");
            if (launch.Playtest)
            {
                RunPlaytest(new GamePlaytestTarget(session, menus, native), launch.PlaytestScript, output);
                Console.WriteLine($"AURELIAN_SURFACE_REBUILDS count={graphics.SurfaceRebuilds}");
                return;
            }
            using var host = new AurelianGameHost(new SilkGameWindowAdapter(window), native, native, native, manifest.Id);
            var clock = Stopwatch.StartNew();
            TimeSpan previous = clock.Elapsed;
            int frames = 0;
            while (!window.IsClosing && !session.Quit)
            {
                TimeSpan now = clock.Elapsed;
                if (!host.RunFrame(now - previous))
                {
                    break;
                }
                previous = now;
                if (launch.LaunchSmoke && ++frames == 4)
                {
                    Console.WriteLine("AURELIAN_GAME_HOST_SMOKE_PASSED frames=4");
                    break;
                }
            }
        }
        finally
        {
            session.Game.DetachGraphics();
        }
    }

    private static void RunPlaytest(GamePlaytestTarget target, string? script, string output)
    {
        using var runner = new PlaytestRunner<SortedDictionary<string, string>>(target, output,
            GameShellJsonContext.Default.SortedDictionaryStringString);
        if (script is null)
        {
            PlaytestConsole.Shell(runner);
        }
        else
        {
            runner.Run(PlaytestScripts.Read(script));
        }
        runner.Print(target.Observe());
    }

    /// <summary>Window mode, resolution, vsync and quality for a Silk window and its Vulkan graphics.</summary>
    private sealed class WindowDisplay(IWindow window, NativeGameGraphics graphics, GameManifest manifest, NativeGameSession native)
        : IGameDisplay
    {
        public IReadOnlyList<GameResolution> Resolutions
        {
            get
            {
                Vector2D<int> desktop = window.Monitor?.Bounds.Size ?? new Vector2D<int>(1920, 1080);
                var result = GameResolution.Common.Where(item => item.Width <= desktop.X && item.Height <= desktop.Y).ToList();
                var native = new GameResolution(desktop.X, desktop.Y);
                if (!result.Contains(native) && native.Width >= 640 && native.Height >= 480)
                {
                    result.Add(native);
                }
                if (result.Count == 0)
                {
                    result.Add(new GameResolution(960, 600));
                }
                return result.OrderBy(item => (long)item.Width * item.Height).ThenBy(item => item.Width).ToArray();
            }
        }

        public void Apply(GameDisplaySettings settings)
        {
            settings.Validate();
            IMonitor? monitor = window.Monitor;
            Rectangle<int> bounds = monitor?.Bounds ?? new Rectangle<int>(0, 0, 1920, 1080);
            switch (settings.Mode)
            {
                case GameWindowMode.Fullscreen:
                    window.WindowBorder = WindowBorder.Resizable;
                    window.Size = new Vector2D<int>(settings.Width, settings.Height);
                    window.WindowState = WindowState.Fullscreen;
                    break;
                case GameWindowMode.Borderless:
                    window.WindowState = WindowState.Normal;
                    window.WindowBorder = WindowBorder.Hidden;
                    window.Position = bounds.Origin;
                    window.Size = bounds.Size;
                    break;
                default:
                    window.WindowState = WindowState.Normal;
                    window.WindowBorder = WindowBorder.Resizable;
                    window.Size = new Vector2D<int>(Math.Min(settings.Width, bounds.Size.X), Math.Min(settings.Height, bounds.Size.Y));
                    window.Position = new Vector2D<int>(
                        bounds.Origin.X + Math.Max(0, (bounds.Size.X - window.Size.X) / 2),
                        bounds.Origin.Y + Math.Max(0, (bounds.Size.Y - window.Size.Y) / 2));
                    break;
            }
            window.DoEvents();
            graphics.Settings = settings.ApplyQuality(manifest.Graphics);
            native.RequestVSync(settings.VSync);
            native.SyncSurface();
        }
    }

    /// <summary>Connects the session to the native host, Vulkan graphics and pointer input.</summary>
    private sealed class NativeGameSession : IAurelianGameApplication, IAurelianHostCompositor, IAurelianHostInput, IGamePlaytestSurface
    {
        private const double ReferenceWidth = 960;
        private const double ReferenceHeight = 600;
        private readonly GameSession session;
        private readonly IWindow window;
        private readonly NativeGameGraphics graphics;
        private readonly GameMenuView menus;
        private readonly GameHudPresenter hud;
        private readonly CapturedGameInput captured;
        private readonly Queue<LayerInputEvent> events = new();
        private bool vSync;
        private bool disposed;
        private ulong sequence;

        public NativeGameSession(GameSession session, IWindow window, IInputContext input, NativeGameGraphics graphics,
            GameMenuView menus, bool manageFocus)
        {
            this.session = session;
            this.window = window;
            this.graphics = graphics;
            this.menus = menus;
            vSync = graphics.VSync;
            hud = new GameHudPresenter(graphics.Font);
            captured = new CapturedGameInput(window, input, session.Input.Adapter, manageFocus, events.Enqueue, () =>
            {
                events.Clear();
                menus.CancelPointer();
            }, session.Pause);
            UpdateScale();
        }

        public int LogicalWidth => graphics.Menus.LogicalWidth;
        public int LogicalHeight => graphics.Menus.LogicalHeight;

        public void RequestVSync(bool value) => vSync = value;

        /// <summary>Rebuild surface resources if the framebuffer or present mode no longer matches.</summary>
        public bool SyncSurface()
        {
            Vector2D<int> size = window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0)
            {
                return false;
            }
            if (graphics.Reconfigure(size.X, size.Y, vSync))
            {
                menus.CancelPointer();
            }
            UpdateScale();
            return true;
        }

        public void BeginFrame(AurelianHostFrame frame)
        {
            while (events.TryDequeue(out LayerInputEvent? input))
            {
                if (session.Menu is { } menu && input is LayerPointerButtonChanged { Button: LayerPointerButton.Primary } pointer)
                {
                    (double x, double y) = ToLogical(pointer.Position.X, pointer.Position.Y);
                    string? action = menus.Pointer(menu, session.SelectedIndex, new PointerPoint(x, y), pointer.IsPressed,
                        LogicalWidth, LogicalHeight);
                    if (action is not null)
                    {
                        session.Activate(action);
                    }
                }
            }
            UpdateCapture();
        }

        public void OnFocusChanged(bool focused) => session.OnFocusChanged(focused);

        public void OnSimulationTick(AurelianHostFrame frame) => session.Advance(frame.Elapsed);

        public void OnRender(AurelianHostFrame frame)
        {
            UpdateCapture();
            if (SyncSurface())
            {
                Render(null);
            }
        }

        public void Present(AurelianHostFrame frame)
        {
            if (window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0)
            {
                return;
            }
            graphics.Present(++sequence);
        }

        public void OnResize(HostSurfaceSize size) => Resize(size);

        public void Resize(HostSurfaceSize size)
        {
            SyncSurface();
        }

        void IGamePlaytestSurface.Resize(int width, int height)
        {
            window.Size = new Vector2D<int>(width, height);
            window.DoEvents();
            SyncSurface();
        }

        public void Present(string? capturePath)
        {
            window.DoEvents();
            if (window.IsClosing)
            {
                throw new InvalidOperationException("Native playtest window closed.");
            }
            UpdateCapture();
            if (!SyncSurface())
            {
                return;
            }
            Render(capturePath);
            graphics.Present(++sequence);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            captured.Dispose();
        }

        private void UpdateCapture()
        {
            captured.SetGameplayCapture(session.Playing && session.Manifest.Input.Look == GameLookMode.CapturedMouse);
        }

        private void UpdateScale()
        {
            graphics.Menus.Scale = Math.Min(graphics.Width / ReferenceWidth, graphics.Height / ReferenceHeight);
        }

        private (double X, double Y) ToLogical(double windowX, double windowY)
        {
            // Pointer events arrive in window coordinates; HiDPI framebuffers can be larger.
            double framebufferScaleX = window.Size.X > 0 ? (double)window.FramebufferSize.X / window.Size.X : 1;
            double framebufferScaleY = window.Size.Y > 0 ? (double)window.FramebufferSize.Y / window.Size.Y : 1;
            double scale = graphics.Menus.Scale;
            return (windowX * framebufferScaleX / scale, windowY * framebufferScaleY / scale);
        }

        private void Render(string? capturePath)
        {
            bool capture = capturePath is not null;
            GameView? view = session.Game.View(new GameViewRequest(graphics.Width, graphics.Height));
            NativeFrameClearColor? clear = null;
            if (view is not null)
            {
                NativeGpuGeometry3D? gpu = session.Game.PrepareGpuGeometry(graphics);
                graphics.Render(view.Scene, view.Camera, view.Eye, gpuGeometry: gpu);
            }
            else
            {
                // No 3D view: the first pass that draws clears the target itself.
                clear = graphics.Clear;
            }
            if (graphics.Layers.Count > 0)
            {
                graphics.Layers.Present(sequence + 1, clear);
                clear = null;
            }
            var overlay = session.Menu is { } menu
                ? graphics.Menus.Render(menus, menu, session.SelectedIndex, capture, clear)
                : RenderHud(capture, clear);
            if (capturePath is not null)
            {
                NativeGameGraphics.WritePng(capturePath, graphics.Width, graphics.Height, overlay.Pixels!);
            }
        }

        private VulkanNativeFrameResult RenderHud(bool capture, NativeFrameClearColor? clear)
        {
            var description = new GameHud(LogicalWidth, LogicalHeight);
            session.Game.BuildHud(description);
            return graphics.Menus.RenderPrepared(hud.Prepare(description), capture, clear);
        }
    }
}
