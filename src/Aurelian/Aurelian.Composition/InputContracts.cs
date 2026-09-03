namespace Aurelian.Composition;

public abstract record LayerInputEvent;

public enum LayerPointerButton
{
    Primary,
    Secondary,
    Middle
}

public enum LayerKey
{
    Unknown,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Enter,
    Escape,
    Space,
    I,
    F,
    Q,
    N,
    Number1,
    Number2,
    Number3,
    Number4,
    Number5,
    Number6,
    Number7,
    Number8
}

public sealed record LayerPointerMoved(LayerPoint Position, LayerPoint? PreviousPosition = null) : LayerInputEvent;

public sealed record LayerPointerButtonChanged(
    LayerPoint Position,
    LayerPointerButton Button,
    bool IsPressed) : LayerInputEvent;

public sealed record LayerPointerWheel(LayerPoint Position, double DeltaX, double DeltaY) : LayerInputEvent;

public sealed record LayerKeyChanged(LayerKey Key, bool IsPressed, bool IsRepeat = false) : LayerInputEvent;

public sealed record LayerTextEntered(string Text) : LayerInputEvent;

public sealed record LayerInputResult(
    bool Consumed,
    bool RequestFocus = false,
    bool RequestCapture = false,
    bool ReleaseCapture = false)
{
    public static LayerInputResult Unconsumed { get; } = new(false);
    public static LayerInputResult ConsumedOnly { get; } = new(true);
}

public sealed record LayerInputRoutingResult(
    bool Consumed,
    LayerId? ConsumedBy,
    LayerId? FocusOwner,
    LayerId? CaptureOwner,
    IReadOnlyList<LayerId> VisitedLayers);
