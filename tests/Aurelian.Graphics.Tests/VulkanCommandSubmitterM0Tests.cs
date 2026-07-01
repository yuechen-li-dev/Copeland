using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanCommandSubmitterM0Tests
{
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000UL;

    [Fact]
    public void VulkanCommandSubmitter_Submit_WhenVulkanUnavailable_SkipsCleanly()
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (init.Success)
            {
                return;
            }

            Assert.NotEmpty(init.Diagnostics);
        }
    }

    [Fact]
    public void VulkanCommandSubmitter_SubmitRejectsMissingCommandBuffer()
        => WithSubmitResources((_, _, _, submitter) =>
        {
            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(null!, DebugName: "missing"));

            Assert.False(result.Success);
            Assert.Equal(VulkanCommandSubmitStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCommandSubmitDiagnosticCodes.CommandBufferMissing);
        });

    [Fact]
    public void VulkanCommandSubmitter_SubmitRejectsCommandBufferNotExecutable()
        => WithSubmitResources((_, pool, _, submitter) =>
        {
            VulkanCommandBufferLease commandBuffer = pool.Rent(completedFenceValue: 0);

            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(commandBuffer, DebugName: "not-executable"));

            Assert.False(result.Success);
            Assert.Equal(VulkanCommandSubmitStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable);
        });

    [Fact]
    public void VulkanCommandSubmitter_SubmitRejectsPlantMismatch()
    {
        VulkanInitResult firstInit = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (firstInit.Plant)
        {
            if (!firstInit.Success)
            {
                Assert.NotEmpty(firstInit.Diagnostics);
                return;
            }

            VulkanInitResult secondInit = VulkanPlantInitializer.CreatePlant(new PlantId(1), new VulkanPlantOptions(EnableValidation: false));
            using (secondInit.Plant)
            {
                if (!secondInit.Success)
                {
                    Assert.NotEmpty(secondInit.Diagnostics);
                    return;
                }

                using var firstFences = VulkanFenceBundle.Create(firstInit.Plant!);
                using var firstPool = VulkanCommandBufferPool.Create(firstInit.Plant!);
                using var firstSubmitter = new VulkanCommandSubmitter(firstInit.Plant!, firstPool, firstFences);
                using var secondPool = VulkanCommandBufferPool.Create(secondInit.Plant!);
                VulkanCommandBufferLease commandBuffer = secondPool.Rent(completedFenceValue: 0);
                Assert.True(commandBuffer.Begin().Success);
                Assert.True(commandBuffer.End().Success);

                VulkanCommandSubmitResult result = firstSubmitter.Submit(new VulkanCommandSubmitRequest(commandBuffer, DebugName: "plant-mismatch"));

                Assert.False(result.Success);
                Assert.Equal(VulkanCommandSubmitStatus.Rejected, result.Status);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCommandSubmitDiagnosticCodes.PlantMismatch);
            }
        }
    }

    [Fact]
    public void VulkanCommandSubmitter_SubmitRejectsActiveRenderPass()
        => WithActiveRenderPass((plant, commandBuffer, scope, encoder, submitter) =>
        {
            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(commandBuffer, DebugName: "active-render-pass"));

            Assert.False(result.Success);
            Assert.Equal(VulkanCommandSubmitStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable);

            VulkanRenderPassCommandResult end = encoder.End(plant, commandBuffer, scope);
            Assert.True(end.Success, FormatDiagnostics(end));
        });

    [Fact]
    public void VulkanCommandSubmitter_SubmitExecutableCommandBuffer_WhenPlantCreated_SignalsFenceAndRetires()
        => WithSubmitResources((_, pool, fences, submitter) =>
        {
            VulkanCommandBufferLease commandBuffer = pool.Rent(completedFenceValue: 0);
            VulkanCommandBufferOperationResult begin = commandBuffer.Begin();
            VulkanCommandBufferOperationResult end = commandBuffer.End();
            Assert.True(begin.Success, FormatDiagnostics(begin));
            Assert.True(end.Success, FormatDiagnostics(end));

            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(commandBuffer, DebugName: "empty-submit"));

            Assert.True(result.Success, FormatDiagnostics(result));
            Assert.NotNull(result.SignalFenceValue);
            VulkanFenceOperationResult completed = fences.CommandListFence.QueryCompletedValue();
            Assert.True(completed.Success, FormatDiagnostics(completed));
            Assert.True(completed.Value >= result.SignalFenceValue.Value);

            VulkanCommandBufferLease reused = pool.Rent(completed.Value!.Value);
            Assert.Same(commandBuffer, reused);
            Assert.True(reused.IsReady);
        });

    [Fact]
    public void VulkanCommandSubmitter_SubmitWithoutWait_ReturnsSignalValue()
        => WithSubmitResources((_, pool, fences, submitter) =>
        {
            VulkanCommandBufferLease commandBuffer = pool.Rent(completedFenceValue: 0);
            Assert.True(commandBuffer.Begin().Success);
            Assert.True(commandBuffer.End().Success);

            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(
                commandBuffer,
                WaitForCompletion: false,
                DebugName: "empty-submit-no-wait"));

            Assert.True(result.Success, FormatDiagnostics(result));
            Assert.NotNull(result.SignalFenceValue);
            VulkanFenceOperationResult wait = fences.CommandListFence.WaitForValue(result.SignalFenceValue.Value, FenceWaitTimeoutNanoseconds);
            Assert.True(wait.Success, FormatDiagnostics(wait));
        });

    [Fact]
    public void VulkanCommandSubmitter_Dispose_IsIdempotent()
        => WithSubmitResources((_, _, _, submitter) =>
        {
            submitter.Dispose();
            submitter.Dispose();
        });

    [Fact]
    public void VulkanCommandSubmitter_SubmitAfterDispose_ReturnsDisposedDiagnostic()
        => WithSubmitResources((_, pool, _, submitter) =>
        {
            VulkanCommandBufferLease commandBuffer = pool.Rent(completedFenceValue: 0);
            submitter.Dispose();

            VulkanCommandSubmitResult result = submitter.Submit(new VulkanCommandSubmitRequest(commandBuffer, DebugName: "disposed"));

            Assert.False(result.Success);
            Assert.Equal(VulkanCommandSubmitStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCommandSubmitDiagnosticCodes.SubmitterDisposed);
        });

    private static void WithSubmitResources(Action<AurelianVulkanPlant, VulkanCommandBufferPool, VulkanFenceBundle, VulkanCommandSubmitter> action)
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var fences = VulkanFenceBundle.Create(init.Plant!);
            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            using var submitter = new VulkanCommandSubmitter(init.Plant!, pool, fences);
            action(init.Plant!, pool, fences, submitter);
        }
    }

    private static void WithActiveRenderPass(Action<AurelianVulkanPlant, VulkanCommandBufferLease, VulkanRenderPassScope, VulkanRenderPassCommandEncoder, VulkanCommandSubmitter> action)
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            AurelianVulkanPlant plant = init.Plant!;
            using var allocator = new RawVulkanMemoryAllocator(plant);
            using var fences = VulkanFenceBundle.Create(plant);
            using var pool = VulkanCommandBufferPool.Create(plant);
            using var submitter = new VulkanCommandSubmitter(plant, pool, fences);
            using AurelianVulkanTexture texture = CreateColorTexture(plant, allocator);
            using AurelianVulkanRenderPass renderPass = CreateRenderPass(plant);
            using AurelianVulkanFramebuffer framebuffer = CreateFramebuffer(plant, renderPass, texture);
            VulkanCommandBufferLease commandBuffer = pool.Rent(completedFenceValue: 0);
            VulkanCommandBufferOperationResult beginCommand = commandBuffer.Begin();
            Assert.True(beginCommand.Success, FormatDiagnostics(beginCommand));

            var encoder = new VulkanRenderPassCommandEncoder();
            VulkanRenderPassBeginResult beginRenderPass = encoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, VulkanColorClearValue.OpaqueBlack));
            Assert.True(beginRenderPass.Success, FormatDiagnostics(beginRenderPass));

            action(plant, commandBuffer, beginRenderPass.Scope!.Value, encoder, submitter);

            if (commandBuffer.IsRecording)
            {
                VulkanCommandBufferOperationResult endCommand = commandBuffer.End();
                Assert.True(endCommand.Success, FormatDiagnostics(endCommand));
            }
        }
    }

    private static AurelianVulkanTexture CreateColorTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
    {
        VulkanTextureCreateResult result = VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                Width: 4,
                Height: 4,
                VulkanTextureFormat.Rgba8Unorm,
                VulkanTextureUsage.ColorAttachment,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                DebugName: "a47.submit.active-render-pass.texture"));

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.Texture!;
    }

    private static AurelianVulkanRenderPass CreateRenderPass(AurelianVulkanPlant plant)
    {
        VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
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

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.RenderPass!;
    }

    private static AurelianVulkanFramebuffer CreateFramebuffer(AurelianVulkanPlant plant, AurelianVulkanRenderPass renderPass, AurelianVulkanTexture texture)
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            renderPass,
            new VulkanFramebufferDescriptor(4, 4, [texture]));

        Assert.True(result.Success, FormatDiagnostics(result));
        return result.Framebuffer!;
    }

    private static string FormatDiagnostics(VulkanCommandSubmitResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanCommandBufferOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFenceOperationResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanTextureCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanFramebufferCreateResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassBeginResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(VulkanRenderPassCommandResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
