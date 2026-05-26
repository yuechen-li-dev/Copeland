using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Runtime.Dispatch;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
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

        private static readonly DispatchTable<DemoState> DemoDispatch =
            DispatchTable.For<DemoState>()
                .Increment(
                    eventName: "counter.increment",
                    get: state => state.Count,
                    set: (state, value) => state with { Count = value })
                .Toggle(
                    eventName: "settings.emailUpdates.toggle",
                    get: state => state.EmailUpdates,
                    set: (state, value) => state with { EmailUpdates = value })
                .Toggle(
                    eventName: "settings.notifications.toggle",
                    get: state => state.Notifications,
                    set: (state, value) => state with { Notifications = value });

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

        private static UiNode BuildUi(DemoState state)
        {
            var emailStateText = state.EmailUpdates ? "on" : "off";
            var notificationsStateText = state.Notifications ? "on" : "off";

            return UI.Rect(
                id: "surface",
                width: 640,
                height: 360,
                style: new UiStyle(
                    Background: ColorToken.Hex(0xEDEDF0FF),
                    Foreground: ColorToken.Hex(0x09090BFF),
                    Padding: 0),
                child: UI.Column(
                    id: "surface-layout",
                    children:
                    [
                        UI.VSpace(24, id: "surface-top-gap"),
                        UI.Row(
                            id: "surface-left-offset",
                            children:
                            [
                                UI.HSpace(72, id: "surface-left-gap"),
                                StandardUI.Card(
                                    id: "settings-card",
                                    width: 500,
                                    height: 292,
                                    child: UI.Column(
                                        id: "content",
                                        gap: 10,
                                        children:
                                        [
                                            UI.Text(
                                                "Machina Presenter",
                                                id: "title",
                                                color: ColorToken.Hex(0x18181BFF),
                                                size: TextSize.Md),
                                            UI.Text(
                                                $"Count: {state.Count}",
                                                id: "count",
                                                color: ColorToken.Hex(0x52525BFF),
                                                size: TextSize.Sm),
                                            StandardUI.Button(
                                                "Increment",
                                                id: "increment",
                                                action: UiAction.Named("counter.increment")),
                                            StandardUI.Separator(id: "rule"),
                                            StandardUI.Checkbox(
                                                id: "email-updates",
                                                label: $"Email updates: {emailStateText}",
                                                isChecked: state.EmailUpdates,
                                                changed: UiAction.Named("settings.emailUpdates.toggle")),
                                            StandardUI.Switch(
                                                id: "notifications",
                                                label: $"Notifications: {notificationsStateText}",
                                                isOn: state.Notifications,
                                                changed: UiAction.Named("settings.notifications.toggle")),
                                            UI.Text(
                                                "Deterministic sample UI",
                                                id: "footnote",
                                                color: ColorToken.Hex(0x71717AFF),
                                                size: TextSize.Sm),
                                        ])),
                            ]),
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
            var next = DemoDispatch.Dispatch(_state, action.Name);
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
