namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanShaderStageDescriptor(
    VulkanShaderStageKind Stage,
    string EntryPoint,
    IReadOnlyList<uint> SpirvWords);
