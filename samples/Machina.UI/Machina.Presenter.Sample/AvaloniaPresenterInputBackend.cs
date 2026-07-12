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

    public PresenterInputEvent TranslatePointerMoved(PointerEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new PresenterInputEvent(
            PresenterInputKind.PointerMoved,
            position,
            TranslateButton(args.GetCurrentPoint(null).Properties),
            BackendName: BackendName);
    }

    public PresenterInputEvent TranslatePointerReleased(PointerReleasedEventArgs args, PresenterInputPoint position)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new PresenterInputEvent(
            PresenterInputKind.PointerReleased,
            position,
            PresenterInputButton.None,
            BackendName: BackendName);
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
        return new PresenterInputEvent(
            PresenterInputKind.TextInput,
            default,
            PresenterInputButton.None,
            BackendName: BackendName,
            Keyboard: new PresenterKeyboardInput(
                PresenterKey.Unknown,
                text,
                PresenterKeyModifiers.None,
                IsRepeat: false));
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

    private static PresenterInputEvent TranslateKeyEvent(
        PresenterInputKind kind,
        Key key,
        KeyModifiers modifiers,
        bool isRepeat)
    {
        return new PresenterInputEvent(
            kind,
            default,
            PresenterInputButton.None,
            BackendName: BackendName,
            Keyboard: new PresenterKeyboardInput(
                TranslateKey(key),
                Text: null,
                TranslateModifiers(modifiers),
                isRepeat));
    }

    private static PresenterKey TranslateKey(Key key)
    {
        return key switch
        {
            Key.Up => PresenterKey.ArrowUp,
            Key.Down => PresenterKey.ArrowDown,
            Key.Left => PresenterKey.ArrowLeft,
            Key.Right => PresenterKey.ArrowRight,
            Key.PageUp => PresenterKey.PageUp,
            Key.PageDown => PresenterKey.PageDown,
            Key.Home => PresenterKey.Home,
            Key.End => PresenterKey.End,
            Key.Enter => PresenterKey.Enter,
            Key.Escape => PresenterKey.Escape,
            Key.Tab => PresenterKey.Tab,
            Key.Space => PresenterKey.Space,
            Key.F => PresenterKey.F,
            Key.R => PresenterKey.R,
            Key.O => PresenterKey.O,
            Key.E => PresenterKey.E,
            _ => PresenterKey.Unknown,
        };
    }

    private static PresenterKeyModifiers TranslateModifiers(KeyModifiers modifiers)
    {
        return new PresenterKeyModifiers(
            Ctrl: modifiers.HasFlag(KeyModifiers.Control),
            Shift: modifiers.HasFlag(KeyModifiers.Shift),
            Alt: modifiers.HasFlag(KeyModifiers.Alt),
            Meta: modifiers.HasFlag(KeyModifiers.Meta));
    }
}
