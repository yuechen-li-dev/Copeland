using System.Diagnostics;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Aurelian.GameHost;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.InputMan;
using TinyFarm.Runtime;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace TinyFarm.Native;

// Historical note: the first native TinyFarm proof was codenamed "Supper".
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = FindRoot();
        int soakFrames = ParseSoakFrames(args);
        bool m25Proof = args.Contains("--m25-proof", StringComparer.Ordinal);
        bool baseline = args.Contains("--m25-baseline", StringComparer.Ordinal);
        bool m24Proof = args.Contains("--m24-proof", StringComparer.Ordinal);
        bool proof = args.Contains("--proof", StringComparer.Ordinal) || m24Proof || m25Proof || soakFrames > 0;
        string saveRoot = proof ? Path.Combine(root, "artifacts", "validation", "m9-saves")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyFarm", "saves");
        try
        {
            var game = new TinyFarmGame(new FileSaveStore(saveRoot));
            var input = new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile()));
            input.SetContexts(game.Contexts);
            int width = ParseDimension(args, "--width", baseline || proof && !m25Proof ? 1280 : 1920);
            int height = ParseDimension(args, "--height", baseline || proof && !m25Proof ? 720 : 1080);
            game.Presentation.HudVisible = !args.Contains("--world-only", StringComparer.Ordinal);
            if (!game.Presentation.HudVisible)
            {
                game.Start();
            }
            var window = TinyFarmNativeWindow.Create(input, proof, vSync: soakFrames == 0 && !m25Proof, width, height);
            using var resources = TinyFarmNativeAudio.CreateResources();
            IAudioOutputBackend backend;
            string audioBackend;
            try
            {
                if (soakFrames > 0)
                {
                    backend = new NullAudioOutputBackend();
                    audioBackend = "Null soak backend";
                }
                else
                {
                    backend = new NAudioOutputBackend();
                    audioBackend = "Windows NAudio";
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                backend = new NullAudioOutputBackend();
                audioBackend = "Silent fallback: " + error.Message;
            }
            var audio = new AurelianAudioRuntime(resources, backend, voiceCapacity: 16);
            audio.SetBusVolume(AudioBusId.Master, .35f);
            audio.Play(new TinyFarmAudioProjector().FarmMusic(new AudioEventId("tinyfarm:music")) with { Priority = 100 });
            var renderer = new TinyFarmNativeRenderer(root, game, window, proof, vSync: soakFrames == 0 && !m25Proof, legacy: baseline || proof && !m25Proof);
            var application = new TinyFarmNativeApplication(game, input, window, audio);
            using var host = new AurelianGameHost(window, input, renderer, application, "TinyFarm", audio);
            if (soakFrames > 0)
            {
                TinyFarmNativeSoak.Run(root, soakFrames, game, renderer, host);
                return 0;
            }
            if (m25Proof)
            {
                TinyFarmM25NativeProof.Run(root, game, input, window, renderer, host, baseline);
                return 0;
            }
            if (m24Proof)
            {
                TinyFarmM24NativeProof.Run(root, game, renderer, host);
                return 0;
            }
            if (proof)
            {
                TinyFarmNativeProof.Run(root, game, input, renderer, host, audio, audioBackend);
                return 0;
            }
            if (args.Contains("--window-smoke", StringComparer.Ordinal))
            {
                TinyFarmNativeProof.RunWindow(game, window, host);
                game.Start();
                string evidence = Path.Combine(root, "artifacts", "tinyfarm-high-fidelity-presentation-m25");
                Directory.CreateDirectory(evidence);
                TinyFarmM25NativeProof.Capture(Path.Combine(evidence, "native-default-window-smoke.png"), renderer, host);
                TinyFarmM25NativeProof.Write(evidence, "native-window-smoke.json", new
                {
                    window.NativeWindow.IsVisible,
                    window.SurfaceSize,
                    renderer.Layout,
                    game.Screen,
                    titleEnterMovementPausePassed = true,
                    renderedPixels = renderer.Last!.NativeFrame.Pixels!.Length,
                });
                return 0;
            }
            var clock = Stopwatch.StartNew();
            TimeSpan previous = clock.Elapsed;
            while (!game.ShouldQuit && !window.ShouldClose)
            {
                TimeSpan now = clock.Elapsed;
                TimeSpan elapsed = now - previous;
                previous = now;
                if (!host.RunFrame(elapsed > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : elapsed))
                {
                    break;
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            string path = Path.Combine(root, "artifacts", "m9-error.txt");
            File.WriteAllText(path, error.ToString());
            if (!proof)
            {
                Console.Error.WriteLine("TinyFarm could not start. " + error.Message + " Details: " + path);
            }
            return 1;
        }
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TinyFarm.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Run TinyFarm from its repository build.");
    }

    private static int ParseDimension(string[] args, string flag, int fallback)
    {
        int index = Array.IndexOf(args, flag);
        if (index < 0)
        {
            return fallback;
        }
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value) || value < 320 || value > 7680)
        {
            throw new ArgumentException(flag + " requires an integer from 320 through 7680.");
        }
        return value;
    }

    private static int ParseSoakFrames(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--soak-frames", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length
                || !int.TryParse(args[index + 1], out int frames)
                || frames <= 0)
            {
                throw new ArgumentException("--soak-frames requires a positive integer frame count.");
            }

            return frames;
        }

        return 0;
    }
}

