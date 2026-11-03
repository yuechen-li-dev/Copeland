namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public readonly record struct VulkanColorClearValue(
    float R,
    float G,
    float B,
    float A)
{
    public (uint R, uint G, uint B, uint A)? Unsigned { get; init; }

    public static VulkanColorClearValue UInt(uint r, uint g, uint b = 0, uint a = 0)
        => new(0, 0, 0, 0) { Unsigned = (r, g, b, a) };

    public static VulkanColorClearValue TransparentBlack { get; } = new(0, 0, 0, 0);

    public static VulkanColorClearValue OpaqueBlack { get; } = new(0, 0, 0, 1);
}
