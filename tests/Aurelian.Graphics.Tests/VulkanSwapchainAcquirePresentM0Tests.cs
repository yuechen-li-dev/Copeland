using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanSwapchainAcquirePresentM0Tests
{
    [Fact]
    public void VulkanSwapchain_AcquirePresent_WhenHeadlessOrUnavailable_SkipsCleanly()
    {
        VulkanInitResult init = CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult result = VulkanSwapchainFactory.Create(init.Plant!);
            using (result.Surface)
            using (result.Swapchain)
            {
                if (!result.Success)
                {
                    Assert.NotEmpty(result.Diagnostics);
                    Assert.Contains(result.Status, new[] { VulkanPresentationStatus.Unavailable, VulkanPresentationStatus.Rejected, VulkanPresentationStatus.Failed });
                    return;
                }

                VulkanSwapchainAcquireResult acquire = result.Swapchain!.AcquireNextImage();
                Assert.Contains(acquire.Status, new[]
                {
                    VulkanSwapchainAcquireStatus.Acquired,
                    VulkanSwapchainAcquireStatus.Suboptimal,
                    VulkanSwapchainAcquireStatus.OutOfDate,
                    VulkanSwapchainAcquireStatus.Unavailable,
                    VulkanSwapchainAcquireStatus.Failed,
                });
            }
        }
    }

    [Fact]
    public void VulkanSwapchain_AcquireNextImage_WhenAvailable_ReturnsImageIndexOrOutOfDateResult()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();

            Assert.Contains(acquire.Status, new[]
            {
                VulkanSwapchainAcquireStatus.Acquired,
                VulkanSwapchainAcquireStatus.Suboptimal,
                VulkanSwapchainAcquireStatus.OutOfDate,
                VulkanSwapchainAcquireStatus.Unavailable,
                VulkanSwapchainAcquireStatus.Failed,
            });

            if (acquire.Status is VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal)
            {
                Assert.NotNull(acquire.ImageIndex);
                Assert.True(acquire.ImageIndex!.Value < swapchain.Images.Count);
            }
            else
            {
                Assert.Null(acquire.ImageIndex);
                Assert.NotEmpty(acquire.Diagnostics);
            }
        });

    [Fact]
    public void VulkanSwapchain_PresentRejectsInvalidImageIndex()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanSwapchainPresentResult present = swapchain.Present((uint)swapchain.Images.Count);

            Assert.False(present.Success);
            Assert.Equal(VulkanSwapchainPresentStatus.Rejected, present.Status);
            Assert.Contains(present.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationDiagnosticCodes.InvalidImageIndex);
        });

    [Fact]
    public void VulkanSwapchain_Present_WhenImageAcquired_ReturnsPresentedOrSuboptimalOrOutOfDate()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
            if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
            {
                Assert.NotEmpty(acquire.Diagnostics);
                return;
            }

            VulkanSwapchainPresentResult present = swapchain.Present(acquire.ImageIndex.Value);

            Assert.Contains(present.Status, new[]
            {
                VulkanSwapchainPresentStatus.Presented,
                VulkanSwapchainPresentStatus.Suboptimal,
                VulkanSwapchainPresentStatus.OutOfDate,
            });
        });

    [Fact]
    public void VulkanPresentationSemaphoreSet_Dispose_IsIdempotent()
    {
        VulkanInitResult init = CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using VulkanPresentationSemaphoreSet semaphores = VulkanPresentationSemaphoreSet.Create(init.Plant!);
            semaphores.Dispose();
            semaphores.Dispose();
        }
    }

    [Fact]
    public void VulkanSwapchain_AcquireAfterDispose_ReturnsDisposedDiagnostic()
        => WithOptionalSwapchain(swapchain =>
        {
            swapchain.Dispose();

            VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();

            Assert.False(acquire.Success);
            Assert.Equal(VulkanSwapchainAcquireStatus.Rejected, acquire.Status);
            Assert.Contains(acquire.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationDiagnosticCodes.PresentationDisposed);
        });

    [Fact]
    public void VulkanSwapchain_PresentAfterDispose_ReturnsDisposedDiagnostic()
        => WithOptionalSwapchain(swapchain =>
        {
            swapchain.Dispose();

            VulkanSwapchainPresentResult present = swapchain.Present(0);

            Assert.False(present.Success);
            Assert.Equal(VulkanSwapchainPresentStatus.Rejected, present.Status);
            Assert.Contains(present.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationDiagnosticCodes.PresentationDisposed);
        });

    private static VulkanInitResult CreatePresentationPlant()
        => VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

    private static void WithOptionalSwapchain(Action<AurelianVulkanSwapchain> action)
    {
        VulkanInitResult init = CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult result = VulkanSwapchainFactory.Create(init.Plant!);
            using (result.Surface)
            using (result.Swapchain)
            {
                if (!result.Success)
                {
                    Assert.NotEmpty(result.Diagnostics);
                    return;
                }

                action(result.Swapchain!);
            }
        }
    }
}
