using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;

namespace InputMan.Aurelian;

/// <summary>Native callback accumulator that produces one order-independent physical snapshot per host frame.</summary>
public sealed class AurelianInputAdapter : IAurelianHostInput
{
    private readonly InputManEngine engine;
    private readonly Dictionary<ControlKey, bool> buttons = [];
    private readonly Dictionary<ControlKey, float> axes = [];
    private readonly HashSet<byte> connectedGamepads = [];
    private bool focused = true;
    private bool disposed;

    public AurelianInputAdapter(InputManEngine engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public InputFrame CurrentFrame => engine.CurrentFrame;
    public IReadOnlySet<byte> ConnectedGamepads => connectedGamepads;

    public void SetContexts(params ActionMapId[] activeMaps)
    {
        ThrowIfDisposed();
        engine.SetMaps(activeMaps);
    }

    public void RecordButton(ControlKey control, bool down)
    {
        ThrowIfDisposed();
        if (control.Device == DeviceKind.Gamepad && !connectedGamepads.Contains(control.DeviceIndex)) return;
        buttons[control] = down;
    }

    public void RecordAxis(ControlKey control, float value)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (control.Device == DeviceKind.Gamepad && !connectedGamepads.Contains(control.DeviceIndex)) return;
        axes[control] = value;
    }

    public void ConnectGamepad(byte deviceIndex)
    {
        ThrowIfDisposed();
        connectedGamepads.Add(deviceIndex);
    }

    public void DisconnectGamepad(byte deviceIndex)
    {
        ThrowIfDisposed();
        connectedGamepads.Remove(deviceIndex);
        RemoveDeviceState(DeviceKind.Gamepad, deviceIndex);
    }

    public void BeginFrame(AurelianHostFrame frame)
    {
        ThrowIfDisposed();
        InputSnapshot snapshot = focused
            ? new InputSnapshot(
                new Dictionary<ControlKey, bool>(buttons),
                new Dictionary<ControlKey, float>(axes))
            : InputSnapshot.Empty;
        engine.Tick(snapshot, (float)frame.Elapsed.TotalSeconds, (float)frame.Total.TotalSeconds);

        foreach (ControlKey delta in axes.Keys.Where(IsDeltaAxis).ToArray())
        {
            axes[delta] = 0f;
        }
    }

    public void OnFocusChanged(bool isFocused)
    {
        ThrowIfDisposed();
        if (focused && !isFocused)
        {
            buttons.Clear();
            axes.Clear();
            engine.ResetOnFocusLoss();
        }
        focused = isFocused;
    }

    public LayerInputEvent ToLayerEvent(KeyboardKey key, bool pressed, bool repeat = false)
    {
        return new LayerKeyChanged(ToLayerKey(key), pressed, repeat);
    }

    public void Dispose()
    {
        if (disposed) return;
        buttons.Clear();
        axes.Clear();
        connectedGamepads.Clear();
        disposed = true;
    }

    private void RemoveDeviceState(DeviceKind kind, byte index)
    {
        foreach (ControlKey control in buttons.Keys.Where(key => key.Device == kind && key.DeviceIndex == index).ToArray()) buttons.Remove(control);
        foreach (ControlKey control in axes.Keys.Where(key => key.Device == kind && key.DeviceIndex == index).ToArray()) axes.Remove(control);
    }

    private static bool IsDeltaAxis(ControlKey control)
    {
        return control.Device == DeviceKind.Mouse
            && control.Code is (int)MouseAxis.DeltaX or (int)MouseAxis.DeltaY or (int)MouseAxis.WheelX or (int)MouseAxis.WheelY;
    }

    private static LayerKey ToLayerKey(KeyboardKey key) => key switch
    {
        KeyboardKey.ArrowUp => LayerKey.ArrowUp,
        KeyboardKey.ArrowDown => LayerKey.ArrowDown,
        KeyboardKey.ArrowLeft => LayerKey.ArrowLeft,
        KeyboardKey.ArrowRight => LayerKey.ArrowRight,
        KeyboardKey.Enter => LayerKey.Enter,
        KeyboardKey.Escape => LayerKey.Escape,
        KeyboardKey.Space => LayerKey.Space,
        KeyboardKey.I => LayerKey.I,
        KeyboardKey.F => LayerKey.F,
        KeyboardKey.Q => LayerKey.Q,
        KeyboardKey.N => LayerKey.N,
        KeyboardKey.Number1 => LayerKey.Number1,
        KeyboardKey.Number2 => LayerKey.Number2,
        KeyboardKey.Number3 => LayerKey.Number3,
        KeyboardKey.Number4 => LayerKey.Number4,
        KeyboardKey.Number5 => LayerKey.Number5,
        KeyboardKey.Number6 => LayerKey.Number6,
        KeyboardKey.Number7 => LayerKey.Number7,
        KeyboardKey.Number8 => LayerKey.Number8,
        _ => LayerKey.Unknown,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
