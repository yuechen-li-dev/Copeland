namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanGraphicsPipelineDescriptor(
    IReadOnlyList<VulkanShaderStageDescriptor> ShaderStages,
    IReadOnlyList<VulkanVertexBufferLayoutDescriptor> VertexBuffers,
    IReadOnlyList<VulkanVertexAttributeDescriptor> VertexAttributes,
    bool EnableDepthTest = false,
    bool EnableDepthWrite = false);
