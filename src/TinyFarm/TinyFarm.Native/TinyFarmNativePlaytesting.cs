using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Aurelian;
using Silk.NET.Maths;
using TinyFarm.InputMan;
using TinyFarm.Playtesting;

namespace TinyFarm.Native;

internal static class TinyFarmNativePlaytesting
{
    public static int Run(TinyFarmGame game, AurelianInputAdapter input, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host, string[] args)
    {
        string output = Option(args, "--playtest-output") ?? "artifacts/tinyfarm-playtesting/session";
        using var target = new NativeTarget(game, input, window, renderer, host);
        using var runner = new PlaytestRunner(target, output);
        try
        {
            host.RunFrame(TimeSpan.Zero);
            string? path = Option(args, "--playtest-script");
            if (path is not null) runner.Run(PlaytestScripts.Read(path));
            else PlaytestConsole.Shell(runner);
            PlaytestConsole.Print(target.Observe());
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            runner.WriteFinal();
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    public static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new FormatException("Missing value for " + name);
        return args[index + 1];
    }

    private sealed class NativeTarget(TinyFarmGame game, AurelianInputAdapter input,
        TinyFarmNativeWindow window, TinyFarmNativeRenderer renderer, AurelianGameHost host)
        : PlaytestTarget(game, input, window.NativeWindow.Size.X, window.NativeWindow.Size.Y)
    {
        public override string Backend => "native-vulkan-inputman-machina";
        public override void Queue(LayerInputEvent value) => window.MenuInput.Enqueue(value);

        public override void ReleaseMouse()
        {
            base.ReleaseMouse();
            renderer.ResetMenuPointer();
        }

        public override void Frame(TimeSpan elapsed)
        {
            if (!host.RunFrame(elapsed)) throw new InvalidOperationException("Native window closed during playtest.");
        }

        public override void Focus(bool focused)
        {
            base.Focus(focused);
            window.SetPlaytestFocus(focused);
        }

        public override void Resize(int width, int height)
        {
            window.NativeWindow.Size = new Vector2D<int>(width, height);
            window.PumpEvents();
            base.Resize(window.NativeWindow.Size.X, window.NativeWindow.Size.Y);
            if (Width != width || Height != height)
            {
                throw new InvalidOperationException($"Requested {width}x{height}, received {Width}x{Height} client pixels.");
            }
        }

        public override (double X, double Y) ActionPoint(string action)
        {
            LayerPoint point = renderer.MenuActionCenter(action);
            return (point.X / Width, point.Y / Height);
        }

        public override void Capture(string path)
        {
            renderer.CaptureNextFrame();
            Frame(TimeSpan.Zero);
            PngWriter.Write(path, renderer.Layout.Width, renderer.Layout.Height,
                renderer.Last?.NativeFrame.Pixels ?? throw new InvalidOperationException("Native capture returned no pixels."));
        }

        // AurelianGameHost owns and disposes its input adapter.
        public override void Dispose() { }
    }
}
