using Machina.Runtime.Input;

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
    PresenterKeyboardInput? Keyboard = null,
    UiInputEvent? FoundationalEvent = null)
{
    /// <summary>
    /// Temporary sample compatibility adapter. Routing continues to consume the
    /// presenter model while live and playback input enter through the same
    /// backend-neutral contract.
    /// </summary>
    public UiInputEvent ToFoundationalEvent()
    {
        if (FoundationalEvent is not null)
        {
            return FoundationalEvent;
        }

        return Kind switch
        {
            PresenterInputKind.PointerPressed => new UiPointerButtonChanged(
                ToPointerPoint(Position),
                ToPointerButton(Button),
                IsPressed: true,
                UiModifiers.None),
            PresenterInputKind.PointerReleased => new UiPointerButtonChanged(
                ToPointerPoint(Position),
                ToPointerButton(Button),
                IsPressed: false,
                UiModifiers.None),
            PresenterInputKind.PointerMoved => new UiPointerMoved(
                ToPointerPoint(Position),
                PreviousPosition: null,
                UiModifiers.None),
            PresenterInputKind.Wheel => new UiPointerWheel(
                ToPointerPoint(Position),
                DeltaX: 0,
                DeltaY: WheelDeltaY,
                UiModifiers.None),
            PresenterInputKind.KeyDown => ToKeyChanged(isPressed: true),
            PresenterInputKind.KeyUp => ToKeyChanged(isPressed: false),
            PresenterInputKind.TextInput => new UiTextEntered(Keyboard?.Text
                ?? throw new InvalidOperationException("Text input requires non-empty text.")),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported presenter input kind."),
        };
    }

    public static PresenterInputEvent FromFoundationalEvent(UiInputEvent inputEvent, string? backendName = null)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);

        return inputEvent switch
        {
            UiPointerMoved moved => new PresenterInputEvent(
                PresenterInputKind.PointerMoved,
                ToPresenterPoint(moved.Position),
                BackendName: backendName,
                FoundationalEvent: inputEvent),
            UiPointerButtonChanged button => new PresenterInputEvent(
                button.IsPressed ? PresenterInputKind.PointerPressed : PresenterInputKind.PointerReleased,
                ToPresenterPoint(button.Position),
                ToPresenterButton(button.Button),
                BackendName: backendName,
                FoundationalEvent: inputEvent),
            UiPointerWheel wheel => new PresenterInputEvent(
                PresenterInputKind.Wheel,
                ToPresenterPoint(wheel.Position),
                WheelDeltaY: checked((float)wheel.DeltaY),
                BackendName: backendName,
                FoundationalEvent: inputEvent),
            UiKeyChanged key => new PresenterInputEvent(
                key.IsPressed ? PresenterInputKind.KeyDown : PresenterInputKind.KeyUp,
                default,
                BackendName: backendName,
                Keyboard: new PresenterKeyboardInput(
                    ToPresenterKey(key.Key),
                    Text: null,
                    ToPresenterModifiers(key.Modifiers),
                    key.IsRepeat),
                FoundationalEvent: inputEvent),
            UiTextEntered text => new PresenterInputEvent(
                PresenterInputKind.TextInput,
                default,
                BackendName: backendName,
                Keyboard: new PresenterKeyboardInput(
                    PresenterKey.Unknown,
                    text.Text,
                    PresenterKeyModifiers.None,
                    IsRepeat: false),
                FoundationalEvent: inputEvent),
            _ => throw new ArgumentOutOfRangeException(nameof(inputEvent), inputEvent, "Unsupported foundational input event."),
        };
    }

    private UiKeyChanged ToKeyChanged(bool isPressed)
    {
        PresenterKeyboardInput keyboard = Keyboard
            ?? throw new InvalidOperationException("Keyboard input requires keyboard details.");
        return new UiKeyChanged(
            ToUiKey(keyboard.Key),
            isPressed,
            keyboard.IsRepeat,
            new UiModifiers(keyboard.Modifiers.Ctrl, keyboard.Modifiers.Shift, keyboard.Modifiers.Alt, keyboard.Modifiers.Meta));
    }

    private static PointerPoint ToPointerPoint(PresenterInputPoint point) => new(point.X, point.Y);

    private static PresenterInputPoint ToPresenterPoint(PointerPoint point) => new((float)point.X, (float)point.Y);

    private static UiPointerButton ToPointerButton(PresenterInputButton button) => button switch
    {
        PresenterInputButton.Primary => UiPointerButton.Primary,
        PresenterInputButton.Secondary => UiPointerButton.Secondary,
        PresenterInputButton.Middle => UiPointerButton.Middle,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Pointer button transitions require a button."),
    };

    private static PresenterInputButton ToPresenterButton(UiPointerButton button) => button switch
    {
        UiPointerButton.Primary => PresenterInputButton.Primary,
        UiPointerButton.Secondary => PresenterInputButton.Secondary,
        UiPointerButton.Middle => PresenterInputButton.Middle,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported pointer button."),
    };

    private static UiKey ToUiKey(PresenterKey key) => Enum.IsDefined(key) ? (UiKey)key : UiKey.Unknown;

    private static PresenterKey ToPresenterKey(UiKey key) => Enum.IsDefined(key) ? (PresenterKey)key : PresenterKey.Unknown;

    private static PresenterKeyModifiers ToPresenterModifiers(UiModifiers modifiers) => new(
        modifiers.Control,
        modifiers.Shift,
        modifiers.Alt,
        modifiers.Meta);
}
