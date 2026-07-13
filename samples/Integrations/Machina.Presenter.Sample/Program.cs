using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Aurelian.Machina;
using Machina.Core.Actions;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation.Input;
using Machina.Runtime.Input;
using Machina.Presenter.Sample.Playback;
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
            if (!string.IsNullOrWhiteSpace(options.PlaybackSuitePath))
            {
                PresenterPlaybackSuiteRunner suiteRunner = new(
                    new PresenterPlaybackRunner(
                        DemoState.Default,
                        AppTheme,
                        options.ProofOptions));
                PresenterPlaybackSuiteResult suiteResult = suiteRunner.RunSuitePath(
                    options.PlaybackSuitePath,
                    options.OutputDirectory);
                if (suiteResult.ScenarioResults.Any(result => !result.Passed))
                {
                    throw new InvalidOperationException(
                        $"Presenter playback suite '{suiteResult.Suite.Id}' failed one or more scenarios. See {suiteResult.ReportJsonPath}.");
                }

                Console.WriteLine(
                    $"Ran presenter playback suite '{suiteResult.Suite.Id}' into {suiteResult.OutputDirectory}; report: {suiteResult.ReportJsonPath}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.PlaybackScenarioPath))
            {
                PresenterPlaybackRunner playbackRunner = new(
                    DemoState.Default,
                    AppTheme,
                    options.ProofOptions);
                PresenterPlaybackOutputWriter.WriteMilestoneManifest(
                    Path.GetFullPath(Path.Combine("artifacts", "m16a")));
                PresenterPlaybackRunResult playbackResult = playbackRunner.RunScenarioFile(
                    options.PlaybackScenarioPath,
                    options.OutputPath);
                if (playbackResult.Trace.Assertions.Any(assertion => !assertion.Passed))
                {
                    throw new InvalidOperationException(
                        $"Presenter playback scenario '{playbackResult.Scenario.Id}' failed one or more assertions. See {playbackResult.OutputDirectory}.");
                }

                Console.WriteLine($"Ran presenter playback scenario '{playbackResult.Scenario.Id}' into {playbackResult.OutputDirectory}");
                return;
            }

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
        public static PresenterProgramOptions Current { get; private set; } = new(
            false,
            PresenterExportContract.DefaultOutputPath,
            Path.Combine("artifacts", "m16c", "playback"),
            new PresenterProofOptions(),
            PresenterNavigationExportOptions.DefaultShell,
            null,
            null);

        public static void Set(PresenterProgramOptions options)
        {
            Current = options;
        }
    }

    private sealed class PresenterWindow : Window
    {
        private readonly string _baseTitle;

        private readonly Image _image;
        private readonly Grid _presenterHost;

        private DemoState _state;
        private readonly PresenterProofOptions _proofOptions;
        private readonly PresenterNavigationExportOptions _navigationOptions;
        private readonly AvaloniaPresenterInputBackend _inputBackend;
        private readonly PresenterHostInputCollector _inputCollector;
        private readonly PresenterNavigationRenderSession _renderSession;
        private PresenterNavigationLayout _navigationLayout;
        private PresenterSurfaceSize _surfaceSize;
        private PresenterNavigationState? _navigationState;
        private PresenterScrollbarInteractionState _scrollbarInteractionState;
        private UiHitTestIndex _hitTestIndex;
        private MachinaComposedFrame _currentFrame;
        private PresenterNavigationShellRenderResult? _navigationShellRender;

        public PresenterWindow(PresenterProofOptions proofOptions, PresenterNavigationExportOptions navigationOptions)
        {
            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _presenterHost = new Grid
            {
                Background = new SolidColorBrush(Color.Parse("#CBD5E1")),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Children = { _image },
            };

            _image.PointerPressed += HandlePointerPressed;
            _image.PointerMoved += HandlePointerMoved;
            _image.PointerReleased += HandlePointerReleased;
            _image.PointerWheelChanged += HandlePointerWheelChanged;
            KeyDown += HandleKeyDown;
            KeyUp += HandleKeyUp;
            TextInput += HandleTextInput;

            _state = new DemoState(
                Count: 0,
                EmailUpdates: true,
                Notifications: false);
            _proofOptions = proofOptions;
            _navigationOptions = navigationOptions;
            _inputBackend = new AvaloniaPresenterInputBackend();
            _inputCollector = new PresenterHostInputCollector();
            _renderSession = new PresenterNavigationRenderSession();
            _surfaceSize = navigationOptions.RuntimeSizeExplicit
                ? PresenterSurfaceSize.Compute(navigationOptions.Width, navigationOptions.Height)
                : PresenterSurfaceSize.DefaultRuntime;
            _navigationLayout = CreateNavigationLayout(_surfaceSize.SurfaceWidth, _surfaceSize.SurfaceHeight);
            _navigationState = navigationOptions.IncludeNavigationShell
                ? PresenterExporterNavigationState()
                : null;
            _scrollbarInteractionState = PresenterScrollbarInteractionState.Default;
            _hitTestIndex = default!;
            _currentFrame = default!;
            _navigationShellRender = null;
            _baseTitle = navigationOptions.IncludeNavigationShell
                ? "Machina Presenter M15b"
                : "Machina Presenter M1e";

            Focusable = true;
            Opened += (_, _) =>
            {
                Focus();
                RefreshRuntimeSurface(forceRender: true);
            };
            SizeChanged += (_, _) => HandleSurfaceResized();
            Closing += (_, _) => HandleCloseRequested();

            RenderCurrentState();
            Title = BuildTitle("startup");

            Width = _surfaceSize.WindowWidth;
            Height = _surfaceSize.WindowHeight;
            MinWidth = PresenterSurfaceSize.MinimumSurfaceWidth;
            MinHeight = PresenterSurfaceSize.MinimumSurfaceHeight;
            CanResize = true;
            Content = _presenterHost;
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
                    _renderSession,
                    _navigationLayout);
                _navigationState = _navigationShellRender.NavigationState;
                _currentFrame = _navigationShellRender.ShellFrame;
                _hitTestIndex = _navigationShellRender.ShellFrame.HitTest;
                _image.Source = PresenterExporter.ToBitmap(_navigationShellRender.ComposedFrame);
                _image.Width = _navigationShellRender.ComposedFrame.Width;
                _image.Height = _navigationShellRender.ComposedFrame.Height;
                return;
            }

            var ui = SettingsScreen.Build(_state, AppTheme, _proofOptions);
            _currentFrame = MachinaAurelianCpuRasterComposition.Render(
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
        }

        private void HandlePointerPressed(object? sender, PointerPressedEventArgs args)
        {
            Point position = args.GetPosition(_image);
            UiInputEvent inputEvent = _inputBackend.TranslatePointerPressed(
                args.GetCurrentPoint(_image).Properties,
                new RuntimePointerPoint(position.X, position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerMoved(object? sender, PointerEventArgs args)
        {
            Point position = args.GetPosition(_image);
            UiInputEvent inputEvent = _inputBackend.TranslatePointerMoved(
                args,
                new RuntimePointerPoint(position.X, position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerReleased(object? sender, PointerReleasedEventArgs args)
        {
            Point position = args.GetPosition(_image);
            UiInputEvent inputEvent = _inputBackend.TranslatePointerReleased(
                args,
                new RuntimePointerPoint(position.X, position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs args)
        {
            Point position = args.GetPosition(_image);
            UiInputEvent inputEvent = _inputBackend.TranslateWheel(
                args,
                new RuntimePointerPoint(position.X, position.Y));
            ProcessInput(inputEvent, position.X, position.Y, args.Pointer);
        }

        private void HandleKeyDown(object? sender, KeyEventArgs args)
        {
            UiInputEvent inputEvent = _inputBackend.TranslateKeyDown(args);
            ProcessInput(inputEvent, double.NaN, double.NaN, pointer: null);
        }

        private void HandleKeyUp(object? sender, KeyEventArgs args)
        {
            UiInputEvent inputEvent = _inputBackend.TranslateKeyUp(args);
            ProcessInput(inputEvent, double.NaN, double.NaN, pointer: null);
        }

        private void HandleTextInput(object? sender, TextInputEventArgs args)
        {
            UiInputEvent inputEvent = _inputBackend.TranslateTextInput(args);
            ProcessInput(inputEvent, double.NaN, double.NaN, pointer: null);
        }

        private void ProcessInput(UiInputEvent inputEvent, double presentedX, double presentedY, IPointer? pointer)
        {
            UiAction? action = null;
            RuntimePointerPoint? point = null;

            if (_navigationOptions.IncludeNavigationShell &&
                _navigationShellRender is not null &&
                inputEvent is UiKeyChanged or UiTextEntered)
            {
                PresenterUiInputRoutingResult batchRouting = PresenterUiInputRouter.Route(
                    _navigationShellRender,
                    CreateInputBatch(inputEvent),
                    _scrollbarInteractionState);
                PresenterNavigationInputRoutingResult routed = batchRouting.RoutedEvents.Single();
                _scrollbarInteractionState = batchRouting.InteractionState;

                if (routed.ActionId is not null)
                {
                    action = new UiAction(routed.ActionId.Value);
                }
            }
            else
            {
                point = inputEvent.TryGetPointerPosition(out RuntimePointerPoint inputPosition)
                    ? MapToRootPoint(inputPosition)
                    : null;

                if (point is not null)
                {
                    if (_navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
                    {
                        UiInputEvent rootInput = inputEvent.WithPointerPosition(point.Value);
                        PresenterUiInputRoutingResult batchRouting = PresenterUiInputRouter.Route(
                            _navigationShellRender,
                            CreateInputBatch(rootInput),
                            _scrollbarInteractionState);
                        PresenterNavigationInputRoutingResult routed = batchRouting.RoutedEvents.Single();
                        UiActionId? routedActionId = routed.ActionId;
                        _scrollbarInteractionState = batchRouting.InteractionState;

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

                    if (action is null && inputEvent.IsPrimaryPressed())
                    {
                        UiHitTestResult? hit = _hitTestIndex.HitTest(point.Value);
                        action = hit?.Action;

                        if (action is null && _navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
                        {
                            action = _navigationShellRender.HitTestContent(point.Value);
                        }
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
                $"Input {inputEvent.GetType().Name} ({presentedX}, {presentedY}) -> root: {(point is null ? "<outside>" : $"{point.Value.X}, {point.Value.Y}")} -> action: {actionName}, count: {_state.Count}, email: {OnOff(_state.EmailUpdates)}, notifications: {OnOff(_state.Notifications)}");
        }

        private void HandleSurfaceResized()
        {
            if (!_navigationOptions.IncludeNavigationShell ||
                ClientSize.Width <= 0 ||
                ClientSize.Height <= 0 ||
                _navigationShellRender is null)
            {
                return;
            }

            int width = Math.Max(1, (int)Math.Round(ClientSize.Width));
            int height = Math.Max(1, (int)Math.Round(ClientSize.Height));
            var inputBatch = CreateInputBatch(
                new UiSurfaceResized(new UiSurfaceSize(width, height)));
            PresenterUiInputRoutingResult routed = PresenterUiInputRouter.Route(
                _navigationShellRender,
                inputBatch,
                _scrollbarInteractionState);
            _scrollbarInteractionState = routed.InteractionState;

            if (routed.RequiresRecomposition)
            {
                RefreshRuntimeSurface(forceRender: false);
            }
        }

        private void HandleCloseRequested()
        {
            UiInputBatch inputBatch = CreateInputBatch(new UiCloseRequested());
            MachinaFrontendInputRoutingResult frontendRouting = MachinaFrontendInputRouter.Route(inputBatch);

            foreach (MachinaFrontendCloseRequested closeMessage in frontendRouting.FrontendMessages
                         .OfType<MachinaFrontendCloseRequested>())
            {
                _ = AurelianHostInputTranslator.Translate(closeMessage);
            }
        }

        private RuntimePointerPoint? MapToRootPoint(RuntimePointerPoint position)
        {
            var destination = new PresentedImageRect(0, 0, _image.Bounds.Width, _image.Bounds.Height);

            return PresentedImageMapper.ToRootPoint(
                position,
                _navigationShellRender?.ComposedFrame.Width ?? _currentFrame.RasterFrame.Width,
                _navigationShellRender?.ComposedFrame.Height ?? _currentFrame.RasterFrame.Height,
                destination,
                ImageStretchMode.None);
        }

        private UiInputBatch CreateInputBatch(UiInputEvent inputEvent)
        {
            _inputCollector.Record(inputEvent);
            return _inputCollector.Publish();
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
                    _navigationLayout);

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

        private PresenterNavigationLayout CreateNavigationLayout(int width, int height)
        {
            PresenterShellMode shellMode = _navigationOptions.ShellMode
                ?? PresenterShellModeResolver.Resolve(width);
            return PresenterNavigationLayout.Create(width, height, shellMode);
        }

        private void RefreshRuntimeSurface(bool forceRender)
        {
            if (!_navigationOptions.IncludeNavigationShell)
            {
                return;
            }

            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            PresenterSurfaceSize nextSurface = PresenterSurfaceSize.ComputeFromClientSize(
                ClientSize.Width,
                ClientSize.Height);

            if (!forceRender &&
                nextSurface.SurfaceWidth == _surfaceSize.SurfaceWidth &&
                nextSurface.SurfaceHeight == _surfaceSize.SurfaceHeight)
            {
                return;
            }

            _surfaceSize = nextSurface;
            _navigationLayout = CreateNavigationLayout(nextSurface.SurfaceWidth, nextSurface.SurfaceHeight);
            RenderCurrentState();
            Title = BuildTitle("resize");
        }

        private static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }
    }
}
