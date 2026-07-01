using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public sealed unsafe class VulkanRenderPassCommandEncoder
{
    private readonly Dictionary<VulkanRenderPassScope, ActiveRenderPassAttachments> activeRenderPasses = [];

    public VulkanRenderPassBeginResult Begin(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassBeginRequest request)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBuffer);
        ArgumentNullException.ThrowIfNull(request);

        List<VulkanRenderPassCommandDiagnostic> diagnostics = [];
        ValidateBegin(plant, commandBuffer, request, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanRenderPassCommandDiagnosticSeverity.Error))
        {
            return new VulkanRenderPassBeginResult(VulkanRenderPassCommandStatus.Rejected, null, diagnostics);
        }

        try
        {
            ClearValue clearValue = ToNative(request.ClearColor);
            RenderPassBeginInfo beginInfo = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = request.RenderPass.NativeRenderPass,
                Framebuffer = request.Framebuffer.NativeFramebuffer,
                RenderArea = new Rect2D(
                    new Offset2D(0, 0),
                    new Extent2D(request.Framebuffer.Width, request.Framebuffer.Height)),
                ClearValueCount = 1,
                PClearValues = &clearValue,
            };

            plant.Vk.CmdBeginRenderPass(commandBuffer.CommandBuffer, &beginInfo, SubpassContents.Inline);
            VulkanRenderPassScope scope = commandBuffer.MarkRenderPassActive(plant.Context.Id);
            activeRenderPasses[scope] = new ActiveRenderPassAttachments(request.RenderPass, request.Framebuffer);
            return VulkanRenderPassBeginResult.Recorded(scope, diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.BeginRenderPassFailed,
                $"vkCmdBeginRenderPass failed: {exception.Message}",
                plant.Context.Id));
            return new VulkanRenderPassBeginResult(VulkanRenderPassCommandStatus.Failed, null, diagnostics);
        }
    }

    public VulkanRenderPassCommandResult End(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassScope scope)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBuffer);

        List<VulkanRenderPassCommandDiagnostic> diagnostics = [];
        ValidateEnd(plant, commandBuffer, scope, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanRenderPassCommandDiagnosticSeverity.Error))
        {
            return new VulkanRenderPassCommandResult(VulkanRenderPassCommandStatus.Rejected, diagnostics);
        }

        try
        {
            plant.Vk.CmdEndRenderPass(commandBuffer.CommandBuffer);
            MarkFinalAttachmentLayouts(scope);
            _ = commandBuffer.TryClearRenderPass(scope);
            return VulkanRenderPassCommandResult.Recorded(diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.EndRenderPassFailed,
                $"vkCmdEndRenderPass failed: {exception.Message}",
                plant.Context.Id));
            return new VulkanRenderPassCommandResult(VulkanRenderPassCommandStatus.Failed, diagnostics);
        }
    }

    private void MarkFinalAttachmentLayouts(VulkanRenderPassScope scope)
    {
        if (!activeRenderPasses.Remove(scope, out ActiveRenderPassAttachments? active))
        {
            return;
        }

        int attachmentCount = Math.Min(
            active.RenderPass.Descriptor.ColorAttachments.Count,
            active.Framebuffer.Descriptor.ColorAttachments.Count);
        for (int i = 0; i < attachmentCount; i++)
        {
            VulkanRenderPassAttachmentDescriptor attachment = active.RenderPass.Descriptor.ColorAttachments[i];
            active.Framebuffer.Descriptor.ColorAttachments[i].LayoutTracker.TryMarkCurrentLayout(0, 0, attachment.FinalLayout);
        }
    }

    private static void ValidateBegin(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassBeginRequest request,
        List<VulkanRenderPassCommandDiagnostic> diagnostics)
    {
        PlantId plantId = plant.Context.Id;
        ValidatePlantAndCommandBuffer(plant, commandBuffer, diagnostics);

        if (commandBuffer.HasActiveRenderPass)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.RenderPassAlreadyActive,
                "Cannot begin a render pass while this command buffer lease already has an active render pass.",
                plantId));
        }

        if (request.RenderPass is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.RenderPassMissing,
                "Render pass begin requires a render pass.",
                plantId));
        }

        if (request.Framebuffer is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.FramebufferMissing,
                "Render pass begin requires a framebuffer.",
                plantId));
        }

        if (request.RenderPass is null || request.Framebuffer is null)
        {
            return;
        }

        if (request.RenderPass.IsDisposed || request.RenderPass.NativeRenderPass.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.RenderPassDisposed,
                "Cannot begin a render pass with a disposed or empty native render pass.",
                plantId));
        }

        if (request.Framebuffer.IsDisposed || request.Framebuffer.NativeFramebuffer.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.FramebufferDisposed,
                "Cannot begin a render pass with a disposed or empty native framebuffer.",
                plantId));
        }

        if (request.RenderPass.PlantId != plantId || request.Framebuffer.PlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.PlantMismatch,
                "Render pass, framebuffer, and command buffer plant ownership must match the target plant.",
                plantId));
        }

        if (!ReferenceEquals(request.Framebuffer.RenderPass, request.RenderPass))
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.RenderPassFramebufferMismatch,
                "Framebuffer was not created for the render pass supplied to begin.",
                plantId));
        }
    }

    private static void ValidateEnd(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassScope scope,
        List<VulkanRenderPassCommandDiagnostic> diagnostics)
    {
        PlantId plantId = plant.Context.Id;
        ValidatePlantAndCommandBuffer(plant, commandBuffer, diagnostics);

        if (!commandBuffer.HasActiveRenderPass)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.NoActiveRenderPass,
                "Cannot end a render pass because the command buffer lease has no active render pass.",
                plantId));
            return;
        }

        if (scope.PlantId != plantId || scope.CommandBufferLeaseId != commandBuffer.LeaseId || !commandBuffer.IsActiveScope(scope))
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.InvalidRenderPassScope,
                "Cannot end a render pass with a scope that is not active on this command buffer lease.",
                plantId));
        }
    }

    private static void ValidatePlantAndCommandBuffer(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        List<VulkanRenderPassCommandDiagnostic> diagnostics)
    {
        PlantId plantId = plant.Context.Id;
        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.PlantMismatch,
                "Cannot record render pass commands for a disposed or uninitialized Vulkan plant.",
                plantId));
        }

        if (commandBuffer.PlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.PlantMismatch,
                "Command buffer plant ownership must match the target plant.",
                plantId));
        }

        if (!commandBuffer.IsRecording)
        {
            diagnostics.Add(Diagnostic(
                VulkanRenderPassCommandDiagnosticCodes.CommandBufferNotRecording,
                "Render pass commands require a command buffer in Recording state.",
                plantId));
        }
    }

    private static ClearValue ToNative(VulkanColorClearValue clearColor)
        => new()
        {
            Color = new ClearColorValue
            {
                Float32_0 = clearColor.R,
                Float32_1 = clearColor.G,
                Float32_2 = clearColor.B,
                Float32_3 = clearColor.A,
            },
        };

    private static VulkanRenderPassCommandDiagnostic Diagnostic(string code, string message, PlantId plantId)
        => new(code, VulkanRenderPassCommandDiagnosticSeverity.Error, message, plantId);

    private sealed record ActiveRenderPassAttachments(
        AurelianVulkanRenderPass RenderPass,
        AurelianVulkanFramebuffer Framebuffer);
}
