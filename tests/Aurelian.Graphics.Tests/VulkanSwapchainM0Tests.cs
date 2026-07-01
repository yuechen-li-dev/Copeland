using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Silk.NET.Vulkan;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanSwapchainM0Tests
{
    [Fact]
    public void VulkanSwapchainFactory_Create_WhenHeadlessOrUnavailable_SkipsCleanly()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                Assert.Contains(init.Status, new[] { VulkanInitStatus.Unavailable, VulkanInitStatus.Rejected });
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

                Assert.True(result.Surface!.Width > 0);
                Assert.True(result.Surface.Height > 0);
                Assert.True(result.Swapchain!.Facts.ImageCount > 0);
                Assert.Equal(result.Swapchain.Facts.ImageCount, result.Swapchain.Facts.ImageViewCount);
            }
        }
    }

    [Fact]
    public void VulkanSwapchainFactory_Create_RequiresPresentationEnabledPlant()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult result = VulkanSwapchainFactory.Create(init.Plant!);

            Assert.False(result.Success);
            Assert.Equal(VulkanPresentationStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationDiagnosticCodes.SwapchainExtensionMissing);
        }
    }

    [Fact]
    public void VulkanSwapchainFactory_Create_WhenAvailable_CreatesSurfaceAndSwapchain()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult result = VulkanSwapchainFactory.Create(
                init.Plant!,
                new VulkanSwapchainCreateOptions(Width: 320, Height: 240, VSync: true, Title: "Aurelian Swapchain M0 Test"));

            using (result.Surface)
            using (result.Swapchain)
            {
                if (!result.Success)
                {
                    Assert.NotEmpty(result.Diagnostics);
                    return;
                }

                Assert.Equal(PlantId.Zero, result.Surface!.PlantId);
                Assert.Equal(PlantId.Zero, result.Swapchain!.PlantId);
                Assert.True(result.Swapchain.Images.Count > 0);
                Assert.Equal(result.Swapchain.Images.Count, result.Swapchain.ImageViews.Count);
                Assert.Equal("FifoKhr", result.Swapchain.Facts.SelectedPresentMode);
            }
        }
    }

    [Fact]
    public void AurelianVulkanSwapchain_Dispose_IsIdempotent()
        => WithOptionalSwapchain((_, swapchain) =>
        {
            swapchain.Dispose();
            swapchain.Dispose();
        });

    [Fact]
    public void AurelianVulkanSurface_Dispose_IsIdempotent()
        => WithOptionalSwapchain((surface, _) =>
        {
            surface.Dispose();
            surface.Dispose();
        });

    [Fact]
    public void VulkanSwapchainFactory_SelectsDeterministicFormat()
    {
        SurfaceFormatKHR[] formats =
        [
            new SurfaceFormatKHR(Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new SurfaceFormatKHR(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new SurfaceFormatKHR(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ];

        SurfaceFormatKHR selected = VulkanSwapchainFactory.SelectSurfaceFormat(formats);

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.Equal(ColorSpaceKHR.SpaceSrgbNonlinearKhr, selected.ColorSpace);
    }

    [Fact]
    public void VulkanSwapchainFactory_SelectsFifoWhenVsync()
    {
        PresentModeKHR selected = VulkanSwapchainFactory.SelectPresentMode(
            [PresentModeKHR.ImmediateKhr, PresentModeKHR.MailboxKhr, PresentModeKHR.FifoKhr],
            vsync: true);

        Assert.Equal(PresentModeKHR.FifoKhr, selected);
    }


    private static void WithOptionalSwapchain(Action<AurelianVulkanSurface, AurelianVulkanSwapchain> action)
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

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

                action(result.Surface!, result.Swapchain!);
            }
        }
    }
}
