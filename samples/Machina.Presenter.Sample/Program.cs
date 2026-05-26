using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Runtime.Dispatch;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Layout.Frames;
using RuntimePointerPoint = Machina.Runtime.Input.PointerPoint;

namespace Machina.Presenter.Sample;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }

    private sealed class App : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new PresenterWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class PresenterWindow : Window
    {
        private const string BaseTitle = "Machina Presenter M1e";

        private static class Actions
        {
            public static readonly UiActionId Increment = new("counter.increment");
            public static readonly UiActionId ToggleEmailUpdates = new("settings.emailUpdates.toggle");
            public static readonly UiActionId ToggleNotifications = new("settings.notifications.toggle");
        }

        private static readonly DispatchTable<DemoState> DemoDispatch =
            DispatchTable.Create<DemoState>(
                [
                    DispatchTransitions.Increment<DemoState>(
                    Actions.Increment,
                    get: state => state.Count,
                    set: (state, value) => state with { Count = value }),
                    DispatchTransitions.Toggle<DemoState>(
                    Actions.ToggleEmailUpdates,
                    get: state => state.EmailUpdates,
                    set: (state, value) => state with { EmailUpdates = value }),
                    DispatchTransitions.Toggle<DemoState>(
                    Actions.ToggleNotifications,
                    get: state => state.Notifications,
                    set: (state, value) => state with { Notifications = value }),
                ]);

        private readonly Image _image;

        private DemoState _state;
        private readonly MachinaRasterPipeline _pipeline;
        private UiHitTestIndex _hitTestIndex;
        private MachinaFrame _currentFrame;

        public PresenterWindow()
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
            _hitTestIndex = default!;
            _currentFrame = default!;

            RenderCurrentState();
            Title = BuildTitle("startup");

            CanResize = false;
            Content = _image;
        }

        private void RenderCurrentState()
        {
            const int width = 640;
            const int height = 360;

            var ui = BuildUi(_state);
            _currentFrame = _pipeline.Render(ui, width, height);
            _hitTestIndex = _currentFrame.HitTest;

            _image.Source = ToBitmap(_currentFrame.RasterFrame);
            _image.Width = _currentFrame.RasterFrame.Width;
            _image.Height = _currentFrame.RasterFrame.Height;
            Width = _currentFrame.RasterFrame.Width;
            Height = _currentFrame.RasterFrame.Height;
        }

        private static UiDocument BuildUi(DemoState state)
        {
            return UiDocument.Create(
                rows:
                [
                    Row.Root(
                        id: "root",
                        view: View.Rect(background: ColorToken.Hex(0xEDEDF0FF), foreground: ColorToken.Hex(0x09090BFF))),
                    Row.Anchor(
                        id: "settings-card",
                        parent: "root",
                        left: 72,
                        top: 24,
                        width: 500,
                        height: 292,
                        component: SettingsCard(state))
                ]);
        }

        private static UiNode SettingsCard(DemoState state)
        {
            return StandardUI.Card(
                id: "settings-card-content",
                child: UI.Column(
                    id: "settings-card-column",
                    gap: 10,
                    children:
                    [
                        UI.Text("Machina Presenter", id: "title", size: TextSize.Md),
                        UI.Text($"Count: {state.Count}", id: "count", size: TextSize.Sm),
                        StandardUI.Button("Increment", id: "increment", action: Actions.Increment.ToAction()),
                        StandardUI.Separator(id: "rule"),
                        StandardUI.Checkbox(
                            id: "email-updates",
                            label: $"Email updates: {OnOff(state.EmailUpdates)}",
                            isChecked: state.EmailUpdates,
                            changed: Actions.ToggleEmailUpdates.ToAction()),
                        StandardUI.Switch(
                            id: "notifications",
                            label: $"Notifications: {OnOff(state.Notifications)}",
                            isOn: state.Notifications,
                            changed: Actions.ToggleNotifications.ToAction()),
                        UI.Text("Deterministic sample UI", id: "footnote", size: TextSize.Sm)
                    ]));
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
            var next = DemoDispatch.Dispatch(_state, action.Id);
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

    private sealed record DemoState(
        int Count,
        bool EmailUpdates,
        bool Notifications);

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
}
