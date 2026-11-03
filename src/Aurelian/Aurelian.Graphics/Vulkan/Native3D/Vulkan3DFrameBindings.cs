using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public enum Native3DFrameMode
{
    Capture,
    Interactive,
}

/// <summary>Each in-flight frame owns its mutable uniform and descriptor set.</summary>
internal sealed unsafe class Vulkan3DFrameBindings : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanFrameInFlightSet? frames;
    private readonly AurelianVulkanBuffer[] uniforms;
    private readonly DescriptorPool[] pools;
    private readonly DescriptorSet[] sets;

    public Vulkan3DFrameBindings(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        DescriptorSetLayout layout, int bytes, uint imageCount, VulkanFrameInFlightSet? frames)
    {
        this.plant = plant;
        this.frames = frames;
        int count = frames is null ? 1 : VulkanFrameInFlightSet.Count;
        uniforms = new AurelianVulkanBuffer[count];
        pools = new DescriptorPool[count];
        sets = new DescriptorSet[count];
        try
        {
            for (int index = 0; index < count; index++)
            {
                uniforms[index] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                    (ulong)bytes, VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "frame.uniform");
                (pools[index], sets[index]) = Vulkan3DPass.AllocateSet(plant, layout, uniforms[index], imageCount);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanBuffer Uniform => uniforms[frames?.Slot ?? 0];
    public DescriptorSet Set => sets[frames?.Slot ?? 0];
    public IReadOnlyList<DescriptorSet> Sets => sets;

    public void Dispose()
    {
        for (int index = 0; index < pools.Length; index++)
        {
            if (pools[index].Handle != 0)
            {
                plant.Vk.DestroyDescriptorPool(plant.Device, pools[index], null);
                pools[index] = default;
            }
            uniforms[index]?.Dispose();
        }
    }
}
