namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanVertexAttributeDescriptor(
    uint Location,
    uint Binding,
    VulkanVertexAttributeFormat Format,
    uint Offset);
