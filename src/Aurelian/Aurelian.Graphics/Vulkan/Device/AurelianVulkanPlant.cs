using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Device;

public sealed class AurelianVulkanPlant : IDisposable
{
    private bool disposed;

    internal AurelianVulkanPlant(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Silk.NET.Vulkan.Device device,
        Queue graphicsQueue,
        uint queueFamilyIndex,
        PlantContext context,
        VulkanPlantFacts facts)
    {
        Vk = vk;
        Instance = instance;
        PhysicalDevice = physicalDevice;
        Device = device;
        GraphicsQueue = graphicsQueue;
        QueueFamilyIndex = queueFamilyIndex;
        Context = context;
        Facts = facts;
    }

    public Vk Vk { get; }

    public Instance Instance { get; private set; }

    public PhysicalDevice PhysicalDevice { get; }

    public Silk.NET.Vulkan.Device Device { get; private set; }

    public Queue GraphicsQueue { get; }

    public uint QueueFamilyIndex { get; }

    public PlantContext Context { get; }

    public VulkanPlantFacts Facts { get; }

    public unsafe void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (Device.Handle != 0)
        {
            Vk.DestroyDevice(Device, (AllocationCallbacks*)null);
            Device = default;
        }

        if (Instance.Handle != 0)
        {
            Vk.DestroyInstance(Instance, (AllocationCallbacks*)null);
            Instance = default;
        }

        Vk.Dispose();
    }
}
