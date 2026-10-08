using InputMan.Aurelian;
using Aurelian.Composition;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Aurelian.Beacon3D;

/// <summary>Owns native subscriptions; portable binding and input state remain in InputMan.</summary>
internal sealed class BeaconNativeInput : IDisposable
{
    private readonly IWindow window;
    private readonly AurelianInputAdapter adapter;
    private readonly SilkInputBridge bridge;
    private readonly Action? cancelPointer;
    private readonly IInputContext context;
    private readonly Action? focusLost;
    private bool focused = true;
    private bool gameplayCapture;
    private bool captured;
    private bool disposed;

    public BeaconNativeInput(IWindow window, IInputContext context, AurelianInputAdapter adapter, bool manageFocus,
        Action<LayerInputEvent>? routeInput = null, Action? cancelPointer = null, Action? focusLost = null)
    {
        this.window = window;
        this.adapter = adapter;
        this.cancelPointer = cancelPointer;
        this.context = context;
        this.focusLost = focusLost;
        bridge = new SilkInputBridge(context, adapter, input =>
        {
            routeInput?.Invoke(input);
            return new LayerInputRoutingResult(false, null, null, null, []);
        });
        if (manageFocus)
        {
            window.FocusChanged += OnFocusChanged;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        SetGameplayCapture(false);
        window.FocusChanged -= OnFocusChanged;
        bridge.Dispose();
    }

    private void OnFocusChanged(bool focused)
    {
        this.focused = focused;
        adapter.OnFocusChanged(focused);
        bridge.ResetPointerTracking();
        if (!focused)
        {
            cancelPointer?.Invoke();
            focusLost?.Invoke();
            gameplayCapture = false;
        }
        ApplyCapture();
    }

    public void SetGameplayCapture(bool playing)
    {
        gameplayCapture = playing;
        ApplyCapture();
    }

    private void ApplyCapture()
    {
        bool next = gameplayCapture && focused;
        if (captured == next)
        {
            return;
        }
        foreach (IMouse mouse in context.Mice)
        {
            CursorMode mode = CursorMode.Normal;
            if (next)
            {
                mode = mouse.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled;
                if (!mouse.Cursor.IsSupported(mode))
                {
                    throw new NotSupportedException("This input backend does not support captured mouse look.");
                }
            }
            mouse.Cursor.CursorMode = mode;
        }
        captured = next;
        bridge.ResetPointerTracking();
        adapter.ClearPointerDeltas();
    }
}
