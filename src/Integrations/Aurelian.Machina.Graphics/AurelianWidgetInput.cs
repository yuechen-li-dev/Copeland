using System.Runtime.InteropServices;
using Machina.Runtime.Input;
using Machina.Standard.Widgets;
using Machina.Core.Semantics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aurelian.Machina;

/// <summary>
/// Native event transport for a widget host. Callbacks only queue bounded input;
/// preparation, application commands and Vulkan work remain on the event loop.
/// Windows pointer capture is owned and released by this instance.
/// </summary>
public sealed class AurelianWidgetInput : IDisposable, IWidgetClipboard
{
    public const int MaximumQueuedEvents = 4096;
    private readonly IWindow window;
    private readonly IInputContext devices;
    private readonly List<UiInputEvent> events = [];
    private readonly HashSet<Key> held = [];
    private ulong batchId;
    private char? pendingSurrogate;
    private bool captured;
    private bool disposed;
    private PointerPoint? observedPointer;
    private readonly AurelianCursor cursor;
    public UiCursor Cursor => cursor.Current;
    public int CursorChanges => cursor.Changes;
    public StandardCursor? NativeCursorShape => cursor.NativeShape;
    public void SetCursor(UiCursor intent) => cursor.Set(intent);

    public int CallbackFailures { get; private set; }
    public int DroppedBatches { get; private set; }
    public Exception? LastError { get; private set; }

    public AurelianWidgetInput(IWindow window)
    {
        this.window = window;
        devices = window.CreateInput();
        cursor = new(devices);
        foreach (var keyboard in devices.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => Guard(() =>
            {
                bool repeat = !held.Add(key);
                Enqueue(new UiKeyChanged(MapKey(key), true, repeat, Modifiers()));
            });
            keyboard.KeyUp += (_, key, _) => Guard(() =>
            {
                held.Remove(key);
                Enqueue(new UiKeyChanged(MapKey(key), false, false, Modifiers()));
            });
            keyboard.KeyChar += (_, character) => Guard(() => Character(character));
        }
        foreach (var mouse in devices.Mice)
        {
            mouse.MouseMove += (_, position) => Guard(() => Enqueue(new UiPointerMoved(new(position.X, position.Y), null, Modifiers())));
            mouse.MouseDown += (source, button) => Guard(() => Pointer(source, button, true));
            mouse.MouseUp += (source, button) => Guard(() => Pointer(source, button, false));
            mouse.Scroll += (source, wheel) => Guard(() => Enqueue(new UiPointerWheel(
                new(source.Position.X, source.Position.Y), wheel.X, wheel.Y, Modifiers())));
        }
        window.FocusChanged += FocusChanged;
        window.Resize += Resized;
        window.Closing += Closing;
    }

