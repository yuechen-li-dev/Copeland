using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static class VulkanCompiledGraphicsPipelineDescriptorFactory
{
    public static VulkanCompiledGraphicsPipelineDescriptorResult CreateDescriptor(
        CompiledShaderProgram program,
        IReadOnlyList<VulkanVertexBufferLayoutDescriptor> vertexBuffers,
        IReadOnlyList<VulkanVertexAttributeDescriptor> vertexAttributes,
        bool enableDepthTest = false,
        bool enableDepthWrite = false)
    {
        List<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics = [];

        if (program is null)
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.ProgramMissing,
                "Compiled shader program must not be null."));
            return RejectedDescriptor(diagnostics);
        }

        IReadOnlyList<CompiledShaderStage>? stages = program.Stages;
        if (stages is null || !stages.Any(stage => stage?.Stage == CompiledShaderStageKind.Vertex))
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingVertexStage,
                "Graphics pipeline M0 requires one compiled vertex shader stage.",
                VulkanShaderStageKind.Vertex));
        }

        if (stages is null || !stages.Any(stage => stage?.Stage == CompiledShaderStageKind.Fragment))
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingFragmentStage,
                "Graphics pipeline M0 requires one compiled fragment shader stage.",
                VulkanShaderStageKind.Fragment));
        }

        if (stages is not null && stages.Any(stage => stage?.Stage == CompiledShaderStageKind.Compute))
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.UnsupportedComputeStage,
                "Compute shader stages cannot be consumed by graphics pipeline M0."));
        }

        if (stages is not null)
        {
            foreach (IGrouping<CompiledShaderStageKind, CompiledShaderStage> duplicateStage in stages
                         .Where(stage => stage is not null)
                         .GroupBy(stage => stage.Stage)
                         .Where(group => group.Count() > 1))
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.DuplicateStage,
                    $"Duplicate compiled shader stage '{duplicateStage.Key}' is not supported by graphics pipeline M0.",
                    ToVulkanStageOrNull(duplicateStage.Key)));
            }
        }

        ValidateVertexInput(vertexBuffers, vertexAttributes, diagnostics);

        if (HasErrors(diagnostics))
        {
            return RejectedDescriptor(diagnostics);
        }

        VulkanCompiledShaderStageMappingResult mappingResult;
        try
        {
            mappingResult = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(program);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.DescriptorCreationFailed,
                $"Compiled shader stage mapping failed: {exception.Message}"));
            return new VulkanCompiledGraphicsPipelineDescriptorResult(VulkanCompiledGraphicsPipelineStatus.Failed, null, diagnostics);
        }

        diagnostics.AddRange(mappingResult.Diagnostics.Select(ToCompiledPipelineDiagnostic));
        if (!mappingResult.Success || HasErrors(diagnostics))
        {
            return RejectedDescriptor(diagnostics);
        }

        VulkanGraphicsPipelineDescriptor descriptor = new(
            mappingResult.Stages,
            vertexBuffers,
            vertexAttributes,
            enableDepthTest,
            enableDepthWrite);

        return new VulkanCompiledGraphicsPipelineDescriptorResult(
            VulkanCompiledGraphicsPipelineStatus.Created,
            descriptor,
            diagnostics);
    }

    public static VulkanCompiledGraphicsPipelineCreateResult CreatePipeline(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        CompiledShaderProgram program,
        IReadOnlyList<VulkanVertexBufferLayoutDescriptor> vertexBuffers,
        IReadOnlyList<VulkanVertexAttributeDescriptor> vertexAttributes,
        bool enableDepthTest = false,
        bool enableDepthWrite = false)
    {
        VulkanCompiledGraphicsPipelineDescriptorResult descriptorResult = CreateDescriptor(
            program,
            vertexBuffers,
            vertexAttributes,
            enableDepthTest,
            enableDepthWrite);

        if (!descriptorResult.Success)
        {
            return new VulkanCompiledGraphicsPipelineCreateResult(
                descriptorResult.Status,
                null,
                descriptorResult.Descriptor,
                descriptorResult.Diagnostics);
        }

        List<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics = [.. descriptorResult.Diagnostics];
        if (plant is null)
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.NativePipelineCreationFailed,
                "Native graphics pipeline creation requires a Vulkan plant."));
            return new VulkanCompiledGraphicsPipelineCreateResult(
                VulkanCompiledGraphicsPipelineStatus.Failed,
                null,
                descriptorResult.Descriptor,
                diagnostics);
        }

        if (renderPass is null)
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.NativePipelineCreationFailed,
                "Native graphics pipeline creation requires a Vulkan render pass."));
            return new VulkanCompiledGraphicsPipelineCreateResult(
                VulkanCompiledGraphicsPipelineStatus.Failed,
                null,
                descriptorResult.Descriptor,
                diagnostics);
        }

        VulkanGraphicsPipelineCreateResult pipelineResult = VulkanGraphicsPipelineFactory.Create(
            plant,
            renderPass,
            descriptorResult.Descriptor!);

        diagnostics.AddRange(pipelineResult.Diagnostics.Select(ToCompiledPipelineDiagnostic));
        if (!pipelineResult.Success)
        {
            VulkanCompiledGraphicsPipelineStatus status = pipelineResult.Status == VulkanGraphicsPipelineStatus.Rejected
                ? VulkanCompiledGraphicsPipelineStatus.Rejected
                : VulkanCompiledGraphicsPipelineStatus.Failed;

            if (!pipelineResult.Diagnostics.Any())
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.NativePipelineCreationFailed,
                    "Native graphics pipeline creation failed without detailed diagnostics."));
            }

            return new VulkanCompiledGraphicsPipelineCreateResult(
                status,
                null,
                descriptorResult.Descriptor,
                diagnostics);
        }

        return new VulkanCompiledGraphicsPipelineCreateResult(
            VulkanCompiledGraphicsPipelineStatus.Created,
            pipelineResult.Pipeline,
            descriptorResult.Descriptor,
            diagnostics);
    }

    private static void ValidateVertexInput(
        IReadOnlyList<VulkanVertexBufferLayoutDescriptor> vertexBuffers,
        IReadOnlyList<VulkanVertexAttributeDescriptor> vertexAttributes,
        List<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics)
    {
        if (vertexBuffers is null || vertexAttributes is null)
        {
            diagnostics.Add(Error(
                VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                "Vertex buffer and attribute descriptor lists must not be null; use empty lists for shader-only input."));
            return;
        }

        HashSet<uint> bindings = [];
        foreach (VulkanVertexBufferLayoutDescriptor vertexBuffer in vertexBuffers)
        {
            if (!bindings.Add(vertexBuffer.Binding))
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Duplicate vertex buffer binding {vertexBuffer.Binding}."));
            }

            if (vertexBuffer.Stride == 0)
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Vertex buffer binding {vertexBuffer.Binding} must have a non-zero stride."));
            }
        }

        HashSet<uint> locations = [];
        foreach (VulkanVertexAttributeDescriptor vertexAttribute in vertexAttributes)
        {
            if (!locations.Add(vertexAttribute.Location))
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Duplicate vertex attribute location {vertexAttribute.Location}."));
            }

            if (!bindings.Contains(vertexAttribute.Binding))
            {
                diagnostics.Add(Error(
                    VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
                    $"Vertex attribute location {vertexAttribute.Location} references unknown binding {vertexAttribute.Binding}."));
            }
        }
    }

    private static VulkanCompiledGraphicsPipelineDescriptorResult RejectedDescriptor(
        IReadOnlyList<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics)
        => new(VulkanCompiledGraphicsPipelineStatus.Rejected, null, diagnostics);

    private static bool HasErrors(IReadOnlyList<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics)
        => diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error);

    private static VulkanCompiledGraphicsPipelineDiagnostic ToCompiledPipelineDiagnostic(
        VulkanCompiledShaderStageMappingDiagnostic diagnostic)
        => new(
            MapStageMappingDiagnosticCode(diagnostic.Code),
            MapSeverity(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.Stage);

    private static VulkanCompiledGraphicsPipelineDiagnostic ToCompiledPipelineDiagnostic(
        VulkanGraphicsPipelineDiagnostic diagnostic)
        => new(
            MapNativePipelineDiagnosticCode(diagnostic.Code),
            MapSeverity(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.Stage);

    private static string MapStageMappingDiagnosticCode(string code)
        => code switch
        {
            VulkanCompiledShaderStageMappingDiagnosticCodes.MissingProgram => VulkanCompiledGraphicsPipelineDiagnosticCodes.ProgramMissing,
            VulkanCompiledShaderStageMappingDiagnosticCodes.MissingStages => VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingVertexStage,
            VulkanCompiledShaderStageMappingDiagnosticCodes.UnsupportedComputeStage => VulkanCompiledGraphicsPipelineDiagnosticCodes.UnsupportedComputeStage,
            VulkanCompiledShaderStageMappingDiagnosticCodes.EmptySpirv => VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidSpirvBytes,
            VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvByteLength => VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidSpirvBytes,
            VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvMagic => VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidSpirvBytes,
            VulkanCompiledShaderStageMappingDiagnosticCodes.DuplicateShaderStage => VulkanCompiledGraphicsPipelineDiagnosticCodes.DuplicateStage,
            _ => VulkanCompiledGraphicsPipelineDiagnosticCodes.DescriptorCreationFailed,
        };

    private static string MapNativePipelineDiagnosticCode(string code)
        => code switch
        {
            VulkanGraphicsPipelineDiagnosticCodes.MissingVertexShader => VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingVertexStage,
            VulkanGraphicsPipelineDiagnosticCodes.MissingFragmentShader => VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingFragmentStage,
            VulkanGraphicsPipelineDiagnosticCodes.DuplicateShaderStage => VulkanCompiledGraphicsPipelineDiagnosticCodes.DuplicateStage,
            VulkanGraphicsPipelineDiagnosticCodes.EmptySpirv => VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidSpirvBytes,
            VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput => VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput,
            _ => VulkanCompiledGraphicsPipelineDiagnosticCodes.NativePipelineCreationFailed,
        };

    private static VulkanCompiledGraphicsPipelineDiagnosticSeverity MapSeverity(VulkanCompiledShaderStageMappingDiagnosticSeverity severity)
        => severity switch
        {
            VulkanCompiledShaderStageMappingDiagnosticSeverity.Error => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error,
            VulkanCompiledShaderStageMappingDiagnosticSeverity.Warning => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Warning,
            VulkanCompiledShaderStageMappingDiagnosticSeverity.Info => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Info,
            _ => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error,
        };

    private static VulkanCompiledGraphicsPipelineDiagnosticSeverity MapSeverity(VulkanGraphicsPipelineDiagnosticSeverity severity)
        => severity switch
        {
            VulkanGraphicsPipelineDiagnosticSeverity.Error => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error,
            VulkanGraphicsPipelineDiagnosticSeverity.Warning => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Warning,
            VulkanGraphicsPipelineDiagnosticSeverity.Info => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Info,
            _ => VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error,
        };

    private static VulkanShaderStageKind? ToVulkanStageOrNull(CompiledShaderStageKind stage)
        => stage switch
        {
            CompiledShaderStageKind.Vertex => VulkanShaderStageKind.Vertex,
            CompiledShaderStageKind.Fragment => VulkanShaderStageKind.Fragment,
            _ => null,
        };

    private static VulkanCompiledGraphicsPipelineDiagnostic Error(string code, string message, VulkanShaderStageKind? stage = null)
        => new(code, VulkanCompiledGraphicsPipelineDiagnosticSeverity.Error, message, stage);
}
