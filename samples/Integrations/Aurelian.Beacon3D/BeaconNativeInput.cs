using InputMan.Aurelian;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Aurelian.Beacon3D;

/// <summary>Owns native subscriptions; portable binding and input state remain in InputMan.</summary>
internal sealed class BeaconNativeInput : IDisposable
{
    private readonly IWindow window;
    private readonly AurelianInputAdapter adapter;
    private readonly SilkInputBridge bridge;
    private bool disposed;

    public BeaconNativeInput(IWindow window, IInputContext context, AurelianInputAdapter adapter, bool manageFocus)
    {
        this.window = window;
        this.adapter = adapter;
        bridge = new SilkInputBridge(context, adapter);
        if (manageFocus)
        {
            window.FocusChanged += adapter.OnFocusChanged;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        window.FocusChanged -= adapter.OnFocusChanged;
        bridge.Dispose();
    }
}
