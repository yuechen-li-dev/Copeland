using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
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
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
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
    private static readonly OblivionStandaloneStyle Style = OblivionStandaloneStyles.M19h;
    private readonly OblivionStandaloneOptions _options;
    private readonly OblivionStandaloneSurface _surface;
    private readonly Grid _host;
    private readonly ScrollViewer _pageScroll;
    private readonly Dictionary<string, ScrollViewer> _documentScrollers = new(StringComparer.Ordinal);
    private OblivionStandaloneSurfaceSnapshot _snapshot;
    private int _surfaceWidth = Style.DevelopmentWidth;
    private int _surfaceHeight = Style.DevelopmentHeight;

    public OblivionStandaloneWindow(OblivionStandaloneOptions options)
    {
        _options = options;
        _surface = new OblivionStandaloneSurface();
        if (options.StartExpanded)
        {
            foreach (var card in _surface.Cards)
            {
                _surface.ToggleExpansion(card.Id.Value);
            }
        }

        _host = new Grid
        {
            Width = Style.DevelopmentWidth,
            Height = Style.DevelopmentHeight,
            Background = ToBrush(Style.PageBackground),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _pageScroll = new ScrollViewer
        {
            Content = _host,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _snapshot = _surface.CreateSnapshot(
            Style.DevelopmentWidth,
            Style.DevelopmentHeight);

        Title = "Oblivion";
        ClientSize = new Size(
            Style.DevelopmentWidth,
            Style.DevelopmentHeight);
        MinWidth = 960;
        MinHeight = 640;
        CanResize = true;
        Content = _pageScroll;
        KeyDown += HandleKeyDown;
        PointerPressed += HandlePointerPressed;
        AddHandler(
            InputElement.PointerWheelChangedEvent,
            HandlePointerWheelChanged,
            RoutingStrategies.Tunnel);
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
        OblivionStandaloneCardSnapshot? card = _snapshot.Cards.FirstOrDefault(
            candidate => Contains(candidate.CardBounds, point));
        if (card is null)
        {
            return;
        }

        if (Contains(card.ExpansionAffordanceBounds, point))
        {
            _surface.ToggleExpansion(card.Card.Id.Value);
        }
        else
        {
            _surface.Select(card.Card.Id.Value);
        }

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

    private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        Point point = args.GetPosition(_host);
        OblivionStandaloneCardSnapshot? card = _snapshot.Cards.FirstOrDefault(
            candidate => Contains(candidate.CardBounds, point));
        if (card is not null &&
            _documentScrollers.TryGetValue(card.Card.Id.Value, out ScrollViewer? documentScroll) &&
            CanConsumeWheel(documentScroll, args.Delta.Y))
        {
            return;
        }

        double offset = OblivionStandaloneScrollRouting.ComputePageOffset(
            _pageScroll.Offset.Y,
            _pageScroll.Extent.Height,
            _pageScroll.Viewport.Height,
            args.Delta.Y);
        _pageScroll.Offset = new Vector(_pageScroll.Offset.X, offset);
        _surface.SetPageScrollOffset(offset);
        args.Handled = true;
    }

    private void HandleKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers != KeyModifiers.None ||
            args.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        string? selectedCardId = _surface.SelectedCardId;
        if (selectedCardId is null)
        {
            return;
        }

        _surface.ToggleExpansion(selectedCardId);
        RefreshSurface();
        args.Handled = true;
    }

    private void RefreshSurface()
    {
        RefreshSurface(_surfaceWidth, _surfaceHeight);
    }

    private void RefreshSurface(int width, int height)
    {
        _surfaceWidth = width;
        _surfaceHeight = height;
        Vector previousOffset = _pageScroll.Offset;
        _snapshot = _surface.CreateSnapshot(width, height);
        _pageScroll.VerticalScrollBarVisibility = _snapshot.PageContentHeight > _snapshot.ViewportHeight
            ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _host.Width = _snapshot.Width;
        _host.Height = _snapshot.PageContentHeight;
        _host.Children.Clear();
        _documentScrollers.Clear();
        _host.Children.Add(new Image
        {
            Source = ToBitmap(_snapshot.ShellFrame),
            Width = _snapshot.Width,
            Height = _snapshot.PageContentHeight,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });

        foreach (OblivionStandaloneCardSnapshot card in _snapshot.Cards)
        {
            AddMatureDocument(card);
        }

        _host.Measure(new Size(_snapshot.Width, _snapshot.PageContentHeight));
        _pageScroll.Offset = previousOffset;
    }

    private void AddMatureDocument(OblivionStandaloneCardSnapshot cardSnapshot)
    {
        if (cardSnapshot.MatureContentBounds is not MachinaRect bodyBounds)
        {
            return;
        }

        Control document = AvaloniaOblivionContentHost.Build(
            cardSnapshot.Card,
            cardSnapshot.ContentPlan,
            new UnavailableDiagramRenderer(),
            Path.Combine(Path.GetTempPath(), "oblivion-m19h-diagrams"),
            workspaceId: _surface.Presentation.Workspace.Id.Value,
            pageId: _surface.PageId,
            maximumReadableWidth: Style.MaximumReadableWidth);
        document.Width = bodyBounds.Width;
        document.Height = bodyBounds.Height;
        document.Margin = new Thickness(bodyBounds.X, bodyBounds.Y, 0, 0);
        document.HorizontalAlignment = HorizontalAlignment.Left;
        document.VerticalAlignment = VerticalAlignment.Top;
        _host.Children.Add(document);
        if (document is Border { Child: ScrollViewer documentScroll })
        {
            _documentScrollers[cardSnapshot.Card.Id.Value] = documentScroll;
        }
    }

    private static bool CanConsumeWheel(ScrollViewer scrollViewer, double deltaY)
    {
        return OblivionStandaloneScrollRouting.ResolveOwner(
            scrollViewer.Extent.Height,
            scrollViewer.Viewport.Height,
            scrollViewer.Offset.Y,
            deltaY) == OblivionStandaloneScrollOwner.Document;
    }

    private void CaptureAndExit()
    {
        string capturePath = Path.GetFullPath(_options.CapturePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
        _pageScroll.Measure(new Size(_snapshot.Width, _snapshot.ViewportHeight));
        _pageScroll.Arrange(new global::Avalonia.Rect(
            0,
            0,
            _snapshot.Width,
            _snapshot.ViewportHeight));
        double maximumOffset = Math.Max(0, _pageScroll.Extent.Height - _pageScroll.Viewport.Height);
        double captureOffset = Math.Clamp(_options.InitialPageScrollOffset, 0, maximumOffset);
        _pageScroll.Offset = new Vector(0, captureOffset);
        _pageScroll.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            new PixelSize(_snapshot.Width, _snapshot.ViewportHeight),
            new Vector(96, 96));
        bitmap.Render(_pageScroll);
        using (FileStream stream = File.Create(capturePath))
        {
            bitmap.Save(stream);
        }

        Console.WriteLine(
            $"Captured {(_surface.AreAllCardsExpanded ? "expanded" : "collapsed")} standalone Oblivion surface to {capturePath} ({_snapshot.Width}x{_snapshot.ViewportHeight}).");
        Console.WriteLine(
            $"Page scroll extent={_pageScroll.Extent.Height:0.###}, viewport={_pageScroll.Viewport.Height:0.###}, offset={_pageScroll.Offset.Y:0.###}.");
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

    private static IBrush ToBrush(Machina.Core.Styling.ColorToken token)
    {
        uint rgba = token.Rgba;
        byte red = (byte)(rgba >> 24);
        byte green = (byte)(rgba >> 16);
        byte blue = (byte)(rgba >> 8);
        byte alpha = (byte)rgba;
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
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
                RendererVersion: "m19h",
                SourceHash: string.Empty,
                RenderedPath: null,
                MediaType: null,
                Diagnostics: []);
        }
    }
}

internal sealed record OblivionStandaloneOptions(
    bool StartExpanded,
    string? CapturePath,
    double InitialPageScrollOffset)
{
    public static OblivionStandaloneOptions Parse(IReadOnlyList<string> args)
    {
        bool expanded = false;
        string? capturePath = null;
        double initialPageScrollOffset = 0;
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

            if (string.Equals(argument, "--scroll-offset", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                if (!double.TryParse(
                    args[++index],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out initialPageScrollOffset) ||
                    initialPageScrollOffset < 0)
                {
                    throw new ArgumentException("--scroll-offset requires a non-negative number.");
                }

                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            throw new ArgumentException($"Unknown standalone option '{argument}'.");
        }

        return new OblivionStandaloneOptions(expanded, capturePath, initialPageScrollOffset);
    }
}
