using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources;

namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed record VulkanCommandBufferTelemetry(
    PlantId PlantId,
    ulong Generation,
    ulong CreatedCount,
    ulong RentedCount,
    ulong ReusedCount,
    ulong RetiredCount,
    int QueuedCount,
    int HighWaterQueuedCount)
{
    internal static VulkanCommandBufferTelemetry From(PlantId plantId, ResourcePoolTelemetry telemetry)
        => new(
            plantId,
            telemetry.Generation,
            telemetry.CreatedCount,
            telemetry.RentedCount,
            telemetry.ReusedCount,
            telemetry.RetiredCount,
            telemetry.QueuedCount,
            telemetry.HighWaterQueuedCount);
}
