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
        public static PresenterProgramOptions Current { get; private set; } = new(false, PresenterExportContract.DefaultOutputPath, new PresenterProofOptions(), PresenterNavigationExportOptions.Disabled);

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
        private PresenterNavigationState? _navigationState;
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

            _state = new DemoState(
                Count: 0,
                EmailUpdates: true,
                Notifications: false);
            _pipeline = new MachinaRasterPipeline();
            _proofOptions = proofOptions;
            _navigationOptions = navigationOptions;
            _navigationState = navigationOptions.IncludeNavigationShell
                ? PresenterExporterNavigationState()
                : null;
            _hitTestIndex = default!;
            _currentFrame = default!;
            _navigationShellRender = null;
            _baseTitle = navigationOptions.IncludeNavigationShell
                ? "Machina Presenter M10a"
                : "Machina Presenter M1e";

            RenderCurrentState();
            Title = BuildTitle("startup");

            CanResize = false;
            Content = _image;
        }

        private PresenterNavigationState PresenterExporterNavigationState()
        {
            PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
            PresenterNavigationState state = PresenterNavigationState.CreateDefault(model);

            if (!string.IsNullOrWhiteSpace(_navigationOptions.SelectedPageId) &&
                model.ContainsPage(_navigationOptions.SelectedPageId))
            {
                PresenterNavigationSection section = model.FindSectionByPageId(_navigationOptions.SelectedPageId);
                PresenterNavigationTab tab = model.FindTabByPageId(_navigationOptions.SelectedPageId);
                state = state
                    .WithSelectedTab(section.Id, tab.Id)
                    .WithSelectedSection(section.Id);
            }

            if (_navigationOptions.ScrollOffsetByPageId is not null)
            {
                foreach ((string pageId, double offset) in _navigationOptions.ScrollOffsetByPageId)
                {
                    state = state.WithScrollOffset(pageId, offset);
                }
            }

            return state;
        }

        private void RenderCurrentState()
        {
            if (_navigationOptions.IncludeNavigationShell)
            {
                _navigationShellRender = PresenterNavigationShellRenderer.Render(
                    _state,
                    _navigationState ?? PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel()),
                    AppTheme,
                    _proofOptions);
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
            var position = args.GetPosition(_image);
            var presentedPoint = new RuntimePointerPoint(position.X, position.Y);
            var destination = new PresentedImageRect(0, 0, _image.Bounds.Width, _image.Bounds.Height);
            RuntimePointerPoint? rootPoint = PresentedImageMapper.ToRootPoint(
                presentedPoint,
                _navigationShellRender?.ComposedFrame.Width ?? _currentFrame.RasterFrame.Width,
                _navigationShellRender?.ComposedFrame.Height ?? _currentFrame.RasterFrame.Height,
                destination,
                ImageStretchMode.None);

            var point = rootPoint;
            UiAction? action = null;

            if (point is not null)
            {
                var hit = _hitTestIndex.HitTest(point.Value);
                action = hit?.Action;

                if (action is null && _navigationOptions.IncludeNavigationShell && _navigationShellRender is not null)
                {
                    action = _navigationShellRender.HitTestContent(point.Value);
                }
            }

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
