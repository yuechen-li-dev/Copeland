namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public readonly record struct VulkanColorClearValue(
    float R,
    float G,
    float B,
    float A)
{
    public static VulkanColorClearValue TransparentBlack { get; } = new(0, 0, 0, 0);

    public static VulkanColorClearValue OpaqueBlack { get; } = new(0, 0, 0, 1);
}
