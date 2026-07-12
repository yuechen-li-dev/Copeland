using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Rendering.Contracts.Compositor;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanPresentationTargetImageM0Tests
{
    [Fact]
    public void VulkanPresentationTargetImageSet_Create_WhenHeadlessOrUnavailable_SkipsCleanly()
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

                VulkanPresentationTargetImageSet imageSet = result.Swapchain!.CreatePresentationTargetImageSet();

                Assert.NotEmpty(imageSet.Images);
                Assert.Equal(result.Swapchain.Facts.ImageCount, (uint)imageSet.Images.Count);
            }
        }
    }

    [Fact]
    public void VulkanPresentationTargetImageSet_Create_WhenSwapchainAvailable_WrapsAllImages()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanPresentationTargetImageSet imageSet = swapchain.CreatePresentationTargetImageSet();

            Assert.Equal(swapchain.PlantId, imageSet.PlantId);
            Assert.Equal(swapchain.Facts.ImageCount, (uint)imageSet.Images.Count);
            Assert.Equal(swapchain.Images.Count, imageSet.Images.Count);

            for (int i = 0; i < imageSet.Images.Count; i++)
            {
                VulkanPresentationTargetImage target = imageSet.Images[i];

                Assert.Equal(swapchain.PlantId, target.PlantId);
                Assert.Equal((uint)i, target.ImageIndex);
                Assert.Equal(swapchain.Facts.Width, target.Width);
                Assert.Equal(swapchain.Facts.Height, target.Height);
                Assert.Equal(swapchain.Facts.SelectedFormat, target.Format);
                Assert.Equal(swapchain.Images[i], target.NativeImage);
                Assert.Equal(swapchain.ImageViews[i], target.NativeImageView);
            }
        });

    [Fact]
    public void VulkanPresentationTargetImageSet_TargetsUsePresentInitialLayout()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanPresentationTargetImageSet imageSet = swapchain.CreatePresentationTargetImageSet();

            foreach (VulkanPresentationTargetImage target in imageSet.Images)
            {
                Assert.Equal(1u, target.LayoutTracker.MipLevels);
                Assert.Equal(1u, target.LayoutTracker.ArrayLayers);
                Assert.Equal(VulkanResourceLayout.Present, target.LayoutTracker.Get(0, 0));
            }
        });

    [Fact]
    public void VulkanPresentationTargetResolver_ResolvesMatchingPresentationTargetRef()
    {
        VulkanPresentationTargetImageSet imageSet = CreateSyntheticImageSet();
        PresentationTargetRef targetRef = new(imageSet.PlantId.Value, 1, 42);

        VulkanPresentationTargetResolutionResult result = VulkanPresentationTargetResolver.Resolve(imageSet, targetRef);

        Assert.True(result.Success);
        Assert.Equal(VulkanPresentationTargetStatus.Resolved, result.Status);
        Assert.Same(imageSet.Images[1], result.Target);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void VulkanPresentationTargetResolver_RejectsPlantMismatch()
    {
        VulkanPresentationTargetImageSet imageSet = CreateSyntheticImageSet();
        PresentationTargetRef targetRef = new(imageSet.PlantId.Value + 1, 0, 42);

        VulkanPresentationTargetResolutionResult result = VulkanPresentationTargetResolver.Resolve(imageSet, targetRef);

        Assert.False(result.Success);
        Assert.Equal(VulkanPresentationTargetStatus.Rejected, result.Status);
        Assert.Null(result.Target);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationTargetDiagnosticCodes.PlantMismatch);
    }

    [Fact]
    public void VulkanPresentationTargetResolver_RejectsImageIndexOutOfRange()
    {
        VulkanPresentationTargetImageSet imageSet = CreateSyntheticImageSet();
        PresentationTargetRef targetRef = new(imageSet.PlantId.Value, 99, 42);

        VulkanPresentationTargetResolutionResult result = VulkanPresentationTargetResolver.Resolve(imageSet, targetRef);

        Assert.False(result.Success);
        Assert.Equal(VulkanPresentationTargetStatus.Rejected, result.Status);
        Assert.Null(result.Target);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanPresentationTargetDiagnosticCodes.ImageIndexOutOfRange);
    }

    [Fact]
    public void VulkanPresentationTargetImageSet_DoesNotOwnSwapchainImagesOrViews()
        => WithOptionalSwapchain(swapchain =>
        {
            VulkanPresentationTargetImageSet imageSet = swapchain.CreatePresentationTargetImageSet();
            Assert.Equal(swapchain.Images.Count, imageSet.Images.Count);

            swapchain.Dispose();
            swapchain.Dispose();
        });

    [Fact]
    public void VulkanPresentationTargetImageSet_CreateAfterSwapchainDispose_IsDisallowed()
        => WithOptionalSwapchain(swapchain =>
        {
            swapchain.Dispose();

            Assert.Throws<ObjectDisposedException>(() => swapchain.CreatePresentationTargetImageSet());
        });

    private static VulkanInitResult CreatePresentationPlant()
        => VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

    private static VulkanPresentationTargetImageSet CreateSyntheticImageSet()
    {
        PlantId plantId = new(7);
        VulkanPresentationTargetImage[] targets =
        [
            new VulkanPresentationTargetImage(plantId, 0, default, default, 640, 480, "B8G8R8A8Srgb"),
            new VulkanPresentationTargetImage(plantId, 1, default, default, 640, 480, "B8G8R8A8Srgb"),
        ];

        return new VulkanPresentationTargetImageSet(plantId, targets);
    }

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
