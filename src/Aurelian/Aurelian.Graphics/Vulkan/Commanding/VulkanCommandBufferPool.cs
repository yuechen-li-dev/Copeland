using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources;

namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed class VulkanCommandBufferPool : IDisposable
{
    private readonly object gate = new();
    private readonly VulkanCommandPool commandPool;
    private readonly FenceTaggedResourcePool<VulkanCommandBufferLease> leases;
    private readonly List<VulkanCommandBufferLease> knownLeases = [];
    private bool disposed;

    private VulkanCommandBufferPool(AurelianVulkanPlant plant, VulkanCommandPool commandPool)
    {
        PlantId = plant.Context.Id;
        QueueFamilyIndex = plant.QueueFamilyIndex;
        this.commandPool = commandPool;
        leases = new FenceTaggedResourcePool<VulkanCommandBufferLease>(
            () => CreateLease(plant),
            ResetLeaseForReuse);
    }

    public PlantId PlantId { get; }

    public uint QueueFamilyIndex { get; }

    public VulkanCommandBufferTelemetry Telemetry => VulkanCommandBufferTelemetry.From(PlantId, leases.Telemetry);

    public static VulkanCommandBufferPool Create(AurelianVulkanPlant plant)
    {
        ArgumentNullException.ThrowIfNull(plant);
        var commandPool = new VulkanCommandPool(plant);
        return new VulkanCommandBufferPool(plant, commandPool);
    }

    public VulkanCommandBufferLease Rent(ulong completedFenceValue)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return leases.Rent(completedFenceValue);
    }

    public void Retire(VulkanCommandBufferLease lease, ulong retireFenceValue)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (lease.PlantId != PlantId)
        {
            throw new InvalidOperationException(
                $"Cannot retire a command buffer lease from plant {lease.PlantId} into plant {PlantId}'s command buffer pool.");
        }

        lease.MarkRetired();
        leases.Retire(lease, retireFenceValue);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (gate)
        {
            foreach (VulkanCommandBufferLease lease in knownLeases)
            {
                lease.MarkDisposed();
            }
        }

        commandPool.Dispose();
    }

    private VulkanCommandBufferLease CreateLease(AurelianVulkanPlant plant)
    {
        var lease = new VulkanCommandBufferLease(PlantId, plant.Vk, commandPool.AllocatePrimary());
        lock (gate)
        {
            knownLeases.Add(lease);
        }

        return lease;
    }

    private static void ResetLeaseForReuse(VulkanCommandBufferLease lease)
    {
        VulkanCommandBufferOperationResult result = lease.Reset();
        if (!result.Success)
        {
            string message = string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message));
            throw new InvalidOperationException(message);
        }
    }
}
