using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Machina.Core.Actions;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Theme;
using RuntimePointerPoint = Machina.Runtime.Input.PointerPoint;

namespace Machina.Presenter.Sample;

internal sealed class Program
{
    public static readonly StandardTheme AppTheme =
        StandardTheme.Default with
        {
            Button = StandardTheme.Default.Button with
            {
                Default = StandardTheme.Default.Button.Default with
                {
                    Background = ColorToken.Hex(0x111827FF),
                    Foreground = ColorToken.Hex(0xF9FAFBFF),
                },
            },
            Card = StandardTheme.Default.Card with
            {
                Default = StandardTheme.Default.Card.Default with
                {
                    ContentInset = 18,
                },
            },
        };

    [STAThread]
    public static void Main(string[] args)
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse(args);
        if (options.ExportOnly)
        {
            PresenterExportResult result = PresenterExporter.Export(options);
            Console.WriteLine($"Exported presenter png to {result.OutputPath} ({result.Width}x{result.Height})");
            return;
        }

        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(PresenterProgramOptions options)
    {
        return AppBuilder.Configure(() => new App(options.ProofOptions))
            .UsePlatformDetect();
    }

    private sealed class App : Application
    {
        private readonly PresenterProofOptions _proofOptions;

        public App(PresenterProofOptions proofOptions)
        {
            _proofOptions = proofOptions;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new PresenterWindow(_proofOptions);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class PresenterWindow : Window
    {
        private const string BaseTitle = "Machina Presenter M1e";

        private readonly Image _image;

        private DemoState _state;
        private readonly MachinaRasterPipeline _pipeline;
        private readonly PresenterProofOptions _proofOptions;
        private UiHitTestIndex _hitTestIndex;
        private MachinaFrame _currentFrame;

        public PresenterWindow(PresenterProofOptions proofOptions)
        {
            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            _image.PointerPressed += HandlePointerPressed;

            _state = new DemoState(
                Count: 0,
                EmailUpdates: true,
                Notifications: false);
            _pipeline = new MachinaRasterPipeline();
            _proofOptions = proofOptions;
            _hitTestIndex = default!;
            _currentFrame = default!;

            RenderCurrentState();
            Title = BuildTitle("startup");

            CanResize = false;
            Content = _image;
        }

        private void RenderCurrentState()
        {
            var ui = SettingsScreen.Build(_state, AppTheme, _proofOptions);
            _currentFrame = _pipeline.Render(
                ui,
                SettingsScreen.GetWidth(_proofOptions),
                SettingsScreen.GetHeight(_proofOptions));

            if (_proofOptions.IncludeDirectOutlineRenderBridgeProof)
            {
                PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(_currentFrame.RasterFrame, _currentFrame.Resolved);
            }

            _hitTestIndex = _currentFrame.HitTest;

            _image.Source = PresenterExporter.ToBitmap(_currentFrame.RasterFrame);
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

            var point = rootPoint;
            var hit = point is null ? null : _hitTestIndex.HitTest(point.Value);
            var action = hit?.Action;
            var actionName = action?.Name ?? "<none>";

            if (action is not null)
            {
                ApplyAction(action);
            }

            Title = BuildTitle(actionName);
            Console.WriteLine(
                $"Pointer ({position.X}, {position.Y}) -> root: {(point is null ? "<outside>" : $"{point.Value.X}, {point.Value.Y}")} -> action: {actionName}, count: {_state.Count}, email: {OnOff(_state.EmailUpdates)}, notifications: {OnOff(_state.Notifications)}");
        }

        private void ApplyAction(UiAction action)
        {
            var next = DemoStateDispatch.Dispatch(_state, action.Id);
            if (!ReferenceEquals(next, _state))
            {
                _state = next;
                RenderCurrentState();
            }
        }

        private string BuildTitle(string actionName)
        {
            return $"{BaseTitle} - action: {actionName}, count: {_state.Count}, email: {OnOff(_state.EmailUpdates)}, notifications: {OnOff(_state.Notifications)}";
        }

        private static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }
    }
}
