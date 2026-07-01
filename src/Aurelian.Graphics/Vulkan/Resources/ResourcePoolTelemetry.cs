namespace Aurelian.Graphics.Vulkan.Resources;

public sealed record ResourcePoolTelemetry(
    ulong Generation,
    ulong CreatedCount,
    ulong RentedCount,
    ulong ReusedCount,
    ulong RetiredCount,
    int QueuedCount,
    int HighWaterQueuedCount);
