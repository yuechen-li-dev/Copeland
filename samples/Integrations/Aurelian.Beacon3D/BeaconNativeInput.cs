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
    private bool disposed;

    public BeaconNativeInput(IWindow window, IInputContext context, AurelianInputAdapter adapter, bool manageFocus,
        Action<LayerInputEvent>? routeInput = null, Action? cancelPointer = null)
    {
        this.window = window;
        this.adapter = adapter;
        this.cancelPointer = cancelPointer;
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
        window.FocusChanged -= OnFocusChanged;
        bridge.Dispose();
    }

    private void OnFocusChanged(bool focused)
    {
        adapter.OnFocusChanged(focused);
        if (!focused)
        {
            cancelPointer?.Invoke();
        }
    }
}
