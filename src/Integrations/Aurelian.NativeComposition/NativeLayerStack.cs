using Aurelian.Composition;
using Aurelian.Graphics.Vulkan.Native2D;

namespace Aurelian.NativeComposition;

/// <summary>
/// Ordered native layers drawn into a frame target owned by someone else, such as the game shell's surface target.
/// Unlike <see cref="NativeLayerCompositor"/> it owns neither the target nor a semantic layer graph: the owner calls
/// <see cref="Retarget"/> when it replaces its target and <see cref="Present"/> once per frame. Layers draw in
/// ascending order, each over the full target.
/// </summary>
public sealed class NativeLayerStack : IDisposable
{
    private readonly List<Entry> entries = [];
    private VulkanNativeFrameTarget? target;
    private bool disposed;

    private sealed record Entry(INativeLayerPresenter Presenter, int Order);

    public int Count => entries.Count;

    public IReadOnlyList<LayerId> Layers => entries.Select(entry => entry.Presenter.Layer).ToArray();

    /// <summary>Add a layer. It is attached immediately when the stack already has a target.</summary>
    public void Add(INativeLayerPresenter presenter, int order = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(presenter);
        if (entries.Any(entry => entry.Presenter.Layer == presenter.Layer))
        {
            throw new ArgumentException($"Native layer '{presenter.Layer}' is already in the stack.", nameof(presenter));
        }
        if (target is not null)
        {
            presenter.Attach(target);
        }
        entries.Add(new Entry(presenter, order));
        // Stable: layers with equal order keep their insertion order.
        List<Entry> sorted = entries.OrderBy(entry => entry.Order).ToList();
        entries.Clear();
        entries.AddRange(sorted);
    }

    /// <summary>Remove and detach a layer. Returns false when it is not in the stack.</summary>
    public bool Remove(LayerId layer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int index = entries.FindIndex(entry => entry.Presenter.Layer == layer);
        if (index < 0)
        {
            return false;
        }
        INativeLayerPresenter presenter = entries[index].Presenter;
        entries.RemoveAt(index);
        if (target is not null)
        {
            presenter.Detach();
        }
        return true;
    }

    /// <summary>
    /// Point every layer at a new target: attach on first use, resize afterwards. Call it whenever the owner creates a
    /// replacement target (for example after a swapchain rebuild).
    /// </summary>
    public void Retarget(VulkanNativeFrameTarget replacement)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        bool first = target is null;
        target = replacement;
        foreach (Entry entry in entries)
        {
            if (first)
            {
                entry.Presenter.Attach(replacement);
            }
            else
            {
                entry.Presenter.Resize(replacement);
            }
        }
    }

    /// <summary>
    /// Draw all layers into the current target. With <paramref name="clear"/> the target is cleared first; without it the
    /// layers draw over whatever is already there (for example a 3D view).
    /// </summary>
    public VulkanNativeFrameResult? Present(ulong frameId, NativeFrameClearColor? clear = null, bool captureReadback = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (target is null || entries.Count == 0)
        {
            return null;
        }
        using VulkanNativeFrameSession frame = clear is { } color
            ? target.BeginFrame(color, preserveContents: false)
            : target.BeginFrame(NativeFrameClearColor.Transparent, preserveContents: true);
        var viewport = new LayerViewport(0, 0, target.Width, target.Height);
        foreach (Entry entry in entries)
        {
            entry.Presenter.Present(new NativeLayerFrameContext(frameId, entry.Presenter.Layer, viewport, frame));
        }
        return frame.EndFrame(captureReadback);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (target is not null)
        {
            foreach (Entry entry in Enumerable.Reverse(entries))
            {
                entry.Presenter.Detach();
            }
        }
        entries.Clear();
    }
}
