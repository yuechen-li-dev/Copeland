using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed class VulkanPresentationSemaphoreSet : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private VkSemaphore imageAvailableSemaphore;
    private VkSemaphore renderFinishedSemaphore;
    private bool disposed;

    private VulkanPresentationSemaphoreSet(
        AurelianVulkanPlant plant,
        VkSemaphore imageAvailableSemaphore,
        VkSemaphore renderFinishedSemaphore)
    {
        this.plant = plant;
        this.imageAvailableSemaphore = imageAvailableSemaphore;
        this.renderFinishedSemaphore = renderFinishedSemaphore;
        PlantId = plant.Context.Id;
    }

    public PlantId PlantId { get; }

    internal VkSemaphore ImageAvailableSemaphore => imageAvailableSemaphore;

    internal VkSemaphore RenderFinishedSemaphore => renderFinishedSemaphore;

    public static VulkanPresentationSemaphoreSet Create(AurelianVulkanPlant plant)
    {
        ArgumentNullException.ThrowIfNull(plant);

        if (!TryCreate(plant, out VulkanPresentationSemaphoreSet? semaphoreSet, out Result result))
        {
            throw new InvalidOperationException($"Failed to create swapchain presentation semaphores for plant '{plant.Context.Id}'. Vulkan result: {result}.");
        }

        return semaphoreSet;
    }

    internal static unsafe bool TryCreate(
        AurelianVulkanPlant plant,
        out VulkanPresentationSemaphoreSet semaphoreSet,
        out Result result)
    {
        SemaphoreCreateInfo createInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        result = plant.Vk.CreateSemaphore(plant.Device, in createInfo, (AllocationCallbacks*)null, out VkSemaphore imageAvailable);
        if (result != Result.Success)
        {
            semaphoreSet = null!;
            return false;
        }

        result = plant.Vk.CreateSemaphore(plant.Device, in createInfo, (AllocationCallbacks*)null, out VkSemaphore renderFinished);
        if (result != Result.Success)
        {
            plant.Vk.DestroySemaphore(plant.Device, imageAvailable, (AllocationCallbacks*)null);
            semaphoreSet = null!;
            return false;
        }

        semaphoreSet = new VulkanPresentationSemaphoreSet(plant, imageAvailable, renderFinished);
        return true;
    }

    public unsafe void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (renderFinishedSemaphore.Handle != 0 && plant.Device.Handle != 0)
        {
            plant.Vk.DestroySemaphore(plant.Device, renderFinishedSemaphore, (AllocationCallbacks*)null);
            renderFinishedSemaphore = default;
        }

        if (imageAvailableSemaphore.Handle != 0 && plant.Device.Handle != 0)
        {
            plant.Vk.DestroySemaphore(plant.Device, imageAvailableSemaphore, (AllocationCallbacks*)null);
            imageAvailableSemaphore = default;
        }
    }
}
