using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed class AurelianVulkanSwapchain : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly KhrSwapchain swapchainApi;
    private readonly ImageView[] imageViews;
    private readonly VulkanPresentationSemaphoreSet semaphoreSet;
    private SwapchainKHR swapchain;
    private bool disposed;

    internal AurelianVulkanSwapchain(
        AurelianVulkanPlant plant,
        KhrSwapchain swapchainApi,
        SwapchainKHR swapchain,
        IReadOnlyList<Image> images,
        ImageView[] imageViews,
        VulkanPresentationSemaphoreSet semaphoreSet,
        VulkanSwapchainFacts facts)
    {
        this.plant = plant;
        this.swapchainApi = swapchainApi;
        this.swapchain = swapchain;
        this.imageViews = imageViews;
        this.semaphoreSet = semaphoreSet;
        Images = images.ToArray();
        ImageViews = imageViews.ToArray();
        Facts = facts;
    }

    public PlantId PlantId => plant.Context.Id;

    public VulkanSwapchainFacts Facts { get; }

    public IReadOnlyList<Image> Images { get; }

    public IReadOnlyList<ImageView> ImageViews { get; }

    public bool IsDisposed => disposed;

    public VulkanPresentationTargetImageSet CreatePresentationTargetImageSet()
        => VulkanPresentationTargetImageSet.FromSwapchain(this);

    public unsafe VulkanSwapchainAcquireResult AcquireNextImage(ulong timeoutNanoseconds = ulong.MaxValue)
    {
        if (disposed || swapchain.Handle == 0 || plant.Device.Handle == 0)
        {
            return new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.Rejected,
                null,
                [DisposedDiagnostic("Cannot acquire a swapchain image because the swapchain has been disposed.")]);
        }

        Fence acquireFence = default;
        FenceCreateInfo fenceCreateInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
        };

        Result fenceCreateResult = plant.Vk.CreateFence(plant.Device, in fenceCreateInfo, (AllocationCallbacks*)null, out acquireFence);
        if (fenceCreateResult != Result.Success)
        {
            return new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.Failed,
                null,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.AcquireFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkCreateFence failed for swapchain image acquire with result {fenceCreateResult}.",
                    PlantId)]);
        }

        uint imageIndex = 0;
        Result result;
        try
        {
            result = swapchainApi.AcquireNextImage(
                plant.Device,
                swapchain,
                timeoutNanoseconds,
                default,
                acquireFence,
                ref imageIndex);

            if (result is Result.Success or Result.SuboptimalKhr)
            {
                Result waitResult = plant.Vk.WaitForFences(plant.Device, 1, in acquireFence, true, timeoutNanoseconds);
                if (waitResult != Result.Success)
                {
                    return new VulkanSwapchainAcquireResult(
                        VulkanSwapchainAcquireStatus.Failed,
                        null,
                        [new VulkanPresentationDiagnostic(
                            VulkanPresentationDiagnosticCodes.AcquireFailed,
                            VulkanPresentationDiagnosticSeverity.Error,
                            $"vkWaitForFences failed while waiting for acquired swapchain image {imageIndex} with result {waitResult}.",
                            PlantId)]);
                }
            }
        }
        finally
        {
            if (acquireFence.Handle != 0 && plant.Device.Handle != 0)
            {
                plant.Vk.DestroyFence(plant.Device, acquireFence, (AllocationCallbacks*)null);
            }
        }

        return result switch
        {
            Result.Success => new VulkanSwapchainAcquireResult(VulkanSwapchainAcquireStatus.Acquired, imageIndex, []),
            Result.SuboptimalKhr => new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.Suboptimal,
                imageIndex,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SwapchainSuboptimal,
                    VulkanPresentationDiagnosticSeverity.Warning,
                    $"vkAcquireNextImageKHR returned {result}; image {imageIndex} is usable, but swapchain recreation should be considered by a later policy milestone.",
                    PlantId)]),
            Result.ErrorOutOfDateKhr => new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.OutOfDate,
                null,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SwapchainOutOfDate,
                    VulkanPresentationDiagnosticSeverity.Warning,
                    "vkAcquireNextImageKHR reported that the swapchain is out of date; A49 surfaces this as a typed result and does not recreate automatically.",
                    PlantId)]),
            Result.ErrorSurfaceLostKhr => new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.Unavailable,
                null,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SurfaceLost,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "vkAcquireNextImageKHR reported that the presentation surface was lost.",
                    PlantId)]),
            _ => new VulkanSwapchainAcquireResult(
                VulkanSwapchainAcquireStatus.Failed,
                null,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.AcquireFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkAcquireNextImageKHR failed with result {result}.",
                    PlantId)]),
        };
    }

    public unsafe VulkanSwapchainPresentResult Present(uint imageIndex)
    {
        if (disposed || swapchain.Handle == 0 || plant.Device.Handle == 0)
        {
            return new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.Rejected,
                [DisposedDiagnostic("Cannot present a swapchain image because the swapchain has been disposed.")]);
        }

        if (imageIndex >= Images.Count)
        {
            return new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.Rejected,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.InvalidImageIndex,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"Cannot present swapchain image {imageIndex}; valid image indices are 0 through {Images.Count - 1}.",
                    PlantId)]);
        }

        SwapchainKHR swapchainHandle = swapchain;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 0,
            PWaitSemaphores = null,
            SwapchainCount = 1,
            PSwapchains = &swapchainHandle,
            PImageIndices = &imageIndex,
            PResults = null,
        };

        Result result = swapchainApi.QueuePresent(plant.GraphicsQueue, in presentInfo);

        return result switch
        {
            Result.Success => new VulkanSwapchainPresentResult(VulkanSwapchainPresentStatus.Presented, []),
            Result.SuboptimalKhr => new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.Suboptimal,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SwapchainSuboptimal,
                    VulkanPresentationDiagnosticSeverity.Warning,
                    "vkQueuePresentKHR returned SuboptimalKhr; the image was presented, but swapchain recreation should be considered by a later policy milestone.",
                    PlantId)]),
            Result.ErrorOutOfDateKhr => new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.OutOfDate,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SwapchainOutOfDate,
                    VulkanPresentationDiagnosticSeverity.Warning,
                    "vkQueuePresentKHR reported that the swapchain is out of date; A49 surfaces this as a typed result and does not recreate automatically.",
                    PlantId)]),
            Result.ErrorSurfaceLostKhr => new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.Unavailable,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.SurfaceLost,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "vkQueuePresentKHR reported that the presentation surface was lost.",
                    PlantId)]),
            _ => new VulkanSwapchainPresentResult(
                VulkanSwapchainPresentStatus.Failed,
                [new VulkanPresentationDiagnostic(
                    VulkanPresentationDiagnosticCodes.PresentFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkQueuePresentKHR failed with result {result}.",
                    PlantId)]),
        };
    }

    private VulkanPresentationDiagnostic DisposedDiagnostic(string message)
        => new(
            VulkanPresentationDiagnosticCodes.PresentationDisposed,
            VulkanPresentationDiagnosticSeverity.Error,
            message,
            PlantId);

    public unsafe void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        semaphoreSet.Dispose();

        foreach (ImageView imageView in imageViews)
        {
            if (imageView.Handle != 0 && plant.Device.Handle != 0)
            {
                plant.Vk.DestroyImageView(plant.Device, imageView, (AllocationCallbacks*)null);
            }
        }

        if (swapchain.Handle != 0 && plant.Device.Handle != 0)
        {
            swapchainApi.DestroySwapchain(plant.Device, swapchain, (AllocationCallbacks*)null);
            swapchain = default;
        }

        swapchainApi.Dispose();
    }
}
