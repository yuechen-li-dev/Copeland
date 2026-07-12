using System.Reflection;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanCompiledGraphicsPipelineDescriptorM0Tests
{
    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsMissingProgram()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            null!,
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Null(result.Descriptor);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.ProgramMissing);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsMissingVertexStage()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Fragment)),
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingVertexStage);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsMissingFragmentStage()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex)),
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.MissingFragmentStage);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsComputeStageForGraphicsM0()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment), Stage(CompiledShaderStageKind.Compute)),
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.UnsupportedComputeStage);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsDuplicateStages()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.DuplicateStage);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_MapsVertexAndFragmentStages()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
            [],
            []);

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Created, result.Status);
        Assert.NotNull(result.Descriptor);
        Assert.Collection(
            result.Descriptor!.ShaderStages,
            vertex =>
            {
                Assert.Equal(VulkanShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("main", vertex.EntryPoint);
                Assert.Equal([0x07230203u, 0x00010000u], vertex.SpirvWords);
            },
            fragment => Assert.Equal(VulkanShaderStageKind.Fragment, fragment.Stage));
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_PreservesVertexInput()
    {
        VulkanVertexBufferLayoutDescriptor[] vertexBuffers = [new(0, 20)];
        VulkanVertexAttributeDescriptor[] vertexAttributes =
        [
            new(0, 0, VulkanVertexAttributeFormat.Float2, 0),
            new(1, 0, VulkanVertexAttributeFormat.Float3, 8),
        ];

        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
            vertexBuffers,
            vertexAttributes,
            enableDepthTest: false,
            enableDepthWrite: false);

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.Same(vertexBuffers, result.Descriptor!.VertexBuffers);
        Assert.Same(vertexAttributes, result.Descriptor.VertexAttributes);
        Assert.False(result.Descriptor.EnableDepthTest);
        Assert.False(result.Descriptor.EnableDepthWrite);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsInvalidSpirvBytes()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex, [0x00, 0x00, 0x00, 0x00]), Stage(CompiledShaderStageKind.Fragment)),
            [],
            []);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Null(result.Descriptor);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidSpirvBytes);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreateDescriptor_RejectsInvalidVertexInput()
    {
        VulkanCompiledGraphicsPipelineDescriptorResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
            [new VulkanVertexBufferLayoutDescriptor(0, 0)],
            [new VulkanVertexAttributeDescriptor(0, 1, VulkanVertexAttributeFormat.Float2, 0)]);

        Assert.False(result.Success);
        Assert.Equal(VulkanCompiledGraphicsPipelineStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.InvalidVertexInput);
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_DoesNotReferenceAurelianShaders()
    {
        Assembly assembly = typeof(VulkanCompiledGraphicsPipelineDescriptorFactory).Assembly;

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "Aurelian.Shaders");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is "Microsoft.Direct3D.DXC");
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreatePipeline_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanRenderPassCreateResult renderPassResult = CreateRenderPass(init.Plant!);
            using (renderPassResult.RenderPass)
            {
                if (!renderPassResult.Success)
                {
                    Assert.NotEmpty(renderPassResult.Diagnostics);
                    return;
                }

                VulkanCompiledGraphicsPipelineCreateResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline(
                    init.Plant!,
                    renderPassResult.RenderPass!,
                    Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
                    [],
                    []);

                Assert.False(result.Success);
                Assert.NotEqual(VulkanCompiledGraphicsPipelineStatus.Created, result.Status);
                Assert.NotNull(result.Descriptor);
                Assert.NotEmpty(result.Diagnostics);
                result.Pipeline?.Dispose();
            }
        }
    }

    [Fact]
    public void VulkanCompiledGraphicsPipelineDescriptorFactory_CreatePipeline_WithInvalidSpirv_WhenPlantCreated_FailsCleanly()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanCompiledGraphicsPipelineCreateResult result = VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline(
                plant,
                renderPass,
                Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)),
                [],
                []);

            Assert.False(result.Success);
            Assert.NotEqual(VulkanCompiledGraphicsPipelineStatus.Created, result.Status);
            Assert.NotNull(result.Descriptor);
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompiledGraphicsPipelineDiagnosticCodes.NativePipelineCreationFailed);
        });

    private static CompiledShaderProgram Program(params CompiledShaderStage[] stages)
        => new(CompiledShaderProgram.CurrentFormatVersion, stages);

    private static CompiledShaderStage Stage(CompiledShaderStageKind stage, byte[]? spirvBytes = null)
        => new(stage, "main", Profile(stage), spirvBytes ?? SpirvBytesWithValidMagic(), new string('f', 64), "shader.hlsl");

    private static string Profile(CompiledShaderStageKind stage)
        => stage switch
        {
            CompiledShaderStageKind.Vertex => "vs_6_0",
            CompiledShaderStageKind.Fragment => "ps_6_0",
            CompiledShaderStageKind.Compute => "cs_6_0",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported stage."),
        };

    private static byte[] SpirvBytesWithValidMagic()
        => [0x03, 0x02, 0x23, 0x07, 0x00, 0x00, 0x01, 0x00];

    private static VulkanRenderPassCreateResult CreateRenderPass(AurelianVulkanPlant plant)
        => VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    VulkanTextureFormat.Rgba8Unorm,
                    VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined,
                    VulkanResourceLayout.ColorAttachment),
            ]));

    private static void WithPlantAndRenderPass(Action<AurelianVulkanPlant, AurelianVulkanRenderPass> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanRenderPassCreateResult renderPassResult = CreateRenderPass(init.Plant!);
            if (!renderPassResult.Success)
            {
                Assert.NotEmpty(renderPassResult.Diagnostics);
                return;
            }

            using AurelianVulkanRenderPass renderPass = renderPassResult.RenderPass!;
            action(init.Plant!, renderPass);
        }
    }

    private static string FormatDiagnostics(IReadOnlyList<VulkanCompiledGraphicsPipelineDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
