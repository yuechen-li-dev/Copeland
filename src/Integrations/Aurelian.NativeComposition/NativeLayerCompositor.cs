using Aurelian.Composition;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.NativeComposition;

public interface INativeLayerPresenter
{
    LayerId Layer { get; }
    void Attach(VulkanNativeFrameTarget target);
    void Resize(VulkanNativeFrameTarget target);
    void Present(NativeLayerFrameContext context);
    void Detach();
}

public sealed class NativeLayerFrameContext
{
    private readonly VulkanNativeFrameSession session;

    internal NativeLayerFrameContext(
        ulong frameId,
        LayerId layer,
        LayerViewport viewport,
        VulkanNativeFrameSession session)
    {
        FrameId = frameId;
        Layer = layer;
        Viewport = viewport;
        this.session = session;
    }

    public ulong FrameId { get; }

    public LayerId Layer { get; }

    public LayerViewport Viewport { get; }

    public uint TargetWidth => session.Width;

    public uint TargetHeight => session.Height;

    public Native2DPassResult Present(
        VulkanOrderedQuadRenderer renderer,
        Action<VulkanOrderedQuadRenderer> submit)
    {
        return session.Present(renderer, submit);
    }
}

public sealed record NativeLayerFrameResult(
    IReadOnlyList<LayerPresentationDto> SemanticPresentations,
    IReadOnlyList<LayerId> NativeLayerOrder,
    VulkanNativeFrameResult NativeFrame);

public sealed class NativeLayerCompositor : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly AurelianLayerCompositor semanticCompositor;
    private readonly Dictionary<LayerId, INativeLayerPresenter> presenters = [];
    private readonly NativeFrameClearColor clearColor;
    private VulkanNativeFrameTarget target;
    private bool attached;
    private bool disposed;

    public NativeLayerCompositor(
        AurelianVulkanPlant plant,
        int width,
        int height,
        double scale = 1,
        NativeFrameClearColor? clearColor = null,
        VulkanTextureFormat format = VulkanTextureFormat.Rgba8Unorm)
    {
        ArgumentNullException.ThrowIfNull(plant);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Native compositor extent must be positive.");
        }
        this.plant = plant;
        this.clearColor = clearColor ?? new NativeFrameClearColor(16f / 255f, 32f / 255f, 64f / 255f, 1);
        semanticCompositor = new AurelianLayerCompositor(new LayerSurfaceDescriptor(width, height, scale));
        target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height, format);
    }

    public LayerSurfaceDescriptor Surface => semanticCompositor.Surface;

    public VulkanNativeFrameTarget Target => target;

    public void Add(IAurelianLayer layer, INativeLayerPresenter presenter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(presenter);
        LayerDescriptor descriptor = layer.Describe();
        if (descriptor.Id != presenter.Layer)
        {
            throw new ArgumentException(
                $"Semantic layer '{descriptor.Id}' does not match native presenter '{presenter.Layer}'.",
                nameof(presenter));
        }
        if (descriptor.PresentationMode != LayerPresentationMode.DirectHostPass)
        {
            throw new NotSupportedException(
                $"Layer '{descriptor.Id}' requests explicit offscreen isolation; M0 direct composition does not silently substitute a surface.");
        }
        semanticCompositor.Add(layer);
        presenters.Add(descriptor.Id, presenter);
        if (attached)
        {
            presenter.Attach(target);
        }
    }

    public void Attach()
    {
        ThrowIfDisposed();
        if (attached)
        {
            return;
        }
        semanticCompositor.Attach();
        foreach (INativeLayerPresenter presenter in presenters.Values)
        {
            presenter.Attach(target);
        }
        attached = true;
    }

    public void SetEnabled(LayerId layer, bool enabled)
    {
        ThrowIfDisposed();
        semanticCompositor.SetEnabled(layer, enabled);
    }

    public void SetZOrder(LayerId layer, int zOrder)
    {
        ThrowIfDisposed();
        semanticCompositor.SetZOrder(layer, zOrder);
    }

    public LayerInputRoutingResult RouteInput(LayerInputEvent input)
    {
        ThrowIfDisposed();
        return semanticCompositor.RouteInput(input);
    }

    public void DetachLayer(LayerId layer)
    {
        ThrowIfDisposed();
        if (!presenters.Remove(layer, out INativeLayerPresenter? presenter))
        {
            throw new KeyNotFoundException($"Native layer '{layer}' is not registered.");
        }
        semanticCompositor.SetEnabled(layer, false);
        presenter.Detach();
    }

    public void Resize(int width, int height, double scale = 1)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Native compositor extent must be positive.");
        }

        var replacement = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height, target.TextureFormat);
        try
        {
            foreach (INativeLayerPresenter presenter in presenters.Values)
            {
                presenter.Resize(replacement);
            }
            semanticCompositor.Resize(new LayerSurfaceDescriptor(width, height, scale));
        }
        catch
        {
            foreach (INativeLayerPresenter presenter in presenters.Values)
            {
                try
                {
                    presenter.Resize(target);
                }
                catch
                {
                    // Preserve the original resize failure. A presenter that cannot roll back
                    // will fail closed on the next frame instead of hiding the root cause.
                }
            }
            replacement.Dispose();
            throw;
        }

        VulkanNativeFrameTarget previous = target;
        target = replacement;
        previous.Dispose();
    }

    public NativeLayerFrameResult RunFrame(
        ulong frameId,
        TimeSpan elapsed,
        bool captureReadback = true)
    {
        ThrowIfDisposed();
        if (!attached)
        {
            throw new InvalidOperationException("The native compositor must be attached before frames are presented.");
        }

        IReadOnlyList<LayerPresentationDto> semanticPresentations = semanticCompositor.RunFrame(frameId, elapsed);
        using VulkanNativeFrameSession frame = target.BeginFrame(clearColor);
        var nativeOrder = new List<LayerId>(semanticPresentations.Count);
        foreach (LayerPresentationDto presentation in semanticPresentations)
        {
            if (!presenters.TryGetValue(presentation.Layer, out INativeLayerPresenter? presenter))
            {
                continue;
            }
            var context = new NativeLayerFrameContext(frameId, presentation.Layer, presentation.Viewport, frame);
            presenter.Present(context);
            nativeOrder.Add(presentation.Layer);
        }
        VulkanNativeFrameResult nativeFrame = frame.EndFrame(captureReadback);
        return new NativeLayerFrameResult(semanticPresentations, nativeOrder, nativeFrame);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (INativeLayerPresenter presenter in presenters.Values.Reverse())
        {
            presenter.Detach();
        }
        presenters.Clear();
        semanticCompositor.Dispose();
        target.Dispose();
        attached = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