internal sealed class TinyFarmNativeApplication(TinyFarmGame game, AurelianInputAdapter input,
    TinyFarmNativeWindow window, AurelianAudioRuntime audio) : IAurelianGameApplication
{
    public void OnResize(HostSurfaceSize size) { }
    public void OnSimulationTick(AurelianHostFrame frame)
    {
        string? dialogueBefore = game.Dialogue.Presentation?.OperationId;
        TinyFarmScreen screenBefore = game.Screen;
        int feedbackEpoch = game.FeedbackEpoch;
        game.Handle(input.CurrentFrame);
        if (dialogueBefore != game.Dialogue.Presentation?.OperationId || screenBefore != game.Screen)
        {
            audio.Play(new AudioCue(new AudioEventId($"ui:{frame.Sequence}"), TinyFarmNativeAudio.Confirm, AudioBusId.UI, Volume: .3f));
        }
        if (feedbackEpoch != game.FeedbackEpoch)
        {
            input.OnFocusChanged(false);
            input.OnFocusChanged(window.IsFocused);
            audio.StopBus(AudioBusId.Sfx, TimeSpan.Zero);
        }
        else
        {
            game.Advance(frame.Elapsed, input.CurrentFrame, window.IsFocused);
        }
        input.SetContexts(game.Contexts);
        while (game.PendingAudio.TryDequeue(out AudioCue? cue))
        {
            audio.Play(cue);
        }
        // Backend notifications are transient and must not accumulate during a long afternoon.
        audio.DrainCompletions();
        audio.DrainDiagnostics();
    }
    public void OnRender(AurelianHostFrame frame) { }
    public void Dispose() { }
}

internal sealed class TinyFarmNativeWindow : IAurelianGameWindow
{
    private readonly IWindow window;
    private readonly AurelianInputAdapter input;
    private readonly bool proof;
    private readonly IInputContext inputContext;
    private readonly SilkInputBridge inputBridge;
    private bool disposed;
    private bool focused;

    private TinyFarmNativeWindow(
        IWindow window,
        IInputContext inputContext,
        SilkInputBridge inputBridge,
        AurelianInputAdapter input,
        bool proof,
        IReadOnlyList<string> requiredVulkanInstanceExtensions)
    {
        this.window = window;
        this.inputContext = inputContext;
        this.inputBridge = inputBridge;
        this.input = input;
        this.proof = proof;
        focused = proof;
        RequiredVulkanInstanceExtensions = requiredVulkanInstanceExtensions;
        window.FramebufferResize += OnResize;
        window.FocusChanged += OnFocusChanged;
    }

