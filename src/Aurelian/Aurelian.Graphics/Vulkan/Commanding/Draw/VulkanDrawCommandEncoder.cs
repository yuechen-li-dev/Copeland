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
            if (request.PreviousVertexBuffer is { } previous)
            {
                NativeBuffer previousBuffer = previous.NativeBuffer;
                plant.Vk.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 1, 1, &previousBuffer, &offset);
            }
            if (request.InstanceBuffer is { } instances)
            {
                NativeBuffer instanceBuffer = instances.NativeBuffer;
                plant.Vk.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 1, 1, &instanceBuffer, &offset);
            }
            if (request.IndexBuffer is { } indices)
            {
                plant.Vk.CmdBindIndexBuffer(commandBuffer.CommandBuffer, indices.NativeBuffer, 0, IndexType.Uint32);
                if (request.IndirectArguments is { } arguments)
                {
                    plant.Vk.CmdDrawIndexedIndirectCount(commandBuffer.CommandBuffer, arguments.NativeBuffer, 0,
                        request.IndirectCount!.NativeBuffer, 0, request.MaximumIndirectDraws, 20);
                }
                else
                {
                    plant.Vk.CmdDrawIndexed(commandBuffer.CommandBuffer, request.IndexCount,
                        request.InstanceCount, 0, (int)request.FirstVertex, request.FirstInstance);
                }
            }
            else
            {
                plant.Vk.CmdDraw(commandBuffer.CommandBuffer, request.VertexCount,
                    request.InstanceCount, request.FirstVertex, request.FirstInstance);
            }

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

        var previousLayout = request.Pipeline?.Descriptor.VertexBuffers.SingleOrDefault(layout => layout.Binding == 1);
        if (request.PreviousVertexBuffer is { } previous)
        {
            ulong endVertex = (ulong)request.FirstVertex + request.VertexCount;
            bool invalid = previousLayout is null || previous.PlantId != plantId || previous.IsDisposed
                || (previous.Usage & VulkanBufferUsage.Vertex) == 0
                || (previousLayout is not null && previous.SizeBytes < endVertex * previousLayout.Stride);
            if (invalid)
            {
                diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.VertexBufferMissing,
                    "Previous vertex stream requires binding one, a live buffer on this plant, and capacity for the requested vertices.", plantId));
            }
        }
        else if (previousLayout is { PerInstance: false })
        {
            diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.VertexBufferMissing,
                "This pipeline requires a previous-position vertex stream at binding one.", plantId));
        }

        if (request.InstanceBuffer is { } instances)
        {
            ulong endInstance = (ulong)request.FirstInstance + request.InstanceCount;
            if (previousLayout is not { PerInstance: true } || request.PreviousVertexBuffer is not null
                || instances.PlantId != plantId || instances.IsDisposed
                || (instances.Usage & VulkanBufferUsage.Vertex) == 0 || request.InstanceCount == 0
                || instances.SizeBytes < endInstance * previousLayout.Stride)
            {
                diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.VertexBufferMissing,
                    "Instance draws require a live instance-rate binding one and sufficient instance capacity.", plantId));
            }
        }
        else if (previousLayout is { PerInstance: true })
        {
            diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.VertexBufferMissing,
                "The instance-rate vertex binding requires an instance buffer.", plantId));
        }

        if (request.IndexBuffer is { } indices && (indices.PlantId != plantId || indices.IsDisposed
            || (indices.Usage & VulkanBufferUsage.Index) == 0 || request.IndexCount == 0
            || (ulong)request.IndexCount * 4 > indices.SizeBytes))
        {
            diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.InvalidVertexCount,
                "Indexed draws require a live uint32 index buffer with sufficient capacity.", plantId));
        }

        if (request.VertexCount == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanDrawCommandDiagnosticCodes.InvalidVertexCount,
                "Draw vertices requires VertexCount greater than zero.",
                plantId));
        }

        if (request.IndirectArguments is not null || request.IndirectCount is not null)
        {
            bool ValidIndirect(AurelianVulkanBuffer? buffer, ulong bytes) => buffer is not null
                && buffer.PlantId == plantId && !buffer.IsDisposed && (buffer.Usage & VulkanBufferUsage.Indirect) != 0
                && buffer.SizeBytes >= bytes;
            if (!plant.Facts.IndirectDrawingEnabled || request.IndexBuffer is null || request.MaximumIndirectDraws == 0
                || !ValidIndirect(request.IndirectArguments, (ulong)request.MaximumIndirectDraws * 20)
                || !ValidIndirect(request.IndirectCount, 4))
            {
                diagnostics.Add(Diagnostic(VulkanDrawCommandDiagnosticCodes.InvalidVertexCount,
                    "Indirect-count draws require enabled device features and live, sufficiently sized argument/count buffers.", plantId));
            }
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
