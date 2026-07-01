using System.Collections.Generic;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanCompiledShaderStageMappingResult(
    IReadOnlyList<VulkanShaderStageDescriptor> Stages,
    IReadOnlyList<VulkanCompiledShaderStageMappingDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(x => x.Severity != VulkanCompiledShaderStageMappingDiagnosticSeverity.Error);
}
