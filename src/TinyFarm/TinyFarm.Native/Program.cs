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

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = FindRoot();
        bool proof = args.Contains("--proof", StringComparer.Ordinal);
        string saveRoot = proof ? Path.Combine(root, "artifacts", "validation", "m9-saves")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyFarm", "saves");
        try
        {
            var game = new TinyFarmSupperGame(new FileSaveStore(saveRoot));
            var input = new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile()));
            input.SetContexts(game.Contexts);
            var window = SupperWindow.Create(input, proof);
            using var resources = SupperAudio.CreateResources();
            IAudioOutputBackend backend;
            string audioBackend;
            try
            {
                backend = new NAudioOutputBackend();
                audioBackend = "Windows NAudio";
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                backend = new NullAudioOutputBackend();
                audioBackend = "Silent fallback: " + error.Message;
            }
            var audio = new AurelianAudioRuntime(resources, backend, voiceCapacity: 16);
            audio.SetBusVolume(AudioBusId.Master, .35f);
            audio.Play(new TinyFarmAudioProjector().FarmMusic(new AudioEventId("supper:music")) with { Priority = 100 });
            var renderer = new SupperRenderer(root, game, window, proof);
            var application = new SupperApplication(game, input, window, audio);
            using var host = new AurelianGameHost(window, input, renderer, application, "TinyFarm", audio);
            if (proof)
            {
                SupperProof.Run(root, game, input, renderer, host, audio, audioBackend);
                return 0;
            }
            if (args.Contains("--window-smoke", StringComparer.Ordinal))
            {
                SupperProof.RunWindow(game, window, host);
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
}

internal sealed class SupperApplication(TinyFarmSupperGame game, AurelianInputAdapter input,
    SupperWindow window, AurelianAudioRuntime audio) : IAurelianGameApplication
{
    public void OnResize(HostSurfaceSize size) { }
    public void OnSimulationTick(AurelianHostFrame frame)
    {
        string? dialogueBefore = game.Dialogue.Presentation?.OperationId;
        SupperScreen screenBefore = game.Screen;
        int feedbackEpoch = game.FeedbackEpoch;
        game.Handle(input.CurrentFrame);
        if (dialogueBefore != game.Dialogue.Presentation?.OperationId || screenBefore != game.Screen)
        {
            audio.Play(new AudioCue(new AudioEventId($"ui:{frame.Sequence}"), SupperAudio.Confirm, AudioBusId.UI, Volume: .3f));
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

internal sealed class SupperWindow : IAurelianGameWindow
{
    private readonly IWindow window;
    private readonly AurelianInputAdapter input;
    private readonly bool proof;
    private readonly IInputContext inputContext;
    private readonly SilkInputBridge inputBridge;
    private bool disposed;
    private bool focused;

    private SupperWindow(
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
        window.Resize += OnResize;
        window.FocusChanged += OnFocusChanged;
    }

    public static SupperWindow Create(AurelianInputAdapter input, bool proof)
    {
        WindowOptions options = WindowOptions.DefaultVulkan;
        options.IsVisible = !proof;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "TinyFarm - A Little Mint of Kindness";
        options.VSync = true;
        options.WindowBorder = WindowBorder.Fixed;
        IWindow window = Silk.NET.Windowing.Window.Create(options);
        window.Initialize();
        IReadOnlyList<string> requiredExtensions = ReadRequiredVulkanExtensions(window);
        IInputContext inputContext = window.CreateInput();
        var inputBridge = new SilkInputBridge(inputContext, input);
        return new SupperWindow(window, inputContext, inputBridge, input, proof, requiredExtensions);
    }

    public IWindow NativeWindow => window;
    public IReadOnlyList<string> RequiredVulkanInstanceExtensions { get; }
    public HostSurfaceSize SurfaceSize => new(window.Size.X, window.Size.Y);
    public bool IsFocused => proof || focused;
    public bool ShouldClose => window.IsClosing;
    public event Action<HostSurfaceSize>? Resized;
    public event Action<bool>? FocusChanged;
    public void PumpEvents() => window.DoEvents();

    internal void InjectKey(KeyboardKey key, bool down)
    {
        input.RecordButton(global::InputMan.Core.Controls.Key(key), down);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        window.Resize -= OnResize;
        window.FocusChanged -= OnFocusChanged;
        inputBridge.Dispose();
        inputContext.Dispose();
        window.Dispose();
    }

    private void OnResize(Vector2D<int> size) => Resized?.Invoke(new HostSurfaceSize(size.X, size.Y));

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

internal static class SupperAudio
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

