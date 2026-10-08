using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using InputMan.Core;

namespace Aurelian.Ariadne.VnDemo;

internal static class VnPresenter
{
    public static int Run(string[] args)
    {
        VnApplication.LaunchSmokeRequested = args.Contains(
            "--launch-smoke",
            StringComparer.OrdinalIgnoreCase);
        VnApplication.LaunchExitCode = 0;
        BuildApp().StartWithClassicDesktopLifetime(args);
        return VnApplication.LaunchExitCode;
    }

    private static AppBuilder BuildApp()
    {
        return AppBuilder.Configure<VnApplication>()
            .UsePlatformDetect();
    }
}

internal sealed class VnApplication : Application
{
    public static bool LaunchSmokeRequested { get; set; }
    public static int LaunchExitCode { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new VnWindow(LaunchSmokeRequested);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class VnWindow : Window
{
    private readonly bool launchSmokeRequested;
    private readonly Image image;
    private RenApp? app;
    private VnMachinaLayer? machina;
    private VnNativeRenderer? native;
    private WriteableBitmap? bitmap;
    private ulong frameSequence;

    public VnWindow(bool launchSmokeRequested)
    {
        this.launchSmokeRequested = launchSmokeRequested;

        image = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Focusable = true,
        };
        image.PointerPressed += OnPointerPressed;
        image.PointerReleased += OnPointerReleased;

        Width = VnNativeRenderer.Width;
        Height = VnNativeRenderer.Height;
        CanResize = true;
        MinWidth = 640;
        MinHeight = 360;
        Background = Brushes.Black;
        Content = new TextBlock
        {
            Text = "INITIALIZING DAWN ENGINE...",
            Foreground = Brushes.Orange,
            FontSize = 24,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Title = "SUNKILL — STARTING";
        KeyDown += OnKeyDown;
        Closed += OnClosed;
        Opened += OnOpened;
        SizeChanged += OnSizeChanged;
    }

    private void OnOpened(object? sender, EventArgs args)
    {
        InitializeProduct();
    }

    private void InitializeProduct()
    {
        try
        {
            ReportSmokeStage("initialize");
            string root = Program.FindRepositoryRoot();
            ReportSmokeStage("repository-root");
            string userRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SUNKILL");
            app = new RenApp(
                Path.Combine(userRoot, "Saves"),
                Path.Combine(userRoot, "settings.json"),
                stage => ReportSmokeStage($"app-{stage}"));
            ReportSmokeStage("app-state");
            machina = new VnMachinaLayer(app);
            ReportSmokeStage("machina-layer");
            int initialFramebufferWidth = Math.Max(1, (int)Math.Round(ClientSize.Width * RenderScaling));
            int initialFramebufferHeight = Math.Max(1, (int)Math.Round(ClientSize.Height * RenderScaling));
            native = new VnNativeRenderer(
                root,
                app,
                machina,
                initialFramebufferWidth,
                initialFramebufferHeight,
                startupProgress: stage => ReportSmokeStage($"native-{stage}"));
            ReportSmokeStage("native-renderer");
            Content = image;
            Render();
            ReportSmokeStage("first-frame");
            image.Focus();

            if (launchSmokeRequested)
            {
                Console.WriteLine(
                    $"SUNKILL_LAUNCH_READY screen={app.State.Screen} notice={app.State.Notice}");
                Console.Out.Flush();
                Dispatcher.UIThread.Post(Close, DispatcherPriority.Normal);
            }
        }
        catch (Exception exception)
        {
            VnApplication.LaunchExitCode = 1;
            Title = "SUNKILL — STARTUP FAILED";
            Content = new TextBlock
            {
                Text = $"DAWN ENGINE STARTUP FAILED\n\n{exception.Message}",
                Foreground = Brushes.OrangeRed,
                FontSize = 20,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(48),
            };
            Console.Error.WriteLine(exception);
            Console.Error.Flush();

            if (launchSmokeRequested)
            {
                Dispatcher.UIThread.Post(Close, DispatcherPriority.Normal);
            }
        }
    }

    private void ReportSmokeStage(string stage)
    {
        if (!launchSmokeRequested)
        {
            return;
        }

        Console.Error.WriteLine($"SUNKILL_LAUNCH_STAGE {stage}");
        Console.Error.Flush();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (app is null)
        {
            return;
        }

        if (!TryMapKey(args.Key, out KeyboardKey key))
        {
            return;
        }

        app.Press(key);
        CompleteInteraction();
        args.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (native is null)
        {
            return;
        }

        PointerPoint point = args.GetCurrentPoint(image);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        args.Pointer.Capture(image);
        native.Route(new LayerPointerButtonChanged(
            ToLayerPoint(args.GetPosition(image)),
            LayerPointerButton.Primary,
            true));
        args.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (native is null)
        {
            return;
        }

        native.Route(new LayerPointerButtonChanged(
            ToLayerPoint(args.GetPosition(image)),
            LayerPointerButton.Primary,
            false));
        args.Pointer.Capture(null);
        CompleteInteraction();
        args.Handled = true;
    }

    private void CompleteInteraction()
    {
        if (app is null)
        {
            return;
        }

        if (app.ExitRequested)
        {
            Close();
            return;
        }

        Render();
    }

    private void Render()
    {
        if (native is null || app is null)
        {
            throw new InvalidOperationException("The SUNKILL renderer is not initialized.");
        }

        NativeLayerFrameResult frame = native.Render(++frameSequence);
        byte[] pixels = frame.NativeFrame.Pixels
            ?? throw new InvalidOperationException("The native presenter frame did not return pixels.");

        bitmap?.Dispose();
        bitmap = CreateBitmap(pixels, native.FramebufferWidth, native.FramebufferHeight);
        image.Source = bitmap;
        Title = $"SUNKILL — {app.State.Screen} — {app.State.Notice}";
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        bitmap?.Dispose();
        native?.Dispose();
        app?.Dispose();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (native is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        int width = Math.Max(1, (int)Math.Round(ClientSize.Width * RenderScaling));
        int height = Math.Max(1, (int)Math.Round(ClientSize.Height * RenderScaling));
        native.Resize(width, height);
        Render();
    }

    private WriteableBitmap CreateBitmap(byte[] pixels, int width, int height)
    {
        var result = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96 * RenderScaling, 96 * RenderScaling),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using ILockedFramebuffer framebuffer = result.Lock();
        int sourceStride = width * 4;
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(
                pixels,
                row * sourceStride,
                framebuffer.Address + (row * framebuffer.RowBytes),
                sourceStride);
        }

        return result;
    }

    private LayerPoint ToLayerPoint(Point point)
    {
        if (native is null || image.Bounds.Width <= 0 || image.Bounds.Height <= 0)
        {
            return new LayerPoint(point.X, point.Y);
        }

        double physicalX = point.X * native.FramebufferWidth / image.Bounds.Width;
        double physicalY = point.Y * native.FramebufferHeight / image.Bounds.Height;
        return native.ToLogicalPointer(physicalX, physicalY);
    }

    private static bool TryMapKey(Key key, out KeyboardKey mapped)
    {
        mapped = key switch
        {
            Key.Enter => KeyboardKey.Enter,
            Key.Space => KeyboardKey.Space,
            Key.Up => KeyboardKey.ArrowUp,
            Key.Down => KeyboardKey.ArrowDown,
            Key.Left => KeyboardKey.ArrowLeft,
            Key.Right => KeyboardKey.ArrowRight,
            Key.Escape => KeyboardKey.Escape,
            Key.F => KeyboardKey.F,
            Key.I => KeyboardKey.I,
            _ => default,
        };
        return key is Key.Enter
            or Key.Space
            or Key.Up
            or Key.Down
            or Key.Left
            or Key.Right
            or Key.Escape
            or Key.F
            or Key.I;
    }
}
