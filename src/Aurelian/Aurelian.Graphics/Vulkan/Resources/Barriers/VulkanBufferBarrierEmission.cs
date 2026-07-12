using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBufferBarrierEmission(
    AurelianVulkanBuffer Buffer,
    VulkanBufferTransitionPlan Plan);
