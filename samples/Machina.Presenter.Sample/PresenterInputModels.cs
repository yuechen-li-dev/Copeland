namespace Machina.Presenter.Sample;

public enum PresenterInputKind
{
    PointerPressed,
    PointerReleased,
    PointerMoved,
    Wheel,
    KeyDown,
    KeyUp,
    TextInput,
}

public enum PresenterInputButton
{
    None,
    Primary,
    Secondary,
    Middle,
}

public enum PresenterKey
{
    Unknown,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    PageUp,
    PageDown,
    Home,
    End,
    Enter,
    Escape,
    Tab,
    Space,
    F,
    R,
    O,
    E,
}

public sealed record PresenterKeyModifiers(
    bool Ctrl,
    bool Shift,
    bool Alt,
    bool Meta)
{
    public static PresenterKeyModifiers None { get; } = new(false, false, false, false);
}

public sealed record PresenterKeyboardInput(
    PresenterKey Key,
    string? Text,
    PresenterKeyModifiers Modifiers,
    bool IsRepeat);

public readonly record struct PresenterInputPoint(float X, float Y);

public sealed record PresenterInputEvent(
    PresenterInputKind Kind,
    PresenterInputPoint Position,
    PresenterInputButton Button = PresenterInputButton.None,
    float WheelDeltaY = 0,
    string? BackendName = null,
    PresenterKeyboardInput? Keyboard = null);
