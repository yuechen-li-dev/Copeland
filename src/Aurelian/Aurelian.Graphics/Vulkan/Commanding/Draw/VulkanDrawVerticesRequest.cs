using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public sealed record VulkanDrawVerticesRequest(
    AurelianVulkanGraphicsPipeline Pipeline,
    AurelianVulkanBuffer VertexBuffer,
    uint VertexCount,
    uint FirstVertex,
    VulkanViewportScissor ViewportScissor)
{
    /// <summary>Optional corresponding previous-frame positions at vertex binding one.</summary>
    public AurelianVulkanBuffer? PreviousVertexBuffer { get; init; }
}
