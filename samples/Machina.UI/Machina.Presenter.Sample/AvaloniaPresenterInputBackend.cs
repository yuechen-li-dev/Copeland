using Avalonia.Input;
using Machina.Runtime.Input;
using RuntimePointerPoint = Machina.Runtime.Input.PointerPoint;

namespace Machina.Presenter.Sample;

public sealed class AvaloniaPresenterInputBackend
{
    public const string BackendName = "Avalonia";

    public PresenterInputEvent TranslatePointerPressed(PointerPointProperties properties, PresenterInputPoint position)
    {
        UiInputEvent normalized = new UiPointerButtonChanged(
            new RuntimePointerPoint(position.X, position.Y),
            TranslateButton(properties),
            IsPressed: true,
            UiModifiers.None);
        return PresenterInputEvent.FromFoundationalEvent(normalized, BackendName);
    }

    public PresenterInputEvent TranslateWheel(PointerWheelEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        UiInputEvent normalized = new UiPointerWheel(
            new RuntimePointerPoint(position.X, position.Y),
            DeltaX: args.Delta.X,
            DeltaY: args.Delta.Y,
            UiModifiers.None);
        return PresenterInputEvent.FromFoundationalEvent(normalized, BackendName);
    }

    public PresenterInputEvent TranslatePointerMoved(PointerEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        UiInputEvent normalized = new UiPointerMoved(
            new RuntimePointerPoint(position.X, position.Y),
            PreviousPosition: null,
            UiModifiers.None);
        return PresenterInputEvent.FromFoundationalEvent(normalized, BackendName);
    }

    public PresenterInputEvent TranslatePointerReleased(PointerReleasedEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        UiInputEvent normalized = new UiPointerButtonChanged(
            new RuntimePointerPoint(position.X, position.Y),
            TranslateButton(args.InitialPressMouseButton),
            IsPressed: false,
            UiModifiers.None);
        return PresenterInputEvent.FromFoundationalEvent(normalized, BackendName);
    }

    public PresenterInputEvent TranslateKeyDown(KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateKeyEvent(PresenterInputKind.KeyDown, args.Key, args.KeyModifiers, isRepeat: false);
    }

    public PresenterInputEvent TranslateKeyDown(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None,
        bool isRepeat = false)
    {
        return TranslateKeyEvent(PresenterInputKind.KeyDown, key, modifiers, isRepeat);
    }

    public PresenterInputEvent TranslateKeyUp(KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateKeyEvent(PresenterInputKind.KeyUp, args.Key, args.KeyModifiers, isRepeat: false);
    }

    public PresenterInputEvent TranslateKeyUp(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        return TranslateKeyEvent(PresenterInputKind.KeyUp, key, modifiers, isRepeat: false);
    }

    public PresenterInputEvent TranslateTextInput(TextInputEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateTextInput(args.Text);
    }

    public PresenterInputEvent TranslateTextInput(string? text)
    {
        return PresenterInputEvent.FromFoundationalEvent(new UiTextEntered(text
            ?? throw new ArgumentException("Text input must not be null.", nameof(text))), BackendName);
    }

    private static UiPointerButton TranslateButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return UiPointerButton.Primary;
        }

        if (properties.IsRightButtonPressed)
        {
            return UiPointerButton.Secondary;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return UiPointerButton.Middle;
        }

        throw new ArgumentException("Pointer button input requires a pressed button.", nameof(properties));
    }

    private static UiPointerButton TranslateButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => UiPointerButton.Primary,
            MouseButton.Right => UiPointerButton.Secondary,
            MouseButton.Middle => UiPointerButton.Middle,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported pointer button."),
        };
    }

    private static PresenterInputEvent TranslateKeyEvent(
        PresenterInputKind kind,
        Key key,
        KeyModifiers modifiers,
        bool isRepeat)
    {
        UiInputEvent normalized = new UiKeyChanged(
            TranslateKey(key),
            kind == PresenterInputKind.KeyDown,
            isRepeat,
            TranslateModifiers(modifiers));
        return PresenterInputEvent.FromFoundationalEvent(normalized, BackendName);
    }

    private static UiKey TranslateKey(Key key)
    {
        return key switch
        {
            Key.Up => UiKey.ArrowUp,
            Key.Down => UiKey.ArrowDown,
            Key.Left => UiKey.ArrowLeft,
            Key.Right => UiKey.ArrowRight,
            Key.PageUp => UiKey.PageUp,
            Key.PageDown => UiKey.PageDown,
            Key.Home => UiKey.Home,
            Key.End => UiKey.End,
            Key.Enter => UiKey.Enter,
            Key.Escape => UiKey.Escape,
            Key.Tab => UiKey.Tab,
            Key.Space => UiKey.Space,
            Key.F => UiKey.F,
            Key.R => UiKey.R,
            Key.O => UiKey.O,
            Key.E => UiKey.E,
            _ => UiKey.Unknown,
        };
    }

    private static UiModifiers TranslateModifiers(KeyModifiers modifiers)
    {
        return new UiModifiers(
            Control: modifiers.HasFlag(KeyModifiers.Control),
            Shift: modifiers.HasFlag(KeyModifiers.Shift),
            Alt: modifiers.HasFlag(KeyModifiers.Alt),
            Meta: modifiers.HasFlag(KeyModifiers.Meta));
    }
}