    public static TinyFarmNativeWindow Create(AurelianInputAdapter input, bool proof, bool vSync = true, int width = 1920, int height = 1080)
    {
        WindowOptions options = WindowOptions.DefaultVulkan;
        options.IsVisible = !proof;
        options.Size = new Vector2D<int>(width, height);
        options.Title = "TinyFarm - A Little Mint of Kindness";
        options.VSync = vSync;
        options.WindowBorder = proof ? WindowBorder.Hidden : WindowBorder.Resizable;
        IWindow window = Silk.NET.Windowing.Window.Create(options);
        window.Initialize();
        if (window.Size.X != width || window.Size.Y != height)
        {
            // Desktop decorations can reduce oversized client requests. Borderless preserves the requested pixels.
            window.WindowBorder = WindowBorder.Hidden;
            window.Size = new Vector2D<int>(width, height);
            window.DoEvents();
        }
        IReadOnlyList<string> requiredExtensions = ReadRequiredVulkanExtensions(window);
        IInputContext inputContext = window.CreateInput();
        var inputBridge = new SilkInputBridge(inputContext, input);
        return new TinyFarmNativeWindow(window, inputContext, inputBridge, input, proof, requiredExtensions);
    }

    public IWindow NativeWindow => window;
    public IReadOnlyList<string> RequiredVulkanInstanceExtensions { get; }
    public HostSurfaceSize SurfaceSize => new(window.FramebufferSize.X, window.FramebufferSize.Y);
    public bool IsFocused => proof || focused;
    public bool ShouldClose => window.IsClosing;
    public event Action<HostSurfaceSize>? Resized;
    public event Action<bool>? FocusChanged;
    public void PumpEvents() => window.DoEvents();

    internal void InjectKey(KeyboardKey key, bool down)
    {
        input.RecordButton(global::InputMan.Core.Controls.Key(key), down);
    }

    internal void InjectFocus(bool isFocused)
    {
        OnFocusChanged(isFocused);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        window.FramebufferResize -= OnResize;
        window.FocusChanged -= OnFocusChanged;
        inputBridge.Dispose();
        inputContext.Dispose();
        window.Dispose();
    }

    private void OnResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            Resized?.Invoke(new HostSurfaceSize(size.X, size.Y));
        }
    }

    private void OnFocusChanged(bool isFocused)
    {
        focused = isFocused;
        FocusChanged?.Invoke(isFocused);
    }

    private static unsafe IReadOnlyList<string> ReadRequiredVulkanExtensions(IWindow window)
    {
        IVkSurface surface = window.VkSurface
            ?? throw new InvalidOperationException("Silk.NET did not expose a Vulkan surface source.");
        uint count = 0;
        byte** extensions = surface.GetRequiredExtensions(out count);
        var names = new List<string>((int)count);
        for (int index = 0; index < count; index++)
        {
            string? name = SilkMarshal.PtrToString((nint)extensions[index], NativeStringEncoding.UTF8);
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }
        return names;
    }
}

internal static class TinyFarmNativeAudio
{
    public static readonly AudioAssetId Confirm = new("tinyfarm.ui.confirm");
    public static AudioResourceScope CreateResources()
    {
        var resources = new AudioResourceScope();
        Add(resources, Confirm, 880, .08);
        Add(resources, TinyFarmAudioAssets.Pickup, 660, .22);
        Add(resources, TinyFarmAudioAssets.Harvest, 440, .24);
        Add(resources, TinyFarmAudioAssets.SwordSwing, 140, .19);
        Add(resources, TinyFarmAudioAssets.Footstep, 90, .05);
        Add(resources, TinyFarmAudioAssets.FarmMusic, 220, 8, music: true);
        return resources;
    }

    private static void Add(AudioResourceScope resources, AudioAssetId id, double frequency, double seconds, bool music = false)
    {
        const int rate = 48000;
        float[] samples = new float[(int)(rate * seconds)];
        double[] melody = [1, 1.25, 1.5, 2, 1.5, 1.25, 1.125, 1];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / rate;
            double local = music ? t % 1 : t;
            double envelope = Math.Min(local * 40, 1) * Math.Exp(-local * (music ? 5 : 18));
            double note = frequency * (music ? melody[(int)t % melody.Length] : 1 + t);
            samples[i] = (float)(Math.Sin(t * note * Math.Tau) * envelope * (music ? .12 : .35));
        }
        resources.Add(new AudioClipResource(id, "authored-supper-pcm-v1", rate, 1, samples.Length, samples));
    }
}

