using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed class VulkanCommandPool : IDisposable
{
    private readonly Vk vk;
    private readonly Silk.NET.Vulkan.Device device;
    private CommandPool commandPool;
    private bool disposed;

    public unsafe VulkanCommandPool(AurelianVulkanPlant plant)
    {
        ArgumentNullException.ThrowIfNull(plant);

        vk = plant.Vk;
        device = plant.Device;
        PlantId = plant.Context.Id;
        QueueFamilyIndex = plant.QueueFamilyIndex;

        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = QueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };

        Result result = vk.CreateCommandPool(device, &createInfo, null, out commandPool);
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{VulkanCommandBufferDiagnosticCodes.CommandPoolCreationFailed}: Vulkan command pool creation failed with result {result} for plant {PlantId} and queue family {QueueFamilyIndex}.");
        }
    }

    public PlantId PlantId { get; }

    public uint QueueFamilyIndex { get; }

    public CommandPool NativeCommandPool => commandPool;

    public CommandBuffer AllocatePrimary() => Allocate(CommandBufferLevel.Primary);

    internal CommandBuffer AllocateSecondary() => Allocate(CommandBufferLevel.Secondary);

    private unsafe CommandBuffer Allocate(CommandBufferLevel level)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = level,
            CommandBufferCount = 1,
        };

        Result result = vk.AllocateCommandBuffers(device, &allocateInfo, out CommandBuffer commandBuffer);
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{VulkanCommandBufferDiagnosticCodes.CommandBufferAllocationFailed}: Vulkan {level} command buffer allocation failed with result {result} for plant {PlantId}.");
        }

        return commandBuffer;
    }

    public unsafe void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (commandPool.Handle != 0)
        {
            vk.DestroyCommandPool(device, commandPool, null);
            commandPool = default;
        }
    }
}
