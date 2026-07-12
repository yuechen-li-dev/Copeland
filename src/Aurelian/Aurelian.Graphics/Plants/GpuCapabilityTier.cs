namespace Aurelian.Graphics.Plants;

public sealed record GpuCapabilityTier(
    string Name,
    bool SupportsTimelineSemaphores,
    bool SupportsPresentation)
{
    public static GpuCapabilityTier Unknown { get; } = new("Unknown", false, false);

    public static GpuCapabilityTier SoftwareSmoke { get; } = new("SoftwareSmoke", false, false);

    public static GpuCapabilityTier VulkanM0 { get; } = new("VulkanM0", true, false);
}
