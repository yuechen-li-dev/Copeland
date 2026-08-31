using System.Runtime.InteropServices;
using System.Text.Json;
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
using Avalonia.VisualTree;
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
        OblivionConfigResult configResult = new OblivionConfigStore().Load();
        if (!configResult.Succeeded || configResult.Config is null)
        {
            throw new InvalidOperationException(
                "Oblivion standalone configuration could not be loaded:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, configResult.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        OblivionConfig config = options.AppearanceOverride is null
            ? configResult.Config
            : configResult.Config with { Appearance = options.AppearanceOverride.Value };
        BuildAvaloniaApp(options, config).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(
        OblivionStandaloneOptions options,
        OblivionConfig config)
    {
        return AppBuilder.Configure(() => new OblivionStandaloneApplication(options, config))
            .UsePlatformDetect();
    }
}

internal sealed class OblivionStandaloneApplication : Application
{
    private readonly OblivionStandaloneOptions _options;
    private readonly OblivionConfig _config;

    public OblivionStandaloneApplication(
        OblivionStandaloneOptions options,
        OblivionConfig config)
    {
        _options = options;
        _config = config;
        RequestedThemeVariant = config.Appearance switch
        {
            OblivionAppearance.Light => ThemeVariant.Light,
            OblivionAppearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            OblivionResolvedAppearance platformAppearance = ActualThemeVariant == ThemeVariant.Light
                ? OblivionResolvedAppearance.Light
                : OblivionResolvedAppearance.Dark;
            OblivionResolvedAppearance resolvedAppearance = OblivionStandaloneAppearanceResolver.Resolve(
                _config.Appearance,
                platformAppearance);
            RequestedThemeVariant = resolvedAppearance == OblivionResolvedAppearance.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            string platformDiagnostic = _config.Appearance == OblivionAppearance.System
                ? platformAppearance.ToString().ToLowerInvariant()
                : "not-consulted";
            Console.WriteLine(
                $"Oblivion appearance configured={_config.Appearance.ToString().ToLowerInvariant()} " +
                $"platform={platformDiagnostic} " +
                $"resolved={resolvedAppearance.ToString().ToLowerInvariant()}.");
            desktop.MainWindow = new OblivionStandaloneWindow(
                _options,
                OblivionStandaloneStyles.For(resolvedAppearance));
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class OblivionStandaloneWindow : Window
{
    private readonly OblivionStandaloneStyle _style;
    private readonly OblivionStandaloneOptions _options;
    private readonly OblivionStandaloneSurface _surface;
    private readonly Grid _host;
    private readonly ScrollViewer _pageScroll;
    private readonly Dictionary<string, ScrollViewer> _documentScrollers = new(StringComparer.Ordinal);
    private readonly IOblivionDiagramRenderer _diagramRenderer;
    private OblivionStandaloneSurfaceSnapshot _snapshot;
    private int _surfaceWidth;
    private int _surfaceHeight;

    public OblivionStandaloneWindow(
        OblivionStandaloneOptions options,
        OblivionStandaloneStyle style)
    {
        _options = options;
        _style = style;
        _surfaceWidth = style.DevelopmentWidth;
        _surfaceHeight = style.DevelopmentHeight;
        _surface = new OblivionStandaloneSurface(options.VaultRoot, style);
        _diagramRenderer = CreateDiagramRenderer(options);
        if (options.StartExpanded)
        {
            foreach (var card in _surface.Cards)
            {
                _surface.ToggleExpansion(card.Id.Value);
            }
        }

        _surface.SetLayout(options.LayoutMode);
        if (Math.Abs(options.DiagramZoom - 1) > 0.0001)
        {
            _surface.ZoomDiagram(options.DiagramZoom);
        }
        if (Math.Abs(options.DiagramPanX) > 0.0001 || Math.Abs(options.DiagramPanY) > 0.0001)
        {
            _surface.PanDiagram(options.DiagramPanX, options.DiagramPanY);
        }

        _host = new Grid
        {
            Width = style.DevelopmentWidth,
            Height = style.DevelopmentHeight,
            Background = ToBrush(style.PageBackground),
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
            style.DevelopmentWidth,
            style.DevelopmentHeight);

        Title = "Oblivion";
        ClientSize = new Size(
            style.DevelopmentWidth,
            style.DevelopmentHeight);
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

    private IOblivionDiagramRenderer CreateDiagramRenderer(OblivionStandaloneOptions options)
    {
        if (!options.UseNativeDiagramExperiment)
        {
            return new OblivionExternalMermaidRenderer(
                OblivionMermaidRendererDiscovery.Discover());
        }

        Oblivion.Model.OblivionCard card = _surface.Cards.Single(candidate =>
            candidate.Kind == Oblivion.Model.OblivionCardKind.Diagram);
        OblivionDiagramCardRealizer realizer = new();
        OblivionDiagramSemanticProjectionResult semantic = realizer
            .ProjectSemanticDiagram(card, options.VaultRoot);
        OblivionDiagramProjectionResult projection = realizer.Project(card, options.VaultRoot);
        if (!semantic.Succeeded || semantic.Diagram is null ||
            !projection.Succeeded || projection.SemanticFingerprint is null)
        {
            throw new InvalidOperationException(
                "The native diagram backend could not project the semantic Diagram IR:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    semantic.Diagnostics.Concat(projection.Diagnostics).Select(diagnostic => diagnostic.Message)));
        }

        return new OblivionFallbackDiagramRenderer(
            new OblivionNativeSvgRenderer(semantic.Diagram, projection.SemanticFingerprint),
            new OblivionExternalMermaidRenderer(OblivionMermaidRendererDiscovery.Discover()));
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
            _surface.FocusSlot(card.SlotId);
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
        if (args.KeyModifiers == KeyModifiers.Control)
        {
            switch (args.Key)
            {
                case Key.D1:
                    _surface.SetLayout(OblivionViewportLayoutMode.Single);
                    break;
                case Key.D2:
                    _surface.SetLayout(OblivionViewportLayoutMode.VerticalSplit);
                    break;
                case Key.D3:
                    _surface.SetLayout(OblivionViewportLayoutMode.HorizontalSplit);
                    break;
                case Key.D0:
                    _surface.FitDiagram();
                    break;
                case Key.OemPlus:
                case Key.Add:
                    _surface.ZoomDiagram(OblivionDiagramViewportState.ZoomStep);
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    _surface.ZoomDiagram(1 / OblivionDiagramViewportState.ZoomStep);
                    break;
                case Key.Tab:
                    _surface.FocusNextSlot();
                    break;
                default:
                    return;
            }

            RefreshSurface();
            args.Handled = true;
            return;
        }

        if (args.KeyModifiers != KeyModifiers.None || args.Key is not (Key.Enter or Key.Space))
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
            _diagramRenderer,
            Path.GetFullPath(Path.Combine(
                "artifacts",
                "derived",
                _options.UseNativeDiagramExperiment ? "native-svg" : "mermaid")),
            workspaceId: _surface.Workspace.Id.Value,
            pageId: _surface.PageId,
            maximumReadableWidth: _style.MaximumReadableWidth,
            style: ToContentStyle(_style),
            resolvedAppearance: _style.Appearance,
            diagramViewportState: cardSnapshot.DiagramViewportState,
            diagramViewportStateChanged: state =>
                _surface.SetDiagramViewportState(cardSnapshot.Card.Id.Value, state),
            fillDiagramViewport: cardSnapshot.Card.Kind == Oblivion.Model.OblivionCardKind.Diagram);
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

        WriteViewportProof(capturePath);

        Console.WriteLine(
            $"Captured {(_surface.AreAllCardsExpanded ? "expanded" : "collapsed")} standalone Oblivion surface to {capturePath} ({_snapshot.Width}x{_snapshot.ViewportHeight}).");
        Console.WriteLine(
            $"Page scroll extent={_pageScroll.Extent.Height:0.###}, viewport={_pageScroll.Viewport.Height:0.###}, offset={_pageScroll.Offset.Y:0.###}.");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void WriteViewportProof(string capturePath)
    {
        AvaloniaOblivionDiagramCanvas? canvas = _host
            .GetVisualDescendants()
            .OfType<AvaloniaOblivionDiagramCanvas>()
            .FirstOrDefault();
        object proof = new
        {
            window = new { width = _snapshot.Width, height = _snapshot.ViewportHeight },
            usableViewport = new
            {
                x = _style.OuterHorizontalMargin,
                y = _style.OuterVerticalMargin,
                width = _snapshot.Width - (_style.OuterHorizontalMargin * 2),
                height = _snapshot.ViewportHeight - (_style.OuterVerticalMargin * 2),
            },
            layoutMode = _snapshot.Viewport.LayoutMode.ToString(),
            focusedSlot = _snapshot.Viewport.FocusedSlot.ToString(),
            slots = _snapshot.Slots.Select(slot => new
            {
                slotId = slot.SlotId.ToString(),
                cardId = slot.CardId,
                focused = slot.IsFocused,
                bounds = new
                {
                    x = slot.Bounds.X,
                    y = slot.Bounds.Y,
                    width = slot.Bounds.Width,
                    height = slot.Bounds.Height,
                },
            }),
            diagram = canvas is null ? null : new
            {
                fitMode = canvas.ViewState.FitMode.ToString(),
                zoom = canvas.ViewState.Zoom,
                panX = canvas.ViewState.PanX,
                panY = canvas.ViewState.PanY,
                fitScale = Math.Min(
                    canvas.Camera.ViewportWidth / canvas.Camera.WorldWidth,
                    canvas.Camera.ViewportHeight / canvas.Camera.WorldHeight),
                scale = canvas.Camera.Scale,
                worldWidth = canvas.Camera.WorldWidth,
                worldHeight = canvas.Camera.WorldHeight,
                viewportWidth = canvas.Camera.ViewportWidth,
                viewportHeight = canvas.Camera.ViewportHeight,
            },
        };
        string path = Path.ChangeExtension(capturePath, ".viewport.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
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

    private static AvaloniaOblivionContentStyle ToContentStyle(OblivionStandaloneStyle style)
    {
        return new AvaloniaOblivionContentStyle(
            style.DocumentSurface.Rgba,
            style.DocumentText.Rgba,
            style.DocumentHeading.Rgba,
            style.DocumentMutedText.Rgba,
            style.DocumentCodeSurface.Rgba,
            style.DocumentBorder.Rgba,
            style.DocumentQuoteBorder.Rgba,
            style.DocumentLinkText.Rgba,
            style.DocumentDiagnosticText.Rgba);
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

}

internal sealed record OblivionStandaloneOptions(
    bool StartExpanded,
    string? CapturePath,
    double InitialPageScrollOffset,
    string VaultRoot,
    OblivionViewportLayoutMode LayoutMode,
    double DiagramZoom,
    double DiagramPanX,
    double DiagramPanY,
    bool UseNativeDiagramExperiment,
    OblivionAppearance? AppearanceOverride)
{
    public static OblivionStandaloneOptions Parse(IReadOnlyList<string> args)
    {
        bool expanded = false;
        string? capturePath = null;
        double initialPageScrollOffset = 0;
        string vaultRoot = M19iStructuredVault.DefaultRoot;
        OblivionViewportLayoutMode layoutMode = OblivionViewportLayoutMode.Single;
        double diagramZoom = 1;
        double diagramPanX = 0;
        double diagramPanY = 0;
        bool useNativeDiagramExperiment = false;
        OblivionAppearance? appearanceOverride = null;
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

            if (string.Equals(argument, "--vault", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                vaultRoot = Path.GetFullPath(args[++index]);
                continue;
            }

            if (string.Equals(argument, "--layout", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                layoutMode = args[++index] switch
                {
                    "single" => OblivionViewportLayoutMode.Single,
                    "vertical" => OblivionViewportLayoutMode.VerticalSplit,
                    "horizontal" => OblivionViewportLayoutMode.HorizontalSplit,
                    string value => throw new ArgumentException(
                        $"Unknown layout '{value}'. Expected single, vertical, or horizontal."),
                };
                continue;
            }

            if (string.Equals(argument, "--diagram-zoom", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                if (!double.TryParse(
                    args[++index],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out diagramZoom) ||
                    diagramZoom <= 0)
                {
                    throw new ArgumentException("--diagram-zoom requires a positive number.");
                }
                continue;
            }

            if (string.Equals(argument, "--diagram-pan", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                string[] parts = args[++index].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out diagramPanX) ||
                    !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out diagramPanY))
                {
                    throw new ArgumentException("--diagram-pan requires x,y numbers.");
                }
                continue;
            }

            if (string.Equals(argument, "--diagram-backend", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                string backend = args[++index];
                useNativeDiagramExperiment = backend switch
                {
                    "mermaid" => false,
                    "native" => true,
                    "native-experiment" => true,
                    _ => throw new ArgumentException(
                        $"Unknown diagram backend '{backend}'. Expected mermaid or native."),
                };
                continue;
            }

            if (string.Equals(argument, "--appearance", StringComparison.Ordinal) && index + 1 < args.Count)
            {
                appearanceOverride = args[++index] switch
                {
                    "light" => OblivionAppearance.Light,
                    "dark" => OblivionAppearance.Dark,
                    string value => throw new ArgumentException(
                        $"Unknown appearance '{value}'. Expected light or dark."),
                };
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            throw new ArgumentException($"Unknown standalone option '{argument}'.");
        }

        return new OblivionStandaloneOptions(
            expanded,
            capturePath,
            initialPageScrollOffset,
            vaultRoot,
            layoutMode,
            diagramZoom,
            diagramPanX,
            diagramPanY,
            useNativeDiagramExperiment,
            appearanceOverride);
    }
}
