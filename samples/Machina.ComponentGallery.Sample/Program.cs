using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Machina.Core.Actions;
using Machina.Pipeline;
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

        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(GalleryProgramOptions options)
    {
        return AppBuilder.Configure(() => new App(
                options.InitialState,
                new GalleryProofOptions(options.IncludeDirectOutlineTextProof, options.IncludeMsdfFontProof)))
            .UsePlatformDetect();
    }

    private static void ExportArtifacts(GalleryProgramOptions options)
    {
        var result = GalleryExporter.Export(options);
        Console.WriteLine($"Exported gallery png to {result.OutputPath} ({result.Width}x{result.Height})");
    }

    private sealed class App : Application
    {
        private readonly GalleryState _initialState;
        private readonly GalleryProofOptions _proofOptions;

        public App(GalleryState initialState, GalleryProofOptions proofOptions)
        {
            _initialState = initialState;
            _proofOptions = proofOptions;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new GalleryWindow(_initialState, _proofOptions);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class GalleryWindow : Window
    {
        private const string BaseTitle = "Machina Component Gallery M7b";

        private readonly Image _image;
        private readonly MachinaRasterPipeline _pipeline;
        private readonly GalleryProofOptions _proofOptions;

        private GalleryState _state;
        private UiHitTestIndex _hitTestIndex;
        private MachinaFrame _currentFrame;

        public GalleryWindow(GalleryState initialState, GalleryProofOptions proofOptions)
        {
            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };

            _image.PointerPressed += HandlePointerPressed;

            _state = initialState;
            _proofOptions = proofOptions;
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
            var document = GalleryScreen.Build(_state, _proofOptions, StandardTheme.Default);
            _currentFrame = _pipeline.Render(document, GalleryScreen.Width, GalleryScreen.GetHeight(_proofOptions));
            _hitTestIndex = _currentFrame.HitTest;

            if (_proofOptions.IncludeDirectOutlineTextProof)
            {
                GalleryDirectOutlineTextProofRenderer.BlitProof(
                    _currentFrame.RasterFrame,
                    _currentFrame.Resolved,
                    _proofOptions.IncludeMsdfFontProof);
            }

            if (_proofOptions.IncludeMsdfFontProof)
            {
                GalleryMsdfFontProofRenderer.BlitProof(_currentFrame.RasterFrame, _currentFrame.Resolved);
            }

            _image.Source = GalleryExporter.ToBitmap(_currentFrame.RasterFrame);
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
}
