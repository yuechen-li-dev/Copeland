using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public readonly record struct VulkanViewportScissor(
    float X,
    float Y,
    float Width,
    float Height,
    float MinDepth = 0f,
    float MaxDepth = 1f)
{
    public static VulkanViewportScissor FromFramebuffer(AurelianVulkanFramebuffer framebuffer)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        return new VulkanViewportScissor(0, 0, framebuffer.Width, framebuffer.Height);
    }

    public bool IsValid => Width > 0f
        && Height > 0f
        && MinDepth >= 0f
        && MinDepth <= 1f
        && MaxDepth >= 0f
        && MaxDepth <= 1f
        && MinDepth <= MaxDepth
        && IsFinite(X)
        && IsFinite(Y)
        && IsFinite(Width)
        && IsFinite(Height)
        && IsFinite(MinDepth)
        && IsFinite(MaxDepth);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
