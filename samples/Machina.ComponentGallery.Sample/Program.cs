using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Core.Actions;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Runtime.Input;
using Machina.Standard.Theme;
using RuntimePointerPoint = Machina.Runtime.Input.PointerPoint;

namespace Machina.ComponentGallery.Sample;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = GalleryProgramOptions.Parse(args);
        if (options.ExportOnly)
        {
            ExportArtifacts(options);
            return;
        }

        BuildAvaloniaApp(options.InitialState).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(GalleryState initialState)
    {
        return AppBuilder.Configure(() => new App(initialState))
            .UsePlatformDetect();
    }

    private static void ExportArtifacts(GalleryProgramOptions options)
    {
        var outputDirectory = options.ExportDirectory ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "m7a");
        Directory.CreateDirectory(outputDirectory);

        var frame = new MachinaRasterPipeline().Render(
            GalleryScreen.Build(options.InitialState, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        var ppmPath = Path.Combine(outputDirectory, $"{options.ExportName}.ppm");

        File.WriteAllBytes(ppmPath, frame.RasterFrame.ToPpm());

        Console.WriteLine($"Exported gallery ppm to {ppmPath}");
    }

    private sealed class App : Application
    {
        private readonly GalleryState _initialState;

        public App(GalleryState initialState)
        {
            _initialState = initialState;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new GalleryWindow(_initialState);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class GalleryWindow : Window
    {
        private const string BaseTitle = "Machina Component Gallery M7a";

        private readonly Image _image;
        private readonly MachinaRasterPipeline _pipeline;

        private GalleryState _state;
        private UiHitTestIndex _hitTestIndex;
        private MachinaFrame _currentFrame;

        public GalleryWindow(GalleryState initialState)
        {
            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };

            _image.PointerPressed += HandlePointerPressed;

            _state = initialState;
            _pipeline = new MachinaRasterPipeline();
            _hitTestIndex = default!;
            _currentFrame = default!;

            RenderCurrentState();
            Title = BuildTitle("<startup>");
            CanResize = false;
            Content = _image;
        }

        private void RenderCurrentState()
        {
            var document = GalleryScreen.Build(_state, StandardTheme.Default);
            _currentFrame = _pipeline.Render(document, GalleryScreen.Width, GalleryScreen.Height);
            _hitTestIndex = _currentFrame.HitTest;

            _image.Source = ToBitmap(_currentFrame.RasterFrame);
            _image.Width = _currentFrame.RasterFrame.Width;
            _image.Height = _currentFrame.RasterFrame.Height;
            Width = _currentFrame.RasterFrame.Width;
            Height = _currentFrame.RasterFrame.Height;
        }

        private void HandlePointerPressed(object? sender, PointerPressedEventArgs args)
        {
            var position = args.GetPosition(_image);
            var presentedPoint = new RuntimePointerPoint(position.X, position.Y);
            var destination = new PresentedImageRect(0, 0, _image.Bounds.Width, _image.Bounds.Height);
            RuntimePointerPoint? rootPoint = PresentedImageMapper.ToRootPoint(
                presentedPoint,
                _currentFrame.RasterFrame.Width,
                _currentFrame.RasterFrame.Height,
                destination,
                ImageStretchMode.None);

            var hit = rootPoint is null ? null : _hitTestIndex.HitTest(rootPoint.Value);
            var action = hit?.Action;
            var actionName = action?.Name ?? "<none>";

            if (action is not null)
            {
                ApplyAction(action);
            }

            Title = BuildTitle(actionName);
            Console.WriteLine(
                $"Pointer ({position.X}, {position.Y}) -> action: {actionName}, clicks: {_state.PrimaryClicks}/{_state.SecondaryClicks}, checkbox: {OnOff(_state.LiveCheckboxChecked)}, switch: {OnOff(_state.LiveSwitchOn)}");
        }

        private void ApplyAction(UiAction action)
        {
            var next = GalleryState.Dispatch(_state, action.Id);
            if (!ReferenceEquals(next, _state))
            {
                _state = next;
                RenderCurrentState();
            }
        }

        private string BuildTitle(string actionName)
        {
            return $"{BaseTitle} - action: {actionName}, clicks: {_state.PrimaryClicks}/{_state.SecondaryClicks}, checkbox: {OnOff(_state.LiveCheckboxChecked)}, switch: {OnOff(_state.LiveSwitchOn)}";
        }
    }

    private static string OnOff(bool value)
    {
        return value ? "on" : "off";
    }

    private static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var locked = bitmap.Lock();
        var pixelBytes = ToRgbaBytes(frame);
        System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);

        return bitmap;
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        var width = frame.Surface.Width;
        var height = frame.Surface.Height;
        var bytes = new byte[width * height * 4];
        var index = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                bytes[index++] = pixel.R;
                bytes[index++] = pixel.G;
                bytes[index++] = pixel.B;
                bytes[index++] = pixel.A;
            }
        }

        return bytes;
    }

    private sealed record GalleryProgramOptions(
        GalleryState InitialState,
        bool ExportOnly,
        string? ExportDirectory,
        string ExportName)
    {
        public static GalleryProgramOptions Parse(IReadOnlyList<string> args)
        {
            var state = GalleryState.Default;
            var exportOnly = false;
            string? exportDirectory = null;
            var exportName = "component-gallery-final";

            for (var index = 0; index < args.Count; index++)
            {
                var arg = args[index];

                if (arg == "--export-only")
                {
                    exportOnly = true;
                    continue;
                }

                if (arg == "--export-dir" && index + 1 < args.Count)
                {
                    exportDirectory = args[++index];
                    continue;
                }

                if (arg == "--export-name" && index + 1 < args.Count)
                {
                    exportName = args[++index];
                    continue;
                }

                if (arg == "--primary-clicks" && index + 1 < args.Count && int.TryParse(args[++index], out var clickCount))
                {
                    state = state with { PrimaryClicks = clickCount };
                    continue;
                }

                if (arg == "--checkbox" && index + 1 < args.Count)
                {
                    state = state with { LiveCheckboxChecked = ParseOnOff(args[++index]) };
                    continue;
                }

                if (arg == "--switch" && index + 1 < args.Count)
                {
                    state = state with { LiveSwitchOn = ParseOnOff(args[++index]) };
                    continue;
                }
            }

            return new GalleryProgramOptions(state, exportOnly, exportDirectory, exportName);
        }

        private static bool ParseOnOff(string value)
        {
            return value.Equals("on", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
