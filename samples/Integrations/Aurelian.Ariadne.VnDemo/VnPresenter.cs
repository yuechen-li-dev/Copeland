using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using InputMan.Core;

namespace Aurelian.Ariadne.VnDemo;

internal static class VnPresenter
{
    public static int Run(string[] args)
    {
        BuildApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildApp()
    {
        return AppBuilder.Configure<VnApplication>()
            .UsePlatformDetect();
    }
}

internal sealed class VnApplication : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new VnWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class VnWindow : Window
{
    private readonly RenApp app;
    private readonly VnMachinaLayer machina;
    private readonly VnNativeRenderer native;
    private readonly Image image;
    private WriteableBitmap? bitmap;
    private ulong frameSequence;

    public VnWindow()
    {
        string root = Program.FindRepositoryRoot();
        string userRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SUNKILL");
        app = new RenApp(
            Path.Combine(userRoot, "Saves"),
            Path.Combine(userRoot, "settings.json"));
        machina = new VnMachinaLayer(app);
        native = new VnNativeRenderer(root, app, machina);

        image = new Image
        {
            Width = VnNativeRenderer.Width,
            Height = VnNativeRenderer.Height,
            Stretch = Stretch.None,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Focusable = true,
        };
        image.PointerPressed += OnPointerPressed;
        image.PointerReleased += OnPointerReleased;

        Width = VnNativeRenderer.Width;
        Height = VnNativeRenderer.Height;
        CanResize = false;
        Content = image;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
        Opened += (_, _) => image.Focus();
        Render();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
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
        if (app.ExitRequested)
        {
            Close();
            return;
        }

        Render();
    }

    private void Render()
    {
        NativeLayerFrameResult frame = native.Render(++frameSequence);
        byte[] pixels = frame.NativeFrame.Pixels
            ?? throw new InvalidOperationException("The native presenter frame did not return pixels.");

        bitmap?.Dispose();
        bitmap = CreateBitmap(pixels);
        image.Source = bitmap;
        Title = $"SUNKILL — {app.State.Screen} — {app.State.Notice}";
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        bitmap?.Dispose();
        native.Dispose();
        app.Dispose();
    }

    private static WriteableBitmap CreateBitmap(byte[] pixels)
    {
        var result = new WriteableBitmap(
            new PixelSize(VnNativeRenderer.Width, VnNativeRenderer.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using ILockedFramebuffer framebuffer = result.Lock();
        int sourceStride = VnNativeRenderer.Width * 4;
        for (int row = 0; row < VnNativeRenderer.Height; row++)
        {
            Marshal.Copy(
                pixels,
                row * sourceStride,
                framebuffer.Address + (row * framebuffer.RowBytes),
                sourceStride);
        }

        return result;
    }

    private static LayerPoint ToLayerPoint(Point point)
    {
        return new LayerPoint(point.X, point.Y);
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
