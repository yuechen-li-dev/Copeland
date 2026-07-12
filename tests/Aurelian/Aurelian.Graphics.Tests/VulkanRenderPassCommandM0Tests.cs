using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanRenderPassCommandM0Tests
{
    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginEnd_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            WithRenderPassCommandResources(init.Plant!, (_, _, renderPass, framebuffer, commandBuffer) =>
            {
                var encoder = new VulkanRenderPassCommandEncoder();
                VulkanRenderPassBeginResult begin = encoder.Begin(
                    init.Plant!,
                    commandBuffer,
                    new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));
                VulkanRenderPassCommandResult end = encoder.End(init.Plant!, commandBuffer, begin.Scope!.Value);

                Assert.True(begin.Success, FormatDiagnostics(begin));
                Assert.True(end.Success, FormatDiagnostics(end));
            });
        }
    }

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginRejectsCommandBufferNotRecording()
        => WithRenderPassCommandResources(recordCommandBuffer: false, (plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassBeginResult result = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.OpaqueBlack));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.CommandBufferNotRecording);
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginRejectsPlantMismatch()
    {
        var firstInit = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (firstInit.Plant)
        {
            if (!firstInit.Success)
            {
                Assert.NotEmpty(firstInit.Diagnostics);
                return;
            }

            var secondInit = VulkanPlantInitializer.CreatePlant(new PlantId(1), new VulkanPlantOptions(EnableValidation: false));
            using (secondInit.Plant)
            {
                if (!secondInit.Success)
                {
                    Assert.NotEmpty(secondInit.Diagnostics);
                    return;
                }

                WithRenderPassResources(firstInit.Plant!, (allocator, renderPass, framebuffer) =>
                {
                    using var commandPool = VulkanCommandBufferPool.Create(secondInit.Plant!);
                    VulkanCommandBufferLease commandBuffer = commandPool.Rent(completedFenceValue: 0);
                    VulkanCommandBufferOperationResult commandBegin = commandBuffer.Begin();
                    Assert.True(commandBegin.Success, FormatDiagnostics(commandBegin));

                    var encoder = new VulkanRenderPassCommandEncoder();
                    VulkanRenderPassBeginResult result = encoder.Begin(
                        secondInit.Plant!,
                        commandBuffer,
                        new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));

                    VulkanCommandBufferOperationResult commandEnd = commandBuffer.End();
                    Assert.True(commandEnd.Success, FormatDiagnostics(commandEnd));
                    Assert.False(result.Success);
                    Assert.Equal(VulkanRenderPassCommandStatus.Rejected, result.Status);
                    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.PlantMismatch);
                    _ = allocator;
                });
            }
        }
    }

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginRejectsDisposedRenderPass()
        => WithRenderPassCommandResources((plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            renderPass.Dispose();
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassBeginResult result = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.RenderPassDisposed);
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginRejectsDisposedFramebuffer()
        => WithRenderPassCommandResources((plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            framebuffer.Dispose();
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassBeginResult result = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.FramebufferDisposed);
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginRejectsDoubleBegin()
        => WithRenderPassCommandResources((plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            var encoder = new VulkanRenderPassCommandEncoder();
            var request = new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack);

            VulkanRenderPassBeginResult first = encoder.Begin(plant, commandBuffer, request);
            VulkanRenderPassBeginResult second = encoder.Begin(plant, commandBuffer, request);
            VulkanRenderPassCommandResult end = encoder.End(plant, commandBuffer, first.Scope!.Value);

            Assert.True(first.Success, FormatDiagnostics(first));
            Assert.False(second.Success);
            Assert.Equal(VulkanRenderPassCommandStatus.Rejected, second.Status);
            Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.RenderPassAlreadyActive);
            Assert.True(end.Success, FormatDiagnostics(end));
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_EndRejectsNoActiveRenderPass()
        => WithRenderPassCommandResources((plant, _, _, _, commandBuffer) =>
        {
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassCommandResult result = encoder.End(plant, commandBuffer, new VulkanRenderPassScope(plant.Context.Id, commandBuffer.LeaseId, 0));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassCommandStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassCommandDiagnosticCodes.NoActiveRenderPass);
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_End_UpdatesAttachmentLayoutTrackersToFinalLayout()
        => WithRenderPassCommandResources((plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            Assert.Equal(VulkanResourceLayout.Undefined, framebuffer.Descriptor.ColorAttachments[0].LayoutTracker.Get(0, 0));
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassBeginResult begin = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.TransparentBlack));
            VulkanRenderPassCommandResult end = encoder.End(plant, commandBuffer, begin.Scope!.Value);

            Assert.True(begin.Success, FormatDiagnostics(begin));
            Assert.True(end.Success, FormatDiagnostics(end));
            Assert.Equal(VulkanResourceLayout.ColorAttachment, framebuffer.Descriptor.ColorAttachments[0].LayoutTracker.Get(0, 0));
        });

    [Fact]
    public void VulkanRenderPassCommandEncoder_BeginEndRecords_WhenPlantCreated()
        => WithRenderPassCommandResources((plant, _, renderPass, framebuffer, commandBuffer) =>
        {
            var encoder = new VulkanRenderPassCommandEncoder();

            VulkanRenderPassBeginResult begin = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, new VulkanColorClearValue(0.1f, 0.2f, 0.3f, 1.0f)));
            VulkanRenderPassCommandResult end = encoder.End(plant, commandBuffer, begin.Scope!.Value);

            Assert.True(begin.Success, FormatDiagnostics(begin));
            Assert.True(end.Success, FormatDiagnostics(end));
        });

    private static void WithRenderPassCommandResources(
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease> action)
        => WithRenderPassCommandResources(recordCommandBuffer: true, action);

    private static void WithRenderPassCommandResources(
        bool recordCommandBuffer,
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

            WithRenderPassCommandResources(init.Plant!, recordCommandBuffer, action);
        }
    }

    private static void WithRenderPassCommandResources(
        AurelianVulkanPlant plant,
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease> action)
        => WithRenderPassCommandResources(plant, recordCommandBuffer: true, action);

    private static void WithRenderPassCommandResources(
        AurelianVulkanPlant plant,
        bool recordCommandBuffer,
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanFramebuffer, VulkanCommandBufferLease> action)
        => WithRenderPassResources(plant, (allocator, renderPass, framebuffer) =>
        {
            using var commandPool = VulkanCommandBufferPool.Create(plant);
            VulkanCommandBufferLease commandBuffer = commandPool.Rent(completedFenceValue: 0);
            if (recordCommandBuffer)
            {
                VulkanCommandBufferOperationResult begin = commandBuffer.Begin();
                Assert.True(begin.Success, FormatDiagnostics(begin));
            }

            action(plant, allocator, renderPass, framebuffer, commandBuffer);

            if (commandBuffer.IsRecording)
            {
                VulkanCommandBufferOperationResult end = commandBuffer.End();
                Assert.True(end.Success, FormatDiagnostics(end));
            }
        });

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
                DebugName: "test.renderpass.command.texture"));

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

    private static string FormatDiagnostics(VulkanRenderPassBeginResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
