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
        ProgramOptionsHolder.Set(options);
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
                desktop.MainWindow = new PresenterWindow(_proofOptions, ProgramOptionsHolder.Current.NavigationOptions);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private static class ProgramOptionsHolder
    {
        public static PresenterProgramOptions Current { get; private set; } = new(false, PresenterExportContract.DefaultOutputPath, new PresenterProofOptions(), PresenterNavigationExportOptions.DefaultShell);

        public static void Set(PresenterProgramOptions options)
        {
            Current = options;
        }
    }

    private sealed class PresenterWindow : Window
    {
        private readonly string _baseTitle;

        private readonly Image _image;

        private DemoState _state;
        private readonly MachinaRasterPipeline _pipeline;
        private readonly PresenterProofOptions _proofOptions;
        private readonly PresenterNavigationExportOptions _navigationOptions;
        private readonly AvaloniaPresenterInputBackend _inputBackend;
        private readonly PresenterNavigationRenderSession _renderSession;
        private PresenterNavigationState? _navigationState;
        private PresenterScrollbarInteractionState _scrollbarInteractionState;
        private UiHitTestIndex _hitTestIndex;
        private MachinaFrame _currentFrame;
        private PresenterNavigationShellRenderResult? _navigationShellRender;

        public PresenterWindow(PresenterProofOptions proofOptions, PresenterNavigationExportOptions navigationOptions)
        {
            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            _image.PointerPressed += HandlePointerPressed;
            _image.PointerMoved += HandlePointerMoved;
            _image.PointerReleased += HandlePointerReleased;
            _image.PointerWheelChanged += HandlePointerWheelChanged;

            _state = new DemoState(
                Count: 0,
                EmailUpdates: true,
                Notifications: false);
            _pipeline = new MachinaRasterPipeline();
            _proofOptions = proofOptions;
            _navigationOptions = navigationOptions;
            _inputBackend = new AvaloniaPresenterInputBackend();
            _renderSession = new PresenterNavigationRenderSession();
            _navigationState = navigationOptions.IncludeNavigationShell
                ? PresenterExporterNavigationState()
                : null;
            _scrollbarInteractionState = PresenterScrollbarInteractionState.Default;
            _hitTestIndex = default!;
            _currentFrame = default!;
            _navigationShellRender = null;
            _baseTitle = navigationOptions.IncludeNavigationShell
                ? "Machina Presenter M10c"
                : "Machina Presenter M1e";

            RenderCurrentState();
            Title = BuildTitle("startup");

            CanResize = false;
            Content = _image;
        }

        private PresenterNavigationState PresenterExporterNavigationState()
        {
            PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
            return PresenterNavigationCatalog.CreateState(model, _proofOptions, _navigationOptions);
        }

        private void RenderCurrentState()
        {
            if (_navigationOptions.IncludeNavigationShell)
            {
                _navigationShellRender = PresenterNavigationShellRenderer.Render(
                    _state,
                    _navigationState ?? PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel()),
                    AppTheme,
                    _proofOptions,
                    _renderSession);
                _navigationState = _navigationShellRender.NavigationState;
                _currentFrame = _navigationShellRender.ShellFrame;
                _hitTestIndex = _navigationShellRender.ShellFrame.HitTest;
                _image.Source = PresenterExporter.ToBitmap(_navigationShellRender.ComposedFrame);
                _image.Width = _navigationShellRender.ComposedFrame.Width;
                _image.Height = _navigationShellRender.ComposedFrame.Height;
                Width = _navigationShellRender.ComposedFrame.Width;
                Height = _navigationShellRender.ComposedFrame.Height;
                return;
            }

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
            Point position = args.GetPosition(_image);
            PresenterInputEvent inputEvent = _inputBackend.TranslatePointerPressed(
                args.GetCurrentPoint(_image).Properties,
                new PresenterInputPoint((float)position.X, (float)position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerMoved(object? sender, PointerEventArgs args)
        {
            Point position = args.GetPosition(_image);
            PresenterInputEvent inputEvent = _inputBackend.TranslatePointerMoved(
                args,
                new PresenterInputPoint((float)position.X, (float)position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerReleased(object? sender, PointerReleasedEventArgs args)
        {
            Point position = args.GetPosition(_image);
            PresenterInputEvent inputEvent = _inputBackend.TranslatePointerReleased(
                args,
                new PresenterInputPoint((float)position.X, (float)position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs args)
        {
            Point position = args.GetPosition(_image);
            PresenterInputEvent inputEvent = _inputBackend.TranslateWheel(
                args,
                new PresenterInputPoint((float)position.X, (float)position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void ProcessInput(PresenterInputEvent inputEvent, double presentedX, double presentedY, IPointer? pointer)
        {
            RuntimePointerPoint? point = MapToRootPoint(inputEvent.Position);
            UiAction? action = null;

            if (point is not null)
            {
                if (_navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
                {
                    PresenterInputEvent rootInput = inputEvent with
                    {
                        Position = new PresenterInputPoint((float)point.Value.X, (float)point.Value.Y),
                    };

                    PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
                        _navigationShellRender,
                        rootInput,
                        _scrollbarInteractionState);
                    UiActionId? routedActionId = routed.ActionId;
                    _scrollbarInteractionState = routed.InteractionState;

                    if (pointer is not null)
                    {
                        if (routed.PointerCaptureRequest == PresenterPointerCaptureRequest.Capture)
                        {
                            pointer.Capture(_image);
                        }

                        if (routed.PointerCaptureRequest == PresenterPointerCaptureRequest.Release)
                        {
                            pointer.Capture(null);
                        }
                    }

                    if (routedActionId is not null)
                    {
                        action = new UiAction(routedActionId.Value);
                    }
                }

                if (action is null && inputEvent.Kind == PresenterInputKind.PointerPressed)
                {
                    UiHitTestResult? hit = _hitTestIndex.HitTest(point.Value);
                    action = hit?.Action;

                    if (action is null && _navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
                    {
                        action = _navigationShellRender.HitTestContent(point.Value);
                    }
                }
            }

            string actionName = action?.Name ?? "<none>";

            if (action is not null)
            {
                ApplyAction(action);
            }

            Title = BuildTitle(actionName);
            Console.WriteLine(
                $"Input {inputEvent.Kind} ({presentedX}, {presentedY}) -> root: {(point is null ? "<outside>" : $"{point.Value.X}, {point.Value.Y}")} -> action: {actionName}, count: {_state.Count}, email: {OnOff(_state.EmailUpdates)}, notifications: {OnOff(_state.Notifications)}");
        }

        private RuntimePointerPoint? MapToRootPoint(PresenterInputPoint position)
        {
            var presentedPoint = new RuntimePointerPoint(position.X, position.Y);
            var destination = new PresentedImageRect(0, 0, _image.Bounds.Width, _image.Bounds.Height);

            return PresentedImageMapper.ToRootPoint(
                presentedPoint,
                _navigationShellRender?.ComposedFrame.Width ?? _currentFrame.RasterFrame.Width,
                _navigationShellRender?.ComposedFrame.Height ?? _currentFrame.RasterFrame.Height,
                destination,
                ImageStretchMode.None);
        }

        private void ApplyAction(UiAction action)
        {
            if (_navigationOptions.IncludeNavigationShell && _navigationState is not null)
            {
                PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
                PresenterNavigationState nextNavigation = PresenterNavigationDispatch.Dispatch(
                    _navigationState,
                    action.Id,
                    model,
                    _proofOptions,
                    PresenterNavigationLayout.Default);

                if (!ReferenceEquals(nextNavigation, _navigationState) &&
                    !Equals(nextNavigation, _navigationState))
                {
                    _navigationState = nextNavigation;
                    RenderCurrentState();
                    return;
                }
            }

            var next = DemoStateDispatch.Dispatch(_state, action.Id);
            if (!ReferenceEquals(next, _state))
            {
                _state = next;
                RenderCurrentState();
            }
        }

        private string BuildTitle(string actionName)
        {
            if (_navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
            {
                return $"{_baseTitle} - section: {_navigationShellRender.SelectedSection.Label}, tab: {_navigationShellRender.SelectedTab.Label}, action: {actionName}, count: {_state.Count}";
            }

            return $"{_baseTitle} - action: {actionName}, count: {_state.Count}, email: {OnOff(_state.EmailUpdates)}, notifications: {OnOff(_state.Notifications)}";
        }

        private static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }
    }
}
