using Avalonia.Input;

namespace Machina.Presenter.Sample;

public sealed class AvaloniaPresenterInputBackend
{
    public const string BackendName = "Avalonia";

    public PresenterInputEvent TranslatePointerPressed(PointerPointProperties properties, PresenterInputPoint position)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerPressed,
            position,
            TranslateButton(properties),
            BackendName: BackendName);
    }

    public PresenterInputEvent TranslateWheel(PointerWheelEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new PresenterInputEvent(
            PresenterInputKind.Wheel,
            position,
            PresenterInputButton.None,
            (float)args.Delta.Y,
            BackendName);
    }

    private static PresenterInputButton TranslateButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return PresenterInputButton.Primary;
        }

        if (properties.IsRightButtonPressed)
        {
            return PresenterInputButton.Secondary;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return PresenterInputButton.Middle;
        }

        return PresenterInputButton.None;
    }
}
