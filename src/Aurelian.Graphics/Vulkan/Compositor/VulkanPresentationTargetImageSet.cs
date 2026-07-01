using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Presentation;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Compositor;

/// <summary>
/// Non-owning ordered wrapper set for the images exposed by a Vulkan swapchain.
/// </summary>
public sealed class VulkanPresentationTargetImageSet
{
    public VulkanPresentationTargetImageSet(PlantId plantId, IReadOnlyList<VulkanPresentationTargetImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        PlantId = plantId;
        Images = images.ToArray();
    }

    public PlantId PlantId { get; }

    public IReadOnlyList<VulkanPresentationTargetImage> Images { get; }

    public bool TryGet(uint imageIndex, out VulkanPresentationTargetImage target)
    {
        if (imageIndex < Images.Count)
        {
            target = Images[(int)imageIndex];
            return true;
        }

        target = null!;
        return false;
    }

    internal static VulkanPresentationTargetImageSet FromSwapchain(AurelianVulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(swapchain);

        if (swapchain.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(AurelianVulkanSwapchain), "Cannot create presentation target image wrappers from a disposed swapchain.");
        }

        List<VulkanPresentationTargetImage> images = new(swapchain.Images.Count);
        for (int i = 0; i < swapchain.Images.Count; i++)
        {
            ImageView imageView = i < swapchain.ImageViews.Count ? swapchain.ImageViews[i] : default;
            images.Add(new VulkanPresentationTargetImage(
                swapchain.PlantId,
                (uint)i,
                swapchain.Images[i],
                imageView,
                swapchain.Facts.Width,
                swapchain.Facts.Height,
                swapchain.Facts.SelectedFormat));
        }

        return new VulkanPresentationTargetImageSet(swapchain.PlantId, images);
    }
}
