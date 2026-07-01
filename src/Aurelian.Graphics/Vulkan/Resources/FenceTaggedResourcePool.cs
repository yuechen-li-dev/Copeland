namespace Aurelian.Graphics.Vulkan.Resources;

public sealed class FenceTaggedResourcePool<T>
{
    private readonly object gate = new();
    private readonly Queue<FenceTaggedResource<T>> retiredResources = new();
    private readonly Func<T> create;
    private readonly Action<T>? reset;
    private ulong generation;
    private ulong createdCount;
    private ulong rentedCount;
    private ulong reusedCount;
    private ulong retiredCount;
    private int highWaterQueuedCount;

    public FenceTaggedResourcePool(Func<T> create, Action<T>? reset = null)
    {
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.reset = reset;
    }

    public ResourcePoolTelemetry Telemetry
    {
        get
        {
            lock (gate)
            {
                return SnapshotTelemetry();
            }
        }
    }

    public T Rent(ulong completedFenceValue)
    {
        T? reusable = default;
        bool hasReusable = false;

        lock (gate)
        {
            rentedCount++;
            generation++;

            if (retiredResources.TryPeek(out FenceTaggedResource<T>? tagged))
            {
                if (tagged.RetireFenceValue <= completedFenceValue)
                {
                    reusable = retiredResources.Dequeue().Resource;
                    reusedCount++;
                    hasReusable = true;
                }
            }
        }

        if (hasReusable)
        {
            reset?.Invoke(reusable!);
            return reusable!;
        }

        T created = create();
        lock (gate)
        {
            createdCount++;
        }

        return created;
    }

    public void Retire(T resource, ulong retireFenceValue)
    {
        lock (gate)
        {
            retiredResources.Enqueue(new FenceTaggedResource<T>(resource, retireFenceValue));
            retiredCount++;
            generation++;
            if (retiredResources.Count > highWaterQueuedCount)
            {
                highWaterQueuedCount = retiredResources.Count;
            }
        }
    }

    private ResourcePoolTelemetry SnapshotTelemetry()
        => new(
            generation,
            createdCount,
            rentedCount,
            reusedCount,
            retiredCount,
            retiredResources.Count,
            highWaterQueuedCount);
}
