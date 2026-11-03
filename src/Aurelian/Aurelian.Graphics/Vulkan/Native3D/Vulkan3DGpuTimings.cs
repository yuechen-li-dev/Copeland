using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record Native3DGpuPassTime(string Pass, double Milliseconds);
public sealed record Native3DCompletedGpuFrame(long Sequence, ulong StartTicks, ulong EndTicks, float NanosecondsPerTick);

/// <summary>Timestamp measurements for named sequential GPU passes; excludes CPU upload and readback.</summary>
internal sealed unsafe class Vulkan3DGpuTimings : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly QueryPool[] pools;
    private readonly VulkanFrameInFlightSet? frames;
    private QueryPool pool => pools[frames?.Slot ?? 0];
    private readonly float period;
    private readonly uint validBits;
    private readonly string[] names;
    public int PassCount => names.Length;
    private readonly long?[] sequences;
    private bool disposed;

    public Vulkan3DGpuTimings(AurelianVulkanPlant plant, IReadOnlyList<string>? passNames = null, VulkanFrameInFlightSet? frames = null)
    {
        this.plant = plant;
        this.frames = frames;
        pools = new QueryPool[frames is null ? 1 : VulkanFrameInFlightSet.Count];
        sequences = new long?[pools.Length];
        names = passNames?.ToArray() ?? ["directional-shadow", "linear-lighting", "tone-map-output"];
        if (names.Length is < 1 or > 32 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("GPU timing requires between one and thirty-two named passes.", nameof(passNames));
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
            QueryCount = (uint)names.Length * 2 + 2,
        };
        for (int index = 0; index < pools.Length; index++)
        {
            if (plant.Vk.CreateQueryPool(plant.Device, &info, null, out pools[index]) != Result.Success)
            {
                Dispose();
                throw new InvalidOperationException("3D GPU timestamp pool creation failed.");
            }
        }
    }

    public void Reset(VulkanCommandBufferLease command)
    {
        if (pool.Handle != 0)
        {
            plant.Vk.CmdResetQueryPool(command.CommandBuffer, pool, 0, (uint)names.Length * 2 + 2);
        }
    }

    public void BeginFrame(VulkanCommandBufferLease command, long sequence)
    {
        sequences[frames?.Slot ?? 0] = sequence;
        Mark(command, (uint)names.Length * 2);
    }

    public void EndFrame(VulkanCommandBufferLease command) => Mark(command, (uint)names.Length * 2 + 1);

    public Native3DCompletedGpuFrame? ReadCompletedFrame()
        => ReadInterval((uint)names.Length);

    public Native3DCompletedGpuFrame? ReadInterval(uint pass)
    {
        long? sequence = sequences[frames?.Slot ?? 0];
        if (pool.Handle == 0 || sequence is null)
        {
            return null;
        }
        ulong* values = stackalloc ulong[2];
        Result result = plant.Vk.GetQueryPoolResults(plant.Device, pool, pass * 2,
            2, 16, values, 8, QueryResultFlags.Result64Bit);
        if (result != Result.Success)
        {
            throw new InvalidOperationException("Completed frame timestamps were unavailable: " + result);
        }
        return new(sequence.Value, values[0], values[1], period);
    }

    public void Mark(VulkanCommandBufferLease command, uint index, PipelineStageFlags? stage = null)
    {
        if (pool.Handle != 0)
        {
            plant.Vk.CmdWriteTimestamp(command.CommandBuffer,
                stage ?? (index % 2 == 0 ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.BottomOfPipeBit), pool, index);
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
        foreach (QueryPool item in pools)
        {
            if (item.Handle != 0)
            {
                plant.Vk.DestroyQueryPool(plant.Device, item, null);
            }
        }
    }
}
