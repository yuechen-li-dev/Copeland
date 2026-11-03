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
    public AurelianVulkanBuffer? IndexBuffer { get; init; }
    public uint IndexCount { get; init; }
    public AurelianVulkanBuffer? InstanceBuffer { get; init; }
    public uint InstanceCount { get; init; } = 1;
    public uint FirstInstance { get; init; }
}
