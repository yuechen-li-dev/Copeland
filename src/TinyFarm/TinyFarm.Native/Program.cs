using System.Diagnostics;
using System.Drawing.Imaging;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Aurelian.GameHost;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.InputMan;
using TinyFarm.Runtime;

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
            ApplicationConfiguration.Initialize();
            var game = new TinyFarmSupperGame(new FileSaveStore(saveRoot));
            var input = new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile()));
            input.SetContexts(game.Contexts);
            var window = new SupperWindow(input, proof);
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
            var renderer = new SupperRenderer(root, game, window.Display);
            var application = new SupperApplication(game, input, window, audio);
            using var host = new AurelianGameHost(window, input, renderer, application, "TinyFarm", audio);
            if (proof)
            {
                SupperProof.Run(root, game, input, renderer, host, audio, audioBackend);
                return 0;
            }
            window.Show();
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
                Thread.Sleep(8);
            }
            return 0;
        }
        catch (Exception error)
        {
            string path = Path.Combine(root, "artifacts", "m9-error.txt");
            File.WriteAllText(path, error.ToString());
            if (!proof)
            {
                MessageBox.Show("TinyFarm could not start.\n" + error.Message + "\nDetails: " + path, "TinyFarm");
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

internal sealed class SupperWindow : Form, IAurelianGameWindow
{
    private readonly AurelianInputAdapter input;
    private readonly bool proof;
    private readonly Bitmap bitmap = new(1280, 720, PixelFormat.Format32bppArgb);
    private bool closed;

    public SupperWindow(AurelianInputAdapter input, bool proof)
    {
        this.input = input;
        this.proof = proof;
        Text = "TinyFarm - A Little Mint of Kindness";
        ClientSize = new Size(1280, 720);
        MinimumSize = Size;
        MaximumSize = Size;
        MaximizeBox = false;
        DoubleBuffered = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        KeyDown += (_, key) => Record(key, true);
        KeyUp += (_, key) => Record(key, false);
        Activated += (_, _) => FocusChanged?.Invoke(true);
        Deactivate += (_, _) => FocusChanged?.Invoke(false);
        FormClosed += (_, _) => closed = true;
    }

    public HostSurfaceSize SurfaceSize => new(1280, 720);
    public bool IsFocused => proof || ContainsFocus;
    public bool ShouldClose => closed;
    public event Action<HostSurfaceSize>? Resized { add { } remove { } }
    public event Action<bool>? FocusChanged;
    public void PumpEvents() => Application.DoEvents();

    internal void InjectKeyMessage(Keys key, bool down)
    {
        // Qualification enters through this window's normal key-message callback.
        Message message = Message.Create(Handle, down ? 0x0100 : 0x0101, (nint)key, 0);
        WndProc(ref message);
    }

    public unsafe void Display(byte[] rgba)
    {
        if (proof)
        {
            return;
        }
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 1280, 720), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* target = (byte*)data.Scan0;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                target[i] = rgba[i + 2];
                target[i + 1] = rgba[i + 1];
                target[i + 2] = rgba[i];
                target[i + 3] = rgba[i + 3];
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(bitmap, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            bitmap.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Record(KeyEventArgs key, bool down)
    {
        KeyboardKey? control = key.KeyCode switch
        {
            Keys.W => KeyboardKey.W,
            Keys.A => KeyboardKey.A,
            Keys.S => KeyboardKey.S,
            Keys.D => KeyboardKey.D,
            Keys.E => KeyboardKey.E,
            Keys.I => KeyboardKey.I,
            Keys.F => KeyboardKey.F,
            Keys.N => KeyboardKey.N,
            Keys.Q => KeyboardKey.Q,
            Keys.Enter => KeyboardKey.Enter,
            Keys.Space => KeyboardKey.Space,
            Keys.Escape => KeyboardKey.Escape,
            Keys.Up => KeyboardKey.ArrowUp,
            Keys.Down => KeyboardKey.ArrowDown,
            Keys.D1 => KeyboardKey.Number1,
            Keys.D2 => KeyboardKey.Number2,
            Keys.D3 => KeyboardKey.Number3,
            Keys.D4 => KeyboardKey.Number4,
            _ => null
        };
        if (control is KeyboardKey value)
        {
            input.RecordButton(global::InputMan.Core.Controls.Key(value), down);
            key.Handled = true;
            key.SuppressKeyPress = true;
        }
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

