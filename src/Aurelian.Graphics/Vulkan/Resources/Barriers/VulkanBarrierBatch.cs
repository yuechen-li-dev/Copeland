namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBarrierBatch(IReadOnlyList<VulkanBarrierPlan> Plans)
{
    public bool IsEmpty => Plans.Count == 0;

    public static VulkanBarrierBatch Empty { get; } = new([]);
}
