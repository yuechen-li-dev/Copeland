using Aurelian.Graphics.Vulkan.Sync;

namespace Aurelian.Graphics.Vulkan.Commanding;

/// <summary>Owns the retirement boundary for two CPU-recorded frames on the graphics queue.</summary>
internal sealed class VulkanFrameInFlightSet(VulkanTimelineFence fence)
{
    public const int Count = 2;
    private readonly ulong[] submitted = new ulong[Count];
    private readonly Queue<(ulong Value, IDisposable Resource)> retired = new();

    public int Slot { get; private set; }
    public ulong LastSubmitted { get; private set; }

    public void Begin()
    {
        Wait(submitted[Slot]);
        Collect();
    }

    public void Submitted(ulong value)
    {
        submitted[Slot] = value;
        LastSubmitted = value;
        Slot = (Slot + 1) % Count;
    }

    public void Drain()
    {
        Wait(LastSubmitted);
        Collect();
    }

    public void Retire(IDisposable resource)
    {
        retired.Enqueue((LastSubmitted, resource));
        Collect();
    }

    public void Retire(Action release) => Retire(new DeferredRelease(release));

    private sealed class DeferredRelease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    private void Wait(ulong value)
    {
        if (value == 0)
        {
            return;
        }
        var result = fence.WaitForValue(value, 5_000_000_000);
        if (!result.Success)
        {
            throw new InvalidOperationException("AUR-FRAME-001: Frame retirement failed: "
                + string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        }
    }

    private void Collect()
    {
        while (retired.TryPeek(out var entry) && entry.Value <= fence.LastKnownCompletedValue)
        {
            retired.Dequeue().Resource.Dispose();
        }
    }
}

