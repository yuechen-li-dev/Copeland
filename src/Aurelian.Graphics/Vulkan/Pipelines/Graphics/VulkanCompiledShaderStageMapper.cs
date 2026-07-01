using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static class VulkanCompiledShaderStageMapper
{
    private const uint SpirvMagicNumber = 0x07230203;

    public static VulkanCompiledShaderStageMappingResult ToVulkanShaderStages(CompiledShaderProgram program)
    {
        var diagnostics = new List<VulkanCompiledShaderStageMappingDiagnostic>();
        var descriptors = new List<VulkanShaderStageDescriptor>();

        if (program is null)
        {
            diagnostics.Add(Error(
                VulkanCompiledShaderStageMappingDiagnosticCodes.MissingProgram,
                "Compiled shader program must not be null."));
            return new VulkanCompiledShaderStageMappingResult(descriptors, diagnostics);
        }

        if (program.Stages is null || program.Stages.Count == 0)
        {
            diagnostics.Add(Error(
                VulkanCompiledShaderStageMappingDiagnosticCodes.MissingStages,
                "Compiled shader program must contain at least one stage."));
            return new VulkanCompiledShaderStageMappingResult(descriptors, diagnostics);
        }

        foreach (IGrouping<CompiledShaderStageKind, CompiledShaderStage> group in program.Stages.GroupBy(x => x.Stage).Where(x => x.Count() > 1))
        {
            diagnostics.Add(Error(
                VulkanCompiledShaderStageMappingDiagnosticCodes.DuplicateShaderStage,
                $"Duplicate compiled shader stage '{group.Key}' is not supported.",
                ToVulkanStageOrNull(group.Key)));
        }

        foreach (CompiledShaderStage stage in program.Stages)
        {
            if (stage.Stage == CompiledShaderStageKind.Compute)
            {
                diagnostics.Add(Error(
                    VulkanCompiledShaderStageMappingDiagnosticCodes.UnsupportedComputeStage,
                    "Compute shader stages cannot be mapped to Vulkan graphics pipeline shader stages."));
                continue;
            }

            VulkanShaderStageKind vulkanStage = ToVulkanStage(stage.Stage);
            if (string.IsNullOrWhiteSpace(stage.EntryPoint))
            {
                diagnostics.Add(Error(
                    VulkanCompiledShaderStageMappingDiagnosticCodes.MissingEntryPoint,
                    $"{vulkanStage} shader entry point must not be empty.",
                    vulkanStage));
                continue;
            }

            if (stage.SpirvBytes is null || stage.SpirvBytes.Length == 0)
            {
                diagnostics.Add(Error(
                    VulkanCompiledShaderStageMappingDiagnosticCodes.EmptySpirv,
                    $"{vulkanStage} shader SPIR-V bytes must not be empty.",
                    vulkanStage));
                continue;
            }

            if (stage.SpirvBytes.Length % sizeof(uint) != 0)
            {
                diagnostics.Add(Error(
                    VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvByteLength,
                    $"{vulkanStage} shader SPIR-V byte length must be a multiple of four.",
                    vulkanStage));
                continue;
            }

            uint[] words = ConvertLittleEndianBytesToWords(stage.SpirvBytes);
            if (words.Length == 0 || words[0] != SpirvMagicNumber)
            {
                diagnostics.Add(Error(
                    VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvMagic,
                    $"{vulkanStage} shader SPIR-V must start with magic number 0x07230203.",
                    vulkanStage));
                continue;
            }

            descriptors.Add(new VulkanShaderStageDescriptor(vulkanStage, stage.EntryPoint, words));
        }

        if (diagnostics.Any(x => x.Severity == VulkanCompiledShaderStageMappingDiagnosticSeverity.Error))
        {
            return new VulkanCompiledShaderStageMappingResult([], diagnostics);
        }

        return new VulkanCompiledShaderStageMappingResult(descriptors, diagnostics);
    }

    private static uint[] ConvertLittleEndianBytesToWords(byte[] spirvBytes)
    {
        var words = new uint[spirvBytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            int offset = i * sizeof(uint);
            words[i] = (uint)(spirvBytes[offset]
                | (spirvBytes[offset + 1] << 8)
                | (spirvBytes[offset + 2] << 16)
                | (spirvBytes[offset + 3] << 24));
        }

        return words;
    }

    private static VulkanShaderStageKind ToVulkanStage(CompiledShaderStageKind stage)
        => stage switch
        {
            CompiledShaderStageKind.Vertex => VulkanShaderStageKind.Vertex,
            CompiledShaderStageKind.Fragment => VulkanShaderStageKind.Fragment,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported graphics shader stage."),
        };

    private static VulkanShaderStageKind? ToVulkanStageOrNull(CompiledShaderStageKind stage)
        => stage == CompiledShaderStageKind.Vertex || stage == CompiledShaderStageKind.Fragment
            ? ToVulkanStage(stage)
            : null;

    private static VulkanCompiledShaderStageMappingDiagnostic Error(string code, string message, VulkanShaderStageKind? stage = null)
        => new(code, VulkanCompiledShaderStageMappingDiagnosticSeverity.Error, message, stage);
}