    public UiInputBatch Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // Non-client movement does not produce GLFW client motion callbacks.
        // Sampling once per iteration clears hover when entering the title bar.
        if (devices.Mice.FirstOrDefault() is { } mouse)
        {
            var point = new PointerPoint(mouse.Position.X, mouse.Position.Y);
            if (observedPointer != point) Enqueue(new UiPointerMoved(point, observedPointer, Modifiers()));
        }
        if (captured && OperatingSystem.IsWindows() && GetCapture() != Handle)
        {
            captured = false;
            Enqueue(new UiPointerCaptureLost());
        }
        var batch = new UiInputBatch(batchId++, events);
        events.Clear();
        return batch;
    }

    public bool SetPointerCapture(bool capture)
    {
        if (capture == captured) return true;
        if (!OperatingSystem.IsWindows()) return !capture;
        if (Handle == 0) return false;
        if (capture)
        {
            SetCapture(Handle);
            captured = GetCapture() == Handle;
            if (!captured) Enqueue(new UiPointerCaptureLost());
            return captured;
        }
        if (GetCapture() == Handle) ReleaseCapture();
        captured = false;
        return true;
    }

    public bool TryRead(out string text)
    {
        text = "";
        if (disposed || devices.Keyboards.Count == 0) return false;
        try { text = devices.Keyboards[0].ClipboardText ?? ""; return true; }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { LastError = error; return false; }
    }

    public bool TryWrite(string text)
    {
        if (disposed || devices.Keyboards.Count == 0) return false;
        try { devices.Keyboards[0].ClipboardText = text; return true; }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { LastError = error; return false; }
    }

    private void Character(char character)
    {
        var modifiers = Modifiers();
        if (char.IsControl(character) || (modifiers.Control || modifiers.Meta) && !modifiers.Alt) return;
        if (char.IsHighSurrogate(character)) { pendingSurrogate = character; return; }
        string text = pendingSurrogate is { } high && char.IsLowSurrogate(character)
            ? new string([high, character]) : char.IsSurrogate(character) ? "\uFFFD" : character.ToString();
        pendingSurrogate = null;
        Enqueue(new UiTextEntered(text));
    }

    private void Pointer(IMouse mouse, MouseButton button, bool pressed)
    {
        UiPointerButton mapped = button switch
        {
            MouseButton.Left => UiPointerButton.Primary,
            MouseButton.Right => UiPointerButton.Secondary,
            _ => UiPointerButton.Middle
        };
        Enqueue(new UiPointerButtonChanged(new(mouse.Position.X, mouse.Position.Y), mapped, pressed, Modifiers()));
    }

    private void Enqueue(UiInputEvent input)
    {
        if (disposed) return;
        if (input is UiPointerMoved moved) observedPointer = moved.Position;
        if (input is UiPointerMoved && events.LastOrDefault() is UiPointerMoved)
        {
            events[^1] = input;
            return;
        }
        if (events.Count >= MaximumQueuedEvents)
        {
            events.Clear();
            held.Clear();
            pendingSurrogate = null;
            DroppedBatches++;
            events.Add(new UiFocusChanged(false));
            return;
        }
        events.Add(input);
    }

    private void FocusChanged(bool focused) => Guard(() =>
    {
        if (!focused) { held.Clear(); pendingSurrogate = null; SetPointerCapture(false); }
        Enqueue(new UiFocusChanged(focused));
    });

    private void Resized(Vector2D<int> size) => Guard(() =>
    {
        SetPointerCapture(false);
        Enqueue(size.X > 0 && size.Y > 0 ? new UiSurfaceResized(new(size.X, size.Y)) : new UiPointerCaptureLost());
    });

    private void Closing() => Guard(() =>
    {
        SetPointerCapture(false);
        Enqueue(new UiCloseRequested());
    });

    private UiModifiers Modifiers() => new(
        held.Contains(Key.ControlLeft) || held.Contains(Key.ControlRight),
        held.Contains(Key.ShiftLeft) || held.Contains(Key.ShiftRight),
        held.Contains(Key.AltLeft) || held.Contains(Key.AltRight),
        held.Contains(Key.SuperLeft) || held.Contains(Key.SuperRight));

    private void Guard(Action callback)
    {
        if (disposed) return;
        try { callback(); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            CallbackFailures++;
            LastError = error;
            held.Clear();
            events.Clear();
            events.Add(new UiFocusChanged(false));
        }
    }

    public static UiKey MapKey(Key key) => key switch
    {
        Key.Up => UiKey.ArrowUp, Key.Down => UiKey.ArrowDown,
        Key.Left => UiKey.ArrowLeft, Key.Right => UiKey.ArrowRight,
        Key.Home => UiKey.Home, Key.End => UiKey.End,
        Key.PageUp => UiKey.PageUp, Key.PageDown => UiKey.PageDown,
        Key.Enter => UiKey.Enter, Key.Escape => UiKey.Escape,
        Key.Tab => UiKey.Tab, Key.Space => UiKey.Space,
        Key.Backspace => UiKey.Backspace, Key.Delete => UiKey.Delete,
        Key.A => UiKey.A, Key.C => UiKey.C, Key.V => UiKey.V,
        Key.X => UiKey.X, Key.Z => UiKey.Z, Key.Y => UiKey.Y,
        Key.F => UiKey.F, Key.R => UiKey.R, Key.O => UiKey.O, Key.E => UiKey.E,
        _ => UiKey.Unknown
    };

    private nint Handle => window.Native?.Win32?.Hwnd ?? 0;

    public void Dispose()
    {
        if (disposed) return;
        SetPointerCapture(false);
        window.FocusChanged -= FocusChanged;
        window.Resize -= Resized;
        window.Closing -= Closing;
        cursor.Dispose();
        devices.Dispose();
        events.Clear();
        held.Clear();
        disposed = true;
    }

    [DllImport("user32.dll")] private static extern nint SetCapture(nint window);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint GetCapture();
}
