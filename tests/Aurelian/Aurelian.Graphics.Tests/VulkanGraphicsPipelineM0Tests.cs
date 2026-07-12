using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanGraphicsPipelineM0Tests
{
    [Fact]
    public void VulkanGraphicsPipelineFactory_Create_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using AurelianVulkanRenderPass? renderPass = CreateRenderPass(init.Plant!).RenderPass;
            if (renderPass is null)
            {
                return;
            }

            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                init.Plant!,
                renderPass,
                InvalidSpirvDescriptor());

            Assert.False(result.Success);
            Assert.NotEqual(VulkanGraphicsPipelineStatus.Created, result.Status);
            Assert.NotEmpty(result.Diagnostics);
            result.Pipeline?.Dispose();
        }
    }

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsMissingVertexShader()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                Descriptor([FragmentShader()]));

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.MissingVertexShader);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsMissingFragmentShader()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                Descriptor([VertexShader()]));

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.MissingFragmentShader);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsDuplicateShaderStage()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                Descriptor([VertexShader(), VertexShader(), FragmentShader()]));

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.DuplicateShaderStage);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsEmptySpirv()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                Descriptor([VertexShader([]), FragmentShader()]));

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.EmptySpirv);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsInvalidEntryPoint()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                Descriptor([VertexShader(entryPoint: " "), FragmentShader()]));

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.InvalidEntryPoint);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsUnsupportedDepthState()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor() with { EnableDepthTest = true });

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.UnsupportedDepthState);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsInvalidVertexInput()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor() with
                {
                    VertexBuffers = [new VulkanVertexBufferLayoutDescriptor(0, 0)],
                    VertexAttributes = [new VulkanVertexAttributeDescriptor(0, 1, VulkanVertexAttributeFormat.Float2, 0)],
                });

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.InvalidVertexInput);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateRejectsDisposedRenderPass()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            renderPass.Dispose();
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor());

            Assert.False(result.Success);
            Assert.Equal(VulkanGraphicsPipelineStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.RenderPassDisposed);
        });

    [Fact]
    public void VulkanGraphicsPipelineFactory_CreateWithInvalidSpirv_WhenPlantCreated_FailsCleanly()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor());

            Assert.False(result.Success);
            Assert.NotEqual(VulkanGraphicsPipelineStatus.Created, result.Status);
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.ShaderModuleCreationFailed
                || diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.PipelineLayoutCreationFailed
                || diagnostic.Code == VulkanGraphicsPipelineDiagnosticCodes.GraphicsPipelineCreationFailed);
        });

    [Fact]
    public void AurelianVulkanGraphicsPipeline_Dispose_IsIdempotent_WhenCreated()
        => WithPlantAndRenderPass((plant, renderPass) =>
        {
            VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor());
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanGraphicsPipeline pipeline = result.Pipeline!;
            pipeline.Dispose();
            pipeline.Dispose();

            Assert.True(pipeline.IsDisposed);
        });

    private static VulkanGraphicsPipelineDescriptor InvalidSpirvDescriptor()
        => Descriptor([VertexShader(), FragmentShader()]);

    private static VulkanGraphicsPipelineDescriptor Descriptor(IReadOnlyList<VulkanShaderStageDescriptor> shaderStages)
        => new(shaderStages, [], []);

    private static VulkanShaderStageDescriptor VertexShader(IReadOnlyList<uint>? words = null, string entryPoint = "main")
        => new(VulkanShaderStageKind.Vertex, entryPoint, words ?? InvalidSpirvWords());

    private static VulkanShaderStageDescriptor FragmentShader(IReadOnlyList<uint>? words = null, string entryPoint = "main")
        => new(VulkanShaderStageKind.Fragment, entryPoint, words ?? InvalidSpirvWords());

    private static uint[] InvalidSpirvWords()
        => [0x07230203, 0x00010000, 0, 1];

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
}
