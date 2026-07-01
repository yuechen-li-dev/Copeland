using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Silk.NET.Vulkan;
using NativeBuffer = Silk.NET.Vulkan.Buffer;

namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public sealed unsafe class VulkanDrawCommandEncoder
{
    public VulkanDrawCommandResult DrawVertices(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassScope renderPassScope,
        VulkanDrawVerticesRequest request)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBuffer);
        ArgumentNullException.ThrowIfNull(request);

        List<VulkanDrawCommandDiagnostic> diagnostics = [];
        Validate(plant, commandBuffer, renderPassScope, request, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanDrawCommandDiagnosticSeverity.Error))
        {
            return new VulkanDrawCommandResult(VulkanDrawCommandStatus.Rejected, diagnostics);
        }

        try
        {
            Viewport viewport = new()
            {
                X = request.ViewportScissor.X,
                Y = request.ViewportScissor.Y,
                Width = request.ViewportScissor.Width,
                Height = request.ViewportScissor.Height,
                MinDepth = request.ViewportScissor.MinDepth,
                MaxDepth = request.ViewportScissor.MaxDepth,
            };
            Rect2D scissor = new(
                new Offset2D((int)request.ViewportScissor.X, (int)request.ViewportScissor.Y),
                new Extent2D((uint)request.ViewportScissor.Width, (uint)request.ViewportScissor.Height));
            NativeBuffer vertexBuffer = request.VertexBuffer.NativeBuffer;
            ulong offset = 0;

            plant.Vk.CmdSetViewport(commandBuffer.CommandBuffer, 0, 1, &viewport);
            plant.Vk.CmdSetScissor(commandBuffer.CommandBuffer, 0, 1, &scissor);
            plant.Vk.CmdBindPipeline(commandBuffer.CommandBuffer, PipelineBindPoint.Graphics, request.Pipeline.NativePipeline);
            plant.Vk.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 0, 1, &vertexBuffer, &offset);
            plant.Vk.CmdDraw(commandBuffer.CommandBuffer, request.VertexCount, 1, request.FirstVertex, 0);

            return VulkanDrawCommandResult.Recorded(diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.DrawRecordingFailed,
                $"Vulkan draw command recording failed: {exception.Message}",
                plant.Context.Id));
            return new VulkanDrawCommandResult(VulkanDrawCommandStatus.Failed, diagnostics);
        }
    }

    private static void Validate(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassScope renderPassScope,
        VulkanDrawVerticesRequest request,
        List<VulkanDrawCommandDiagnostic> diagnostics)
    {
        PlantId plantId = plant.Context.Id;

        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.PlantMismatch,
                "Cannot record draw commands for a disposed or uninitialized Vulkan plant.",
                plantId));
        }

        if (commandBuffer.PlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.PlantMismatch,
                "Command buffer plant ownership must match the target plant.",
                plantId));
        }

        if (!commandBuffer.IsRecording)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.CommandBufferNotRecording,
                "Draw commands require a command buffer in Recording state.",
                plantId));
        }

        if (!commandBuffer.HasActiveRenderPass)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.NoActiveRenderPass,
                "Draw commands require an active render pass on the command buffer lease.",
                plantId));
        }
        else if (renderPassScope.PlantId != plantId
            || renderPassScope.CommandBufferLeaseId != commandBuffer.LeaseId
            || !commandBuffer.IsActiveScope(renderPassScope))
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.InvalidRenderPassScope,
                "Draw command render pass scope is not active on this command buffer lease.",
                plantId));
        }

        if (request.Pipeline is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.PipelineMissing,
                "Draw vertices requires a graphics pipeline.",
                plantId));
        }

        if (request.VertexBuffer is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.VertexBufferMissing,
                "Draw vertices requires one vertex buffer.",
                plantId));
        }

        if (request.Pipeline is not null)
        {
            if (request.Pipeline.PlantId != plantId)
            {
                diagnostics.Add(Diagnostic(
                    VulkanDrawCommandDiagnosticCodes.PlantMismatch,
                    "Graphics pipeline plant ownership must match the target plant.",
                    plantId));
            }

            if (request.Pipeline.IsDisposed || request.Pipeline.NativePipeline.Handle == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanDrawCommandDiagnosticCodes.PipelineDisposed,
                    "Cannot draw with a disposed or empty graphics pipeline.",
                    plantId));
            }
        }

        if (request.VertexBuffer is not null)
        {
            if (request.VertexBuffer.PlantId != plantId)
            {
                diagnostics.Add(Diagnostic(
                    VulkanDrawCommandDiagnosticCodes.PlantMismatch,
                    "Vertex buffer plant ownership must match the target plant.",
                    plantId));
            }

            if (request.VertexBuffer.IsDisposed || request.VertexBuffer.NativeBuffer.Handle == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanDrawCommandDiagnosticCodes.VertexBufferDisposed,
                    "Cannot draw with a disposed or empty vertex buffer.",
                    plantId));
            }

            if ((request.VertexBuffer.Usage & VulkanBufferUsage.Vertex) == 0)
            {
                diagnostics.Add(Diagnostic(
                    VulkanDrawCommandDiagnosticCodes.VertexBufferMissingVertexUsage,
                    "Draw vertices requires a buffer created with Vertex usage.",
                    plantId));
            }
        }

        if (request.VertexCount == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.InvalidVertexCount,
                "Draw vertices requires VertexCount greater than zero.",
                plantId));
        }

        if (!request.ViewportScissor.IsValid)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.InvalidViewport,
                "Viewport/scissor must have positive width and height with depth bounds in [0, 1].",
                plantId));
        }
    }

    private static VulkanDrawCommandDiagnostic Diagnostic(string code, string message, PlantId plantId)
        => new(code, VulkanDrawCommandDiagnosticSeverity.Error, message, plantId);
}
