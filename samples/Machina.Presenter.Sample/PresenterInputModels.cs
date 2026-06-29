namespace Machina.Presenter.Sample;

public enum PresenterInputKind
{
    PointerPressed,
    PointerReleased,
    PointerMoved,
    Wheel,
}

public enum PresenterInputButton
{
    None,
    Primary,
    Secondary,
    Middle,
}

public readonly record struct PresenterInputPoint(float X, float Y);

public sealed record PresenterInputEvent(
    PresenterInputKind Kind,
    PresenterInputPoint Position,
    PresenterInputButton Button = PresenterInputButton.None,
    float WheelDeltaY = 0,
    string? BackendName = null);
