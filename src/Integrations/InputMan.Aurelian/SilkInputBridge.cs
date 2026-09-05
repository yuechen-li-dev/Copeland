using System.Numerics;
using Aurelian.Composition;
using InputMan.Core;
using Silk.NET.Input;
using SilkButton = Silk.NET.Input.Button;
using SilkGamepad = Silk.NET.Input.IGamepad;
using SilkKey = Silk.NET.Input.Key;
using SilkKeyboard = Silk.NET.Input.IKeyboard;
using SilkMouse = Silk.NET.Input.IMouse;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace InputMan.Aurelian;

/// <summary>Owns Silk.NET subscriptions and translates native device events into portable InputMan controls.</summary>
public sealed class SilkInputBridge : IDisposable
{
    private readonly IInputContext context;
    private readonly AurelianInputAdapter adapter;
    private readonly Func<LayerInputEvent, LayerInputRoutingResult>? routeInput;
    private readonly Dictionary<int, Vector2> pointerPositions = [];
    private bool disposed;

    public SilkInputBridge(
        IInputContext context,
        AurelianInputAdapter adapter,
        Func<LayerInputEvent, LayerInputRoutingResult>? routeInput = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.routeInput = routeInput;
        context.ConnectionChanged += OnConnectionChanged;
        foreach (SilkKeyboard keyboard in context.Keyboards) Attach(keyboard);
        foreach (SilkMouse mouse in context.Mice) Attach(mouse);
        foreach (SilkGamepad gamepad in context.Gamepads) Attach(gamepad);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        context.ConnectionChanged -= OnConnectionChanged;
        foreach (SilkKeyboard keyboard in context.Keyboards) Detach(keyboard);
        foreach (SilkMouse mouse in context.Mice) Detach(mouse);
        foreach (SilkGamepad gamepad in context.Gamepads) Detach(gamepad);
        pointerPositions.Clear();
    }

    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        switch (device)
        {
            case SilkKeyboard keyboard:
                if (connected) Attach(keyboard); else Detach(keyboard);
                break;
            case SilkMouse mouse:
                if (connected) Attach(mouse); else Detach(mouse);
                break;
            case SilkGamepad gamepad:
                if (connected) Attach(gamepad); else Detach(gamepad);
                break;
        }
    }

    private void Attach(SilkKeyboard keyboard)
    {
        keyboard.KeyDown += OnKeyDown;
        keyboard.KeyUp += OnKeyUp;
    }

    private void Detach(SilkKeyboard keyboard)
    {
        keyboard.KeyDown -= OnKeyDown;
        keyboard.KeyUp -= OnKeyUp;
    }

    private void Attach(SilkMouse mouse)
    {
        pointerPositions[mouse.Index] = mouse.Position;
        mouse.MouseDown += OnMouseDown;
        mouse.MouseUp += OnMouseUp;
        mouse.MouseMove += OnMouseMove;
        mouse.Scroll += OnMouseScroll;
    }

    private void Detach(SilkMouse mouse)
    {
        mouse.MouseDown -= OnMouseDown;
        mouse.MouseUp -= OnMouseUp;
        mouse.MouseMove -= OnMouseMove;
        mouse.Scroll -= OnMouseScroll;
        pointerPositions.Remove(mouse.Index);
    }

    private void Attach(SilkGamepad gamepad)
    {
        byte index = CheckedIndex(gamepad.Index);
        adapter.ConnectGamepad(index);
        gamepad.ButtonDown += OnGamepadButtonDown;
        gamepad.ButtonUp += OnGamepadButtonUp;
        gamepad.ThumbstickMoved += OnThumbstickMoved;
        gamepad.TriggerMoved += OnTriggerMoved;
    }

    private void Detach(SilkGamepad gamepad)
    {
        gamepad.ButtonDown -= OnGamepadButtonDown;
        gamepad.ButtonUp -= OnGamepadButtonUp;
        gamepad.ThumbstickMoved -= OnThumbstickMoved;
        gamepad.TriggerMoved -= OnTriggerMoved;
        adapter.DisconnectGamepad(CheckedIndex(gamepad.Index));
    }

    private void OnKeyDown(SilkKeyboard keyboard, SilkKey key, int scanCode)
    {
        if (TryMapKey(key, out KeyboardKey mapped))
        {
            routeInput?.Invoke(adapter.ToLayerEvent(mapped, pressed: true));
            adapter.RecordButton(Controls.Key(mapped), true);
        }
    }

    private void OnKeyUp(SilkKeyboard keyboard, SilkKey key, int scanCode)
    {
        if (TryMapKey(key, out KeyboardKey mapped))
        {
            routeInput?.Invoke(adapter.ToLayerEvent(mapped, pressed: false));
            adapter.RecordButton(Controls.Key(mapped), false);
        }
    }

    private void OnMouseDown(SilkMouse mouse, SilkMouseButton button)
    {
        if (TryMapMouseButton(button, out InputMan.Core.MouseButton mapped))
        {
            routeInput?.Invoke(new LayerPointerButtonChanged(ToLayerPoint(mouse.Position), ToLayerButton(mapped), true));
            adapter.RecordButton(Controls.Mouse(mapped), true);
        }
    }

    private void OnMouseUp(SilkMouse mouse, SilkMouseButton button)
    {
        if (TryMapMouseButton(button, out InputMan.Core.MouseButton mapped))
        {
            routeInput?.Invoke(new LayerPointerButtonChanged(ToLayerPoint(mouse.Position), ToLayerButton(mapped), false));
            adapter.RecordButton(Controls.Mouse(mapped), false);
        }
    }

    private void OnMouseMove(SilkMouse mouse, Vector2 position)
    {
        Vector2 previous = pointerPositions.GetValueOrDefault(mouse.Index, position);
        pointerPositions[mouse.Index] = position;
        Vector2 delta = position - previous;
        routeInput?.Invoke(new LayerPointerMoved(ToLayerPoint(position), ToLayerPoint(previous)));
        adapter.RecordAxis(Controls.Mouse(MouseAxis.PositionX), position.X);
        adapter.RecordAxis(Controls.Mouse(MouseAxis.PositionY), position.Y);
        adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), delta.X);
        adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), delta.Y);
    }

    private void OnMouseScroll(SilkMouse mouse, ScrollWheel wheel)
    {
        routeInput?.Invoke(new LayerPointerWheel(ToLayerPoint(mouse.Position), wheel.X, wheel.Y));
        adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelX), wheel.X);
        adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), wheel.Y);
    }

    private void OnGamepadButtonDown(SilkGamepad gamepad, SilkButton button) => RecordGamepadButton(gamepad, button, true);
    private void OnGamepadButtonUp(SilkGamepad gamepad, SilkButton button) => RecordGamepadButton(gamepad, button, false);

    private void RecordGamepadButton(SilkGamepad gamepad, SilkButton button, bool down)
    {
        int portableCode = button.Index + 1;
        if (Enum.IsDefined(typeof(GamepadButton), portableCode))
        {
            adapter.RecordButton(Controls.Gamepad((GamepadButton)portableCode, CheckedIndex(gamepad.Index)), down);
        }
    }

    private void OnThumbstickMoved(SilkGamepad gamepad, Thumbstick stick)
    {
        byte index = CheckedIndex(gamepad.Index);
        if (stick.Index == 0)
        {
            adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftX, index), stick.X);
            adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY, index), stick.Y);
        }
        else if (stick.Index == 1)
        {
            adapter.RecordAxis(Controls.Gamepad(GamepadAxis.RightX, index), stick.X);
            adapter.RecordAxis(Controls.Gamepad(GamepadAxis.RightY, index), stick.Y);
        }
    }

    private void OnTriggerMoved(SilkGamepad gamepad, Trigger trigger)
    {
        GamepadAxis axis = trigger.Index == 0 ? GamepadAxis.LeftTrigger : GamepadAxis.RightTrigger;
        adapter.RecordAxis(Controls.Gamepad(axis, CheckedIndex(gamepad.Index)), trigger.Position);
    }

    private static bool TryMapMouseButton(SilkMouseButton button, out InputMan.Core.MouseButton mapped)
    {
        mapped = button switch
        {
            SilkMouseButton.Left => InputMan.Core.MouseButton.Primary,
            SilkMouseButton.Right => InputMan.Core.MouseButton.Secondary,
            SilkMouseButton.Middle => InputMan.Core.MouseButton.Middle,
            SilkMouseButton.Button4 => InputMan.Core.MouseButton.Back,
            SilkMouseButton.Button5 => InputMan.Core.MouseButton.Forward,
            _ => default,
        };
        return button is >= SilkMouseButton.Left and <= SilkMouseButton.Button5;
    }

    private static bool TryMapKey(SilkKey key, out KeyboardKey mapped)
    {
        mapped = key switch
        {
            SilkKey.A => KeyboardKey.A,
            SilkKey.B => KeyboardKey.B,
            SilkKey.C => KeyboardKey.C,
            SilkKey.D => KeyboardKey.D,
            SilkKey.E => KeyboardKey.E,
            SilkKey.F => KeyboardKey.F,
            SilkKey.I => KeyboardKey.I,
            SilkKey.N => KeyboardKey.N,
            SilkKey.Q => KeyboardKey.Q,
            SilkKey.S => KeyboardKey.S,
            SilkKey.W => KeyboardKey.W,
            SilkKey.Number1 => KeyboardKey.Number1,
            SilkKey.Number2 => KeyboardKey.Number2,
            SilkKey.Number3 => KeyboardKey.Number3,
            SilkKey.Number4 => KeyboardKey.Number4,
            SilkKey.Number5 => KeyboardKey.Number5,
            SilkKey.Number6 => KeyboardKey.Number6,
            SilkKey.Number7 => KeyboardKey.Number7,
            SilkKey.Number8 => KeyboardKey.Number8,
            SilkKey.Enter => KeyboardKey.Enter,
            SilkKey.Escape => KeyboardKey.Escape,
            SilkKey.Space => KeyboardKey.Space,
            SilkKey.Right => KeyboardKey.ArrowRight,
            SilkKey.Left => KeyboardKey.ArrowLeft,
            SilkKey.Down => KeyboardKey.ArrowDown,
            SilkKey.Up => KeyboardKey.ArrowUp,
            SilkKey.ControlLeft => KeyboardKey.LeftControl,
            SilkKey.ShiftLeft => KeyboardKey.LeftShift,
            SilkKey.AltLeft => KeyboardKey.LeftAlt,
            SilkKey.ControlRight => KeyboardKey.RightControl,
            SilkKey.ShiftRight => KeyboardKey.RightShift,
            SilkKey.AltRight => KeyboardKey.RightAlt,
            _ => KeyboardKey.Unknown,
        };
        return mapped != KeyboardKey.Unknown;
    }

    private static byte CheckedIndex(int index)
    {
        if (index is < byte.MinValue or > byte.MaxValue) throw new InvalidOperationException($"Silk device index {index} is outside InputMan's bounded range.");
        return (byte)index;
    }

    private static LayerPoint ToLayerPoint(Vector2 value) => new(value.X, value.Y);

    private static LayerPointerButton ToLayerButton(InputMan.Core.MouseButton button) => button switch
    {
        InputMan.Core.MouseButton.Primary => LayerPointerButton.Primary,
        InputMan.Core.MouseButton.Secondary => LayerPointerButton.Secondary,
        _ => LayerPointerButton.Middle,
    };
}
