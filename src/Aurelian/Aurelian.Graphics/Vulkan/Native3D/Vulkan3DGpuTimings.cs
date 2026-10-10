using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record Native3DGpuPassTime(string Pass, double Milliseconds);

/// <summary>Timestamp measurements for named sequential GPU passes; excludes CPU upload and readback.</summary>
internal sealed unsafe class Vulkan3DGpuTimings : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly QueryPool pool;
    private readonly float period;
    private readonly uint validBits;
    private readonly string[] names;
    private bool disposed;

    public Vulkan3DGpuTimings(AurelianVulkanPlant plant, IReadOnlyList<string>? passNames = null)
    {
        this.plant = plant;
        names = passNames?.ToArray() ?? ["directional-shadow", "linear-lighting", "tone-map-output"];
        if (names.Length is < 1 or > 16 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("GPU timing requires between one and sixteen named passes.", nameof(passNames));
        }
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        period = properties.Limits.TimestampPeriod;
        uint count = 0;
        plant.Vk.GetPhysicalDeviceQueueFamilyProperties(plant.PhysicalDevice, &count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = families)
        {
            plant.Vk.GetPhysicalDeviceQueueFamilyProperties(plant.PhysicalDevice, &count, pointer);
        }
        validBits = families[plant.QueueFamilyIndex].TimestampValidBits;
        if (validBits == 0 || period <= 0)
        {
            return;
        }
        QueryPoolCreateInfo info = new()
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = (uint)names.Length * 2,
        };
        if (plant.Vk.CreateQueryPool(plant.Device, &info, null, out pool) != Result.Success)
        {
            throw new InvalidOperationException("3D GPU timestamp pool creation failed.");
        }
    }

    public void Reset(VulkanCommandBufferLease command)
    {
        if (pool.Handle != 0)
        {
            plant.Vk.CmdResetQueryPool(command.CommandBuffer, pool, 0, (uint)names.Length * 2);
        }
    }

    public void Mark(VulkanCommandBufferLease command, uint index)
    {
        if (pool.Handle != 0)
        {
            plant.Vk.CmdWriteTimestamp(command.CommandBuffer,
                index % 2 == 0 ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.BottomOfPipeBit, pool, index);
        }
    }

    public IReadOnlyList<Native3DGpuPassTime> Read()
    {
        if (pool.Handle == 0)
        {
            return [];
        }
        uint queryCount = (uint)names.Length * 2;
        ulong* values = stackalloc ulong[(int)queryCount];
        Result result = plant.Vk.GetQueryPoolResults(plant.Device, pool, 0, queryCount, queryCount * 8, values, 8, QueryResultFlags.Result64Bit);
        if (result != Result.Success)
        {
            throw new InvalidOperationException("Completed 3D GPU timestamps were unavailable: " + result);
        }
        ulong mask = validBits >= 64 ? ulong.MaxValue : (1UL << (int)validBits) - 1;
        var timings = new List<Native3DGpuPassTime>();
        for (int index = 0; index < names.Length; index++)
        {
            ulong elapsed = unchecked(values[index * 2 + 1] - values[index * 2]) & mask;
            timings.Add(new(names[index], elapsed * period / 1_000_000d));
        }
        return timings;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (pool.Handle != 0)
        {
            plant.Vk.DestroyQueryPool(plant.Device, pool, null);
        }
    }
}
