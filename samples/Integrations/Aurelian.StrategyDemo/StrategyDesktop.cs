using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using InputMan.Core;
using Controls = InputMan.Core.Controls;

namespace Aurelian.StrategyDemo;

internal sealed class StrategyApplication : Application
{
    public static bool Smoke { get; set; }
    public static int ExitCode { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new StrategyWindow(Smoke);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class StrategyWindow : Window
{
    private readonly StrategySession session = new();
    private readonly StrategyView view = new();
    private readonly StrategyHost host;
    private readonly StrategyInput input;
    private readonly Image image = new() { Stretch = Stretch.Uniform, Focusable = true };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private StrategyRenderer? renderer;
    private WriteableBitmap? surface;
    private TimeSpan last;
    private int frames;
    private readonly byte[] rowBuffer = new byte[1280 * 4];
    private readonly bool smoke;

    public StrategyWindow(bool smoke)
    {
        this.smoke = smoke;
        host = new(session);
        input = new(session, view);
        view.Center(StrategySession.Home);
        Width = 1280;
        Height = 840;
        MinWidth = 960;
        MinHeight = 640;
        Title = "Mossward — starting";
        Background = Brushes.Black;
        Content = new TextBlock { Text = "Preparing Mossward…", Foreground = Brushes.Beige, FontSize = 26, Margin = new Thickness(40) };
        Opened += (_, _) => Start();
        KeyDown += (_, e) => RecordKey(e, true);
        KeyUp += (_, e) => RecordKey(e, false);
        Activated += (_, _) => input.Focus(true);
        Deactivated += (_, _) => input.Focus(false);
        image.PointerMoved += (_, e) => Point(e);
        image.PointerPressed += (_, e) =>
        {
            Point(e);
            var properties = e.GetCurrentPoint(image).Properties;
            input.RecordButton(Controls.Mouse(InputMan.Core.MouseButton.Primary), properties.IsLeftButtonPressed);
            input.RecordButton(Controls.Mouse(InputMan.Core.MouseButton.Secondary), properties.IsRightButtonPressed);
            input.Update(TimeSpan.Zero, clock.Elapsed);
            e.Pointer.Capture(image);
        };
        image.PointerReleased += (_, e) =>
        {
            Point(e);
            input.RecordButton(Controls.Mouse(InputMan.Core.MouseButton.Primary), false);
            input.RecordButton(Controls.Mouse(InputMan.Core.MouseButton.Secondary), false);
            input.Update(TimeSpan.Zero, clock.Elapsed);
            e.Pointer.Capture(null);
        };
        image.PointerWheelChanged += (_, e) =>
        {
            Point(e);
            input.RecordWheel((float)e.Delta.Y);
        };
        timer.Tick += (_, _) => TickFrame();
        Closed += (_, _) =>
        {
            timer.Stop();
            input.Dispose();
            renderer?.Dispose();
            surface?.Dispose();
        };
    }

    private void Start()
    {
        try
        {
            renderer = new(Path.Combine(AppContext.BaseDirectory, "Assets"));
            surface = new(new PixelSize(1280, 800), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            image.Source = surface;
            Content = image;
            image.Focus();
            last = clock.Elapsed;
            timer.Start();
            TickFrame();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void TickFrame()
    {
        try
        {
            TimeSpan now = clock.Elapsed;
            TimeSpan delta = now - last;
            last = now;
            input.Update(delta, now);
            host.Advance(delta, view.Paused);
            using var bitmap = renderer!.Render(session, view);
            using (var frame = surface!.Lock())
            {
                for (int y = 0; y < 800; y++)
                {
                    Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, rowBuffer, 0, rowBuffer.Length);
                    Marshal.Copy(rowBuffer, 0, frame.Address + y * frame.RowBytes, rowBuffer.Length);
                }
            }
            image.InvalidateVisual();
            Title = "Mossward — ready";
            frames++;
            if (smoke && frames == 3)
            {
                ProofArtifacts.Write("native-launch.json", new { windowOpened = IsVisible, framesRendered = frames, title = Title,
                    renderer = "Aurelian CPU panels + Machina direct outline + Copeland vector contours / Avalonia desktop", width = 1280, height = 800 });
                ProofArtifacts.Save(bitmap, "native-launch-frame.png");
                Console.WriteLine("MOSSWARD_NATIVE_READY frames=3");
                Close();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void RecordKey(KeyEventArgs args, bool down)
    {
        KeyboardKey key = args.Key switch
        {
            Key.Left => KeyboardKey.ArrowLeft,
            Key.Right => KeyboardKey.ArrowRight,
            Key.Up => KeyboardKey.ArrowUp,
            Key.Down => KeyboardKey.ArrowDown,
            Key.LeftShift or Key.RightShift => KeyboardKey.LeftShift,
            Key.LeftCtrl or Key.RightCtrl => KeyboardKey.LeftControl,
            Key.D1 => KeyboardKey.Number1,
            Key.D2 => KeyboardKey.Number2,
            Key.D3 => KeyboardKey.Number3,
            _ => Enum.TryParse(args.Key.ToString(), out KeyboardKey parsed) ? parsed : KeyboardKey.Unknown,
        };
        if (key != KeyboardKey.Unknown)
        {
            input.RecordButton(Controls.Key(key), down);
            args.Handled = true;
        }
    }

    private void Point(PointerEventArgs args)
    {
        Point point = args.GetPosition(image);
        double scale = Math.Min(image.Bounds.Width / 1280, image.Bounds.Height / 800);
        if (scale > 0)
        {
            view.ScreenPointer = ((float)((point.X - (image.Bounds.Width - 1280 * scale) / 2) / scale),
                (float)((point.Y - (image.Bounds.Height - 800 * scale) / 2) / scale));
        }
    }

    private void Fail(Exception exception)
    {
        timer.Stop();
        StrategyApplication.ExitCode = 1;
        Title = "Mossward — startup failed";
        Content = new TextBlock { Text = exception.ToString(), Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        Console.Error.WriteLine(exception);
        if (smoke)
        {
            Close();
        }
    }
}
