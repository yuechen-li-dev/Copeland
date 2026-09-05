using Aurelian.GameHost;

namespace Aurelian.NativeComposition;

/// <summary>Adapts the qualified world+Machina native compositor to the bounded game-host lifecycle.</summary>
public sealed class NativeGameHostCompositor(
    NativeLayerCompositor compositor,
    bool captureReadback = false) : IAurelianHostCompositor
{
    private bool disposed;

    public NativeLayerFrameResult? LastFrame { get; private set; }

    public void Resize(HostSurfaceSize size)
    {
        ThrowIfDisposed();
        compositor.Resize(size.Width, size.Height);
    }

    public void Present(AurelianHostFrame frame)
    {
        ThrowIfDisposed();
        LastFrame = compositor.RunFrame(frame.Sequence, frame.Elapsed, captureReadback);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        compositor.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
