using System.Collections.Immutable;

namespace Machina.Runtime.Input;

/// <summary>
/// Device-oriented input observed during one host iteration. These records are
/// deliberately independent of a windowing toolkit and contain no UI policy.
/// </summary>
public abstract record UiInputEvent;

public enum UiPointerButton
{
    Primary,
    Secondary,
    Middle,
}

public enum UiKey
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

public readonly record struct UiModifiers(bool Control, bool Shift, bool Alt, bool Meta)
{
    public static UiModifiers None { get; } = new(false, false, false, false);
}

public readonly record struct UiSurfaceSize
{
    public UiSurfaceSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Surface width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Surface height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}

public sealed record UiPointerMoved(
    PointerPoint Position,
    PointerPoint? PreviousPosition,
    UiModifiers Modifiers) : UiInputEvent;

public sealed record UiPointerButtonChanged(
    PointerPoint Position,
    UiPointerButton Button,
    bool IsPressed,
    UiModifiers Modifiers) : UiInputEvent;

/// <summary>
/// Wheel deltas use the platform-independent convention that positive Y moves
/// content toward its origin (the existing presenter subtracts the delta from
/// its scroll offset). X is retained without assigning horizontal scroll policy.
/// </summary>
public sealed record UiPointerWheel(
    PointerPoint Position,
    double DeltaX,
    double DeltaY,
    UiModifiers Modifiers) : UiInputEvent;

public sealed record UiKeyChanged(
    UiKey Key,
    bool IsPressed,
    bool IsRepeat,
    UiModifiers Modifiers) : UiInputEvent;

public sealed record UiTextEntered : UiInputEvent
{
    public UiTextEntered(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text input must not be empty.", nameof(text));
        }

        Text = text;
    }

    public string Text { get; }
}

public sealed record UiSurfaceResized(UiSurfaceSize Size) : UiInputEvent;

public sealed record UiCloseRequested : UiInputEvent;

/// <summary>
/// Immutable callback-order-preserving input for one host iteration. Batch IDs
/// are assigned by the integration host; no global sequence source is used.
/// </summary>
public sealed record UiInputBatch
{
    public UiInputBatch(ulong batchId, IEnumerable<UiInputEvent>? events = null)
    {
        BatchId = batchId;
        Events = events is null
            ? ImmutableArray<UiInputEvent>.Empty
            : ImmutableArray.CreateRange(events);

        for (int index = 0; index < Events.Length; index++)
        {
            UiInputEvent inputEvent = Events[index]
                ?? throw new ArgumentException($"Input event at index {index} is null.", nameof(events));
            Validate(inputEvent, index);
        }
    }

    public ulong BatchId { get; }

    public ImmutableArray<UiInputEvent> Events { get; }

    public static UiInputBatch Empty(ulong batchId) => new(batchId);

    private static void Validate(UiInputEvent inputEvent, int index)
    {
        switch (inputEvent)
        {
            case UiPointerMoved moved:
                ValidatePoint(moved.Position, index);
                if (moved.PreviousPosition is PointerPoint previousPosition)
                {
                    ValidatePoint(previousPosition, index);
                }

                break;
            case UiPointerButtonChanged button:
                ValidatePoint(button.Position, index);
                break;
            case UiPointerWheel wheel:
                ValidatePoint(wheel.Position, index);
                ValidateFinite(wheel.DeltaX, index, "wheel X");
                ValidateFinite(wheel.DeltaY, index, "wheel Y");
                break;
        }
    }

    private static void ValidatePoint(PointerPoint point, int index)
    {
        ValidateFinite(point.X, index, "pointer X");
        ValidateFinite(point.Y, index, "pointer Y");
    }

    private static void ValidateFinite(double value, int index, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException($"Input event at index {index} has a non-finite {name} value.");
        }
    }
}
