using Avalonia.Input;
using Machina.Runtime.Input;
using RuntimePointerPoint = Machina.Runtime.Input.PointerPoint;

namespace Machina.Presenter.Sample;

public sealed class AvaloniaPresenterInputBackend
{
    public const string BackendName = "Avalonia";

    public UiInputEvent TranslatePointerPressed(PointerPointProperties properties, RuntimePointerPoint position)
    {
        return new UiPointerButtonChanged(
            position,
            TranslateButton(properties),
            IsPressed: true,
            UiModifiers.None);
    }

    public UiInputEvent TranslateWheel(PointerWheelEventArgs args, RuntimePointerPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new UiPointerWheel(
            position,
            DeltaX: args.Delta.X,
            DeltaY: args.Delta.Y,
            UiModifiers.None);
    }

    public UiInputEvent TranslatePointerMoved(PointerEventArgs args, RuntimePointerPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new UiPointerMoved(
            position,
            PreviousPosition: null,
            UiModifiers.None);
    }

    public UiInputEvent TranslatePointerReleased(PointerReleasedEventArgs args, RuntimePointerPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new UiPointerButtonChanged(
            position,
            TranslateButton(args.InitialPressMouseButton),
            IsPressed: false,
            UiModifiers.None);
    }

    public UiInputEvent TranslateKeyDown(KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateKeyEvent(isPressed: true, args.Key, args.KeyModifiers, isRepeat: false);
    }

    public UiInputEvent TranslateKeyDown(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None,
        bool isRepeat = false)
    {
        return TranslateKeyEvent(isPressed: true, key, modifiers, isRepeat);
    }

    public UiInputEvent TranslateKeyUp(KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateKeyEvent(isPressed: false, args.Key, args.KeyModifiers, isRepeat: false);
    }

    public UiInputEvent TranslateKeyUp(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        return TranslateKeyEvent(isPressed: false, key, modifiers, isRepeat: false);
    }

    public UiInputEvent TranslateTextInput(TextInputEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return TranslateTextInput(args.Text);
    }

    public UiInputEvent TranslateTextInput(string? text)
    {
        return new UiTextEntered(text
            ?? throw new ArgumentException("Text input must not be null.", nameof(text)));
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

    private static UiInputEvent TranslateKeyEvent(
        bool isPressed,
        Key key,
        KeyModifiers modifiers,
        bool isRepeat)
    {
        return new UiKeyChanged(
            TranslateKey(key),
            isPressed,
            isRepeat,
            TranslateModifiers(modifiers));
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
