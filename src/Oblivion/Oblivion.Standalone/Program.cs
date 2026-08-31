using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Oblivion.App;
using Oblivion.Avalonia;
using Oblivion.Product;
using MachinaRect = Machina.Layout.Geometry.Rect;
using RasterFrame = Aurelian.Rendering.Raster.RasterFrame;

namespace Oblivion.Standalone;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        OblivionStandaloneOptions options = OblivionStandaloneOptions.Parse(args);
        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(OblivionStandaloneOptions options)
    {
        return AppBuilder.Configure(() => new OblivionStandaloneApplication(options))
            .UsePlatformDetect();
    }
}

internal sealed class OblivionStandaloneApplication : Application
{
    private readonly OblivionStandaloneOptions _options;

    public OblivionStandaloneApplication(OblivionStandaloneOptions options)
    {
        _options = options;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new OblivionStandaloneWindow(_options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class OblivionStandaloneWindow : Window
{
    private readonly OblivionStandaloneOptions _options;
    private readonly OblivionStandaloneSurface _surface;
    private readonly Grid _host;
    private OblivionStandaloneSurfaceSnapshot _snapshot;

    public OblivionStandaloneWindow(OblivionStandaloneOptions options)
    {
        _options = options;
        _surface = new OblivionStandaloneSurface();
        if (options.StartExpanded)
        {
            _surface.ToggleExpansion();
        }

        _host = new Grid
        {
            Width = OblivionStandaloneRenderer.DevelopmentWidth,
            Height = OblivionStandaloneRenderer.DevelopmentHeight,
            Background = Brush.Parse("#050914"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _snapshot = _surface.CreateSnapshot(
            OblivionStandaloneRenderer.DevelopmentWidth,
            OblivionStandaloneRenderer.DevelopmentHeight);

        Title = "Oblivion";
        ClientSize = new Size(
            OblivionStandaloneRenderer.DevelopmentWidth,
            OblivionStandaloneRenderer.DevelopmentHeight);
        MinWidth = 960;
        MinHeight = 640;
        CanResize = true;
        Content = _host;
        KeyDown += HandleKeyDown;
        PointerPressed += HandlePointerPressed;
        Opened += HandleOpened;
        SizeChanged += HandleSizeChanged;

        RefreshSurface();
    }

    private void HandleOpened(object? sender, EventArgs args)
    {
        Focus();
        if (!string.IsNullOrWhiteSpace(_options.CapturePath))
        {
            Dispatcher.UIThread.Post(CaptureAndExit, DispatcherPriority.Loaded);
        }
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        Point point = args.GetPosition(_host);
        if (!Contains(_snapshot.ExpansionAffordanceBounds, point))
        {
            return;
        }

        _surface.ToggleExpansion();
        RefreshSurface();
        args.Handled = true;
    }

    private void HandleSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(_options.CapturePath))
        {
            return;
        }

        int width = Math.Max(960, (int)Math.Round(args.NewSize.Width));
        int height = Math.Max(640, (int)Math.Round(args.NewSize.Height));
        _host.Width = width;
        _host.Height = height;
        RefreshSurface(width, height);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        _surface.ToggleExpansion();
        RefreshSurface();
        args.Handled = true;
    }

    private void RefreshSurface()
    {
        RefreshSurface(
            OblivionStandaloneRenderer.DevelopmentWidth,
            OblivionStandaloneRenderer.DevelopmentHeight);
    }

    private void RefreshSurface(int width, int height)
    {
        _snapshot = _surface.CreateSnapshot(width, height);
        _host.Children.Clear();
        _host.Children.Add(new Image
        {
            Source = ToBitmap(_snapshot.ShellFrame),
            Width = _snapshot.Width,
            Height = _snapshot.Height,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });

        if (_snapshot.MatureContentBounds is not MachinaRect bodyBounds)
        {
            return;
        }

        Control document = AvaloniaOblivionContentHost.Build(
            _snapshot.Card,
            _snapshot.ContentPlan,
            new UnavailableDiagramRenderer(),
            Path.Combine(Path.GetTempPath(), "oblivion-m19g-diagrams"),
            workspaceId: _surface.Presentation.Workspace.Id.Value,
            pageId: _surface.PageId,
            maximumReadableWidth: OblivionStandaloneRenderer.MaximumReadableWidth);
        document.Width = bodyBounds.Width;
        document.Height = bodyBounds.Height;
        document.Margin = new Thickness(bodyBounds.X, bodyBounds.Y, 0, 0);
        document.HorizontalAlignment = HorizontalAlignment.Left;
        document.VerticalAlignment = VerticalAlignment.Top;
        _host.Children.Add(document);
    }

    private void CaptureAndExit()
    {
        string capturePath = Path.GetFullPath(_options.CapturePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
        _host.Measure(new Size(_snapshot.Width, _snapshot.Height));
        _host.Arrange(new global::Avalonia.Rect(0, 0, _snapshot.Width, _snapshot.Height));
        RenderTargetBitmap bitmap = new(
            new PixelSize(_snapshot.Width, _snapshot.Height),
            new Vector(96, 96));
        bitmap.Render(_host);
        using (FileStream stream = File.Create(capturePath))
        {
            bitmap.Save(stream);
        }

        Console.WriteLine(
            $"Captured {(_surface.IsExpanded ? "expanded" : "collapsed")} standalone Oblivion surface to {capturePath} ({_snapshot.Width}x{_snapshot.Height}).");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static bool Contains(MachinaRect bounds, Point point)
    {
        return point.X >= bounds.X &&
            point.X <= bounds.X + bounds.Width &&
            point.Y >= bounds.Y &&
            point.Y <= bounds.Y + bounds.Height;
    }

    private static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(frame.Surface.Width, frame.Surface.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using ILockedFramebuffer locked = bitmap.Lock();
        byte[] pixels = new byte[locked.RowBytes * frame.Surface.Height];
        for (int y = 0; y < frame.Surface.Height; y++)
        {
            int rowOffset = y * locked.RowBytes;
            for (int x = 0; x < frame.Surface.Width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                int pixelOffset = rowOffset + (x * 4);
                pixels[pixelOffset] = pixel.R;
                pixels[pixelOffset + 1] = pixel.G;
                pixels[pixelOffset + 2] = pixel.B;
                pixels[pixelOffset + 3] = pixel.A;
            }
        }

        Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
        return bitmap;
    }

    private sealed class UnavailableDiagramRenderer : IOblivionDiagramRenderer
    {
        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            return new OblivionDiagramRenderResult(
                Succeeded: false,
                Renderer: "not-required",
                RendererVersion: "m19g",
                SourceHash: string.Empty,
                RenderedPath: null,
                MediaType: null,
                Diagnostics: []);
        }
    }
}

internal sealed record OblivionStandaloneOptions(
    bool StartExpanded,
    string? CapturePath)
{
    public static OblivionStandaloneOptions Parse(IReadOnlyList<string> args)
    {
        bool expanded = false;
        string? capturePath = null;
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (string.Equals(argument, "--expanded", StringComparison.Ordinal))
            {
                expanded = true;
                continue;
            }

            if (string.Equals(argument, "--capture", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                capturePath = args[++index];
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            throw new ArgumentException($"Unknown standalone option '{argument}'.");
        }

        return new OblivionStandaloneOptions(expanded, capturePath);
    }
}
