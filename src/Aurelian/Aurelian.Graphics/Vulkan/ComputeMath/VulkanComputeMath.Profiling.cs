using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record ComputeDispatchProfile(ComputeMathResult Execution, IReadOnlyList<double> DispatchMilliseconds);

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>Times dispatch intervals in one synchronous submission. Intervals include
    /// synchronization and timestamp costs. Compare uninstrumented runs for latency.</summary>
    public ComputeDispatchProfile ExecuteWithDispatchTimings(ComputeMathBatch batch)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ComputeMathBatch.Require(queuedScope is null, "Dispatch profiling requires synchronous execution.");
        dispatchTimestamps ??= new(plant, MaximumDispatches + 1);
        ComputeMathResult result = ExecuteCore(batch, null, default, dispatchTimestamps);
        return new(result, dispatchTimestamps.Read(batch.DispatchCount));
    }

    private sealed class DispatchTimestamps : IDisposable
    {
        private readonly AurelianVulkanPlant plant;
        private readonly QueryPool pool;
        private readonly ulong mask;
        private readonly double millisecondsPerTick;

        public DispatchTimestamps(AurelianVulkanPlant plant, int capacity)
        {
            this.plant = plant;
            plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out var properties);
            uint count = 0;
            plant.Vk.GetPhysicalDeviceQueueFamilyProperties(plant.PhysicalDevice, &count, null);
            var families = new QueueFamilyProperties[count];
            fixed (QueueFamilyProperties* pointer = families)
            {
                plant.Vk.GetPhysicalDeviceQueueFamilyProperties(plant.PhysicalDevice, &count, pointer);
            }
            uint bits = families[plant.QueueFamilyIndex].TimestampValidBits;
            Require(bits != 0 && properties.Limits.TimestampPeriod > 0, "The Vulkan compute queue lacks timestamps.");
            mask = bits >= 64 ? ulong.MaxValue : (1UL << (int)bits) - 1;
            millisecondsPerTick = properties.Limits.TimestampPeriod / 1_000_000d;
            var info = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = (uint)capacity,
            };
            Check(plant.Vk.CreateQueryPool(plant.Device, &info, null, out pool));
        }

        public void Begin(VulkanCommandBufferLease command, int dispatches)
        {
            plant.Vk.CmdResetQueryPool(command.CommandBuffer, pool, 0, (uint)dispatches + 1);
        }

        public void Mark(VulkanCommandBufferLease command, int query)
        {
            plant.Vk.CmdWriteTimestamp(command.CommandBuffer, PipelineStageFlags.BottomOfPipeBit, pool, (uint)query);
        }

        public double[] Read(int dispatches)
        {
            ulong* ticks = stackalloc ulong[dispatches + 1];
            uint queries = (uint)dispatches + 1;
            Check(plant.Vk.GetQueryPoolResults(plant.Device, pool, 0, queries, queries * sizeof(ulong), ticks,
                sizeof(ulong), QueryResultFlags.Result64Bit));
            var elapsed = new double[dispatches];
            for (int index = 0; index < elapsed.Length; index++)
            {
                elapsed[index] = (unchecked(ticks[index + 1] - ticks[index]) & mask) * millisecondsPerTick;
            }
            return elapsed;
        }

        public void Dispose() => plant.Vk.DestroyQueryPool(plant.Device, pool, null);
    }
}
