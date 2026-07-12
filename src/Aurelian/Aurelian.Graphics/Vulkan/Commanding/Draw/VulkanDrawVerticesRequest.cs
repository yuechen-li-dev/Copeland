using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public sealed record VulkanDrawVerticesRequest(
    AurelianVulkanGraphicsPipeline Pipeline,
    AurelianVulkanBuffer VertexBuffer,
    uint VertexCount,
    uint FirstVertex,
    VulkanViewportScissor ViewportScissor);
