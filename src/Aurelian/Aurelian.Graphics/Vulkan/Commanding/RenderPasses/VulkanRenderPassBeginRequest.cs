using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public sealed record VulkanRenderPassBeginRequest(
    AurelianVulkanRenderPass RenderPass,
    AurelianVulkanFramebuffer Framebuffer,
    VulkanColorClearValue ClearColor)
{
    /// <summary>Optional clears for targets after target zero, in attachment order.</summary>
    public IReadOnlyList<VulkanColorClearValue> AdditionalClearColors { get; init; } = [];
}
