using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Compositor;

/// <summary>
/// Non-owning wrapper for a swapchain image that can be addressed as a backend presentation target.
/// </summary>
/// <remarks>
/// The wrapper never owns image memory, never destroys the swapchain image, and never destroys the
/// image view. Swapchain image views remain owned by the swapchain owner. A52 initializes the single
/// tracked presentation subresource to <see cref="VulkanResourceLayout.Present"/> so later compositor
/// milestones can plan explicit transitions from the presentation convention.
/// </remarks>
public sealed class VulkanPresentationTargetImage
{
    internal VulkanPresentationTargetImage(
        PlantId plantId,
        uint imageIndex,
        Image nativeImage,
        ImageView nativeImageView,
        uint width,
        uint height,
        string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        PlantId = plantId;
        ImageIndex = imageIndex;
        NativeImage = nativeImage;
        NativeImageView = nativeImageView;
        Width = width;
        Height = height;
        Format = format;
        LayoutTracker = new VulkanLayoutTracker(1, 1, VulkanResourceLayout.Present);
    }

    public PlantId PlantId { get; }

    public uint ImageIndex { get; }

    public uint Width { get; }

    public uint Height { get; }

    public string Format { get; }

    public VulkanLayoutTracker LayoutTracker { get; }

    internal Image NativeImage { get; }

    internal ImageView NativeImageView { get; }
}
