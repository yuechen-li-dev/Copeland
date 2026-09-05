using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aurelian.GameHost.Silk;

/// <summary>Thin ownership adapter for a Silk native window already initialized by the bootstrap.</summary>
public sealed class SilkGameWindowAdapter : IAurelianGameWindow
{
    private readonly IWindow window;
    private bool focused;
    private bool disposed;

    public SilkGameWindowAdapter(IWindow window, bool initiallyFocused = true)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        focused = initiallyFocused;
        window.FramebufferResize += OnFramebufferResize;
        window.FocusChanged += OnFocusChanged;
    }

    public HostSurfaceSize SurfaceSize => new(window.FramebufferSize.X, window.FramebufferSize.Y);
    public bool IsFocused => focused;
    public bool ShouldClose => window.IsClosing;
    public event Action<HostSurfaceSize>? Resized;
    public event Action<bool>? FocusChanged;

    public void PumpEvents()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        window.DoEvents();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.FramebufferResize -= OnFramebufferResize;
        window.FocusChanged -= OnFocusChanged;
        window.Dispose();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0) Resized?.Invoke(new HostSurfaceSize(size.X, size.Y));
    }

    private void OnFocusChanged(bool value)
    {
        focused = value;
        FocusChanged?.Invoke(value);
    }
}
