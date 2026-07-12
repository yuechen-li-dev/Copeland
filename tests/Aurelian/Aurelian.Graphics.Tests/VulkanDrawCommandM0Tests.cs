using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanDrawCommandM0Tests
{
    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsCommandBufferNotRecording()
        => WithCommandBuffer(recordCommandBuffer: false, (plant, commandBuffer) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                new VulkanRenderPassScope(plant.Context.Id, commandBuffer.LeaseId, 0),
                InvalidRequest());

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.CommandBufferNotRecording);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsNoActiveRenderPass()
        => WithCommandBuffer(recordCommandBuffer: true, (plant, commandBuffer) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                new VulkanRenderPassScope(plant.Context.Id, commandBuffer.LeaseId, 0),
                InvalidRequest());

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.NoActiveRenderPass);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsInvalidScope()
        => WithActiveRenderPass((plant, _, _, _, commandBuffer, scope) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                new VulkanRenderPassScope(plant.Context.Id, commandBuffer.LeaseId, scope.ScopeId + 1),
                InvalidRequest());

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.InvalidRenderPassScope);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsDisposedPipeline()
        => WithPipelineResources((plant, _, _, _, commandBuffer, scope, pipeline, vertexBuffer) =>
        {
            pipeline.Dispose();
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                new VulkanDrawVerticesRequest(pipeline, vertexBuffer, 3, 0, ValidViewport));

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.PipelineDisposed);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsDisposedVertexBuffer()
        => WithActiveRenderPassAndBuffer(VulkanBufferUsage.Vertex, (plant, _, _, _, commandBuffer, scope, vertexBuffer) =>
        {
            vertexBuffer.Dispose();
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                new VulkanDrawVerticesRequest(null!, vertexBuffer, 3, 0, ValidViewport));

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.VertexBufferDisposed);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsBufferWithoutVertexUsage()
        => WithActiveRenderPassAndBuffer(VulkanBufferUsage.Uniform, (plant, _, _, _, commandBuffer, scope, buffer) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                new VulkanDrawVerticesRequest(null!, buffer, 3, 0, ValidViewport));

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.VertexBufferMissingVertexUsage);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsZeroVertexCount()
        => WithActiveRenderPass((plant, _, _, _, commandBuffer, scope) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                InvalidRequest() with { VertexCount = 0 });

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.InvalidVertexCount);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawRejectsInvalidViewport()
        => WithActiveRenderPass((plant, _, _, _, commandBuffer, scope) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                InvalidRequest() with { ViewportScissor = new VulkanViewportScissor(0, 0, 0, 4) });

            Assert.False(result.Success);
            Assert.Equal(VulkanDrawCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanDrawCommandDiagnosticCodes.InvalidViewport);
        });

    [Fact]
    public void VulkanDrawCommandEncoder_DrawVertices_WhenPlantCreated_RecordsDraw()
        => WithPipelineResources((plant, _, _, _, commandBuffer, scope, pipeline, vertexBuffer) =>
        {
            var encoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult result = encoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                new VulkanDrawVerticesRequest(pipeline, vertexBuffer, 3, 0, ValidViewport));

            Assert.True(result.Success, FormatDiagnostics(result));
        });

    private static VulkanViewportScissor ValidViewport => new(0, 0, 4, 4);

    private static VulkanDrawVerticesRequest InvalidRequest()
        => new(null!, null!, 1, 0, ValidViewport);

    private static void WithCommandBuffer(bool recordCommandBuffer, Action<AurelianVulkanPlant, VulkanCommandBufferLease> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease commandBuffer = commandPool.Rent(completedFenceValue: 0);
            if (recordCommandBuffer)
            {
                VulkanCommandBufferOperationResult begin = commandBuffer.Begin();
                Assert.True(begin.Success, FormatDiagnostics(begin));
            }

            action(init.Plant!, commandBuffer);

            if (commandBuffer.IsRecording)
            {
                VulkanCommandBufferOperationResult end = commandBuffer.End();
                Assert.True(end.Success, FormatDiagnostics(end));
            }
        }
    }

    private static void WithActiveRenderPass(
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease, VulkanRenderPassScope> action)
        => WithRenderPassCommandResources((plant, allocator, renderPass, framebuffer, commandBuffer) =>
        {
            var renderPassEncoder = new VulkanRenderPassCommandEncoder();
            VulkanRenderPassBeginResult begin = renderPassEncoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));
            Assert.True(begin.Success, FormatDiagnostics(begin));

            action(plant, allocator, renderPass, framebuffer, commandBuffer, begin.Scope!.Value);

            VulkanRenderPassCommandResult end = renderPassEncoder.End(plant, commandBuffer, begin.Scope.Value);
            Assert.True(end.Success, FormatDiagnostics(end));
        });

    private static void WithActiveRenderPassAndBuffer(
        VulkanBufferUsage usage,
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease, VulkanRenderPassScope, AurelianVulkanBuffer> action)
        => WithActiveRenderPass((plant, allocator, renderPass, framebuffer, commandBuffer, scope) =>
        {
            VulkanBufferCreateResult bufferResult = CreateBuffer(plant, allocator, usage);
            if (!bufferResult.Success)
            {
                Assert.NotEmpty(bufferResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = bufferResult.Buffer!;
            action(plant, allocator, renderPass, framebuffer, commandBuffer, scope, buffer);
        });

    private static void WithPipelineResources(
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease, VulkanRenderPassScope, AurelianVulkanGraphicsPipeline, AurelianVulkanBuffer> action)
        => WithActiveRenderPassAndBuffer(VulkanBufferUsage.Vertex, (plant, allocator, renderPass, framebuffer, commandBuffer, scope, vertexBuffer) =>
        {
            VulkanGraphicsPipelineCreateResult pipelineResult = VulkanGraphicsPipelineFactory.Create(
                plant,
                renderPass,
                InvalidSpirvDescriptor());
            using (pipelineResult.Pipeline)
            {
                if (!pipelineResult.Success)
                {
                    Assert.NotEmpty(pipelineResult.Diagnostics);
                    return;
                }

                action(plant, allocator, renderPass, framebuffer, commandBuffer, scope, pipelineResult.Pipeline!, vertexBuffer);
            }
        });

    private static void WithRenderPassCommandResources(
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            WithRenderPassResources(init.Plant!, (allocator, renderPass, framebuffer) =>
            {
                using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
                VulkanCommandBufferLease commandBuffer = commandPool.Rent(completedFenceValue: 0);
                VulkanCommandBufferOperationResult begin = commandBuffer.Begin();
                Assert.True(begin.Success, FormatDiagnostics(begin));

                action(init.Plant!, allocator, renderPass, framebuffer, commandBuffer);

                if (commandBuffer.IsRecording)
                {
                    VulkanCommandBufferOperationResult end = commandBuffer.End();
                    Assert.True(end.Success, FormatDiagnostics(end));
                }
            });
        }
    }

    private static void WithRenderPassResources(
        AurelianVulkanPlant plant,
        Action<RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer> action)
    {
        using var allocator = new RawVulkanMemoryAllocator(plant);
        VulkanTextureCreateResult textureResult = CreateColorTexture(plant, allocator);
        if (!textureResult.Success)
        {
            Assert.NotEmpty(textureResult.Diagnostics);
            return;
        }

        VulkanRenderPassCreateResult renderPassResult = CreateRenderPass(plant);
        if (!renderPassResult.Success)
        {
            textureResult.Texture!.Dispose();
            Assert.NotEmpty(renderPassResult.Diagnostics);
            return;
        }

        VulkanFramebufferCreateResult framebufferResult = VulkanFramebufferFactory.Create(
            plant,
            renderPassResult.RenderPass!,
            new VulkanFramebufferDescriptor(4, 4, [textureResult.Texture!]));
        if (!framebufferResult.Success)
        {
            renderPassResult.RenderPass!.Dispose();
            textureResult.Texture!.Dispose();
            Assert.NotEmpty(framebufferResult.Diagnostics);
            return;
        }

        using AurelianVulkanTexture texture = textureResult.Texture!;
        using AurelianVulkanRenderPass renderPass = renderPassResult.RenderPass!;
        using AurelianVulkanFramebuffer framebuffer = framebufferResult.Framebuffer!;
        action(allocator, renderPass, framebuffer);
    }

    private static VulkanTextureCreateResult CreateColorTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                4,
                4,
                VulkanTextureFormat.Rgba8Unorm,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                MipLevels: 1,
                ArrayLayers: 1,
                DebugName: "test.draw.command.texture"));

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

    private static VulkanBufferCreateResult CreateBuffer(
        AurelianVulkanPlant plant,
        RawVulkanMemoryAllocator allocator,
        VulkanBufferUsage usage)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                64,
                usage,
                VulkanMemoryUsage.GpuOnly,
                "test.draw.command.buffer"));

    private static VulkanGraphicsPipelineDescriptor InvalidSpirvDescriptor()
        => new(
            [
                new VulkanShaderStageDescriptor(VulkanShaderStageKind.Vertex, "main", InvalidSpirvWords()),
                new VulkanShaderStageDescriptor(VulkanShaderStageKind.Fragment, "main", InvalidSpirvWords()),
            ],
            [],
            []);

    private static uint[] InvalidSpirvWords()
        => [0x07230203, 0x00010000, 0, 1];

    private static string FormatDiagnostics(VulkanDrawCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassBeginResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
