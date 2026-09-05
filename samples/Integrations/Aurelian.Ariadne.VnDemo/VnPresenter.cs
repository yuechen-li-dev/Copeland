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
using Ariadne.OptFlow.Presentation;
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
    private readonly VnSession session;
    private readonly VnMachinaLayer machina;
    private readonly VnNativeRenderer native;
    private readonly VnPersistence persistence;
    private readonly Image image;
    private readonly DispatcherTimer automationTimer;
    private WriteableBitmap? bitmap;
    private ulong frameSequence;
    private DateTimeOffset nextAutomaticAdvance;
    private string notice = "READY";

    public VnWindow()
    {
        string root = Program.FindRepositoryRoot();
        string saveRoot = Path.Combine(
            root,
            "artifacts",
            "aurelian-ariadne-machina-dialogue-m7b",
            "presenter-saves");

        session = new VnSession();
        machina = new VnMachinaLayer(session);
        native = new VnNativeRenderer(root, session, machina);
        persistence = new VnPersistence(saveRoot);
        session.SaveRequested = SaveQuickSlot;
        session.LoadRequested = LoadQuickSlot;

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

        automationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, OnAutomationTick);
        automationTimer.Start();
        Render();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!TryMapKey(args.Key, out KeyboardKey key))
        {
            return;
        }

        string? previousStep = session.Presentation.OperationId;
        session.Press(key);
        if (session.Presentation.OperationId != previousStep || key is KeyboardKey.A or KeyboardKey.S or KeyboardKey.Escape)
        {
            ResetAutomaticDeadline();
        }

        Render();
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
        ResetAutomaticDeadline();
        Render();
        args.Handled = true;
    }

    private void OnAutomationTick(object? sender, EventArgs args)
    {
        if ((!session.AutoEnabled && !session.SkipEnabled) || DateTimeOffset.UtcNow < nextAutomaticAdvance)
        {
            return;
        }

        string? previousStep = session.Presentation.OperationId;
        session.PulseAutomatic();
        if (session.Presentation.OperationId != previousStep)
        {
            Render();
        }

        ResetAutomaticDeadline();
    }

    private void SaveQuickSlot()
    {
        persistence.SaveAsync("quick", session).GetAwaiter().GetResult();
        notice = "QUICK SAVE WRITTEN";
    }

    private void LoadQuickSlot()
    {
        try
        {
            persistence.LoadAsync("quick", session).GetAwaiter().GetResult();
            notice = "QUICK SAVE RESTORED";
            ResetAutomaticDeadline();
        }
        catch (FileNotFoundException)
        {
            notice = "NO QUICK SAVE YET";
        }
    }

    private void Render()
    {
        NativeLayerFrameResult frame = native.Render(++frameSequence);
        byte[] pixels = frame.NativeFrame.Pixels
            ?? throw new InvalidOperationException("The native presenter frame did not return pixels.");

        bitmap?.Dispose();
        bitmap = CreateBitmap(pixels);
        image.Source = bitmap;
        DialoguePresentationSnapshot presentation = session.Presentation;
        Title = $"Aurelian VN - {presentation.OperationId} - {notice} - Auto {(session.AutoEnabled ? "ON" : "OFF")} / Skip {(session.SkipEnabled ? "ON" : "OFF")}";
    }

    private void ResetAutomaticDeadline()
    {
        int delayMilliseconds = session.SkipEnabled ? 90 : 1400;
        nextAutomaticAdvance = DateTimeOffset.UtcNow.AddMilliseconds(delayMilliseconds);
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        automationTimer.Stop();
        bitmap?.Dispose();
        native.Dispose();
        session.Dispose();
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
                framebuffer.Address + row * framebuffer.RowBytes,
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
            Key.Escape => KeyboardKey.Escape,
            Key.A => KeyboardKey.A,
            Key.S => KeyboardKey.S,
            Key.F => KeyboardKey.F,
            Key.I => KeyboardKey.I,
            _ => default,
        };
        return key is Key.Enter or Key.Space or Key.Up or Key.Down or Key.Escape or Key.A or Key.S or Key.F or Key.I;
    }
}
