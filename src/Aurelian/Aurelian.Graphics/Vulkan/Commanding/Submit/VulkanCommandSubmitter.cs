using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Aurelian.Graphics.Vulkan.Commanding.Submit;

public sealed unsafe class VulkanCommandSubmitter : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanCommandBufferPool commandBufferPool;
    private readonly VulkanFenceBundle fences;
    private bool disposed;

    public VulkanCommandSubmitter(
        AurelianVulkanPlant plant,
        VulkanCommandBufferPool commandBufferPool,
        VulkanFenceBundle fences)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBufferPool);
        ArgumentNullException.ThrowIfNull(fences);

        PlantId plantId = plant.Context.Id;
        if (commandBufferPool.PlantId != plantId || fences.CommandListFence.PlantId != plantId)
        {
            throw new ArgumentException("Submitter plant, command buffer pool, and command-list fence must belong to the same plant.");
        }

        this.plant = plant;
        this.commandBufferPool = commandBufferPool;
        this.fences = fences;
        PlantId = plantId;
    }

    public PlantId PlantId { get; }

    public ulong LastKnownCompletedFenceValue => fences.CommandListFence.LastKnownCompletedValue;

    public VulkanCommandSubmitResult Submit(VulkanCommandSubmitRequest request)
    {
        List<VulkanCommandSubmitDiagnostic> diagnostics = [];
        string? debugName = request?.DebugName;

        if (disposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.SubmitterDisposed,
                "Cannot submit a Vulkan command buffer through a disposed submitter.",
                debugName));
            return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Rejected, null, diagnostics);
        }

        Validate(request, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Rejected, null, diagnostics);
        }

        VulkanCommandSubmitRequest validRequest = request!;
        ulong signalValue;
        try
        {
            signalValue = fences.CommandListFence.AllocateSignalValue();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.FenceSignalValueUnavailable,
                $"Command-list fence signal value allocation failed: {ex.Message}",
                validRequest.DebugName));
            return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Failed, null, diagnostics);
        }

        Result submitResult = SubmitAndSignal(
            validRequest.CommandBuffer.CommandBuffer,
            fences.CommandListFence.Semaphore,
            signalValue);
        if (submitResult != Result.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.QueueSubmitFailed,
                $"vkQueueSubmit failed with result {submitResult}.",
                validRequest.DebugName));
            return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Failed, signalValue, diagnostics);
        }

        if (validRequest.WaitForCompletion)
        {
            VulkanFenceOperationResult waitResult = fences.CommandListFence.WaitForValue(signalValue, validRequest.TimeoutNanoseconds);
            if (!waitResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanCommandSubmitDiagnosticCodes.FenceWaitFailed,
                    FormatFenceFailure($"Timed out or failed waiting for command-list fence value {signalValue}", waitResult),
                    validRequest.DebugName));
                RetireSubmittedCommandBuffer(validRequest.CommandBuffer, signalValue, validRequest.DebugName, diagnostics);
                return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Failed, signalValue, diagnostics);
            }
        }

        if (!RetireSubmittedCommandBuffer(validRequest.CommandBuffer, signalValue, validRequest.DebugName, diagnostics))
        {
            return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Failed, signalValue, diagnostics);
        }

        return new VulkanCommandSubmitResult(VulkanCommandSubmitStatus.Submitted, signalValue, diagnostics);
    }

    public void Dispose()
    {
        disposed = true;
    }

    private void Validate(VulkanCommandSubmitRequest? request, List<VulkanCommandSubmitDiagnostic> diagnostics)
    {
        if (request?.CommandBuffer is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferMissing,
                "A Vulkan command submit request must include one command buffer lease.",
                request?.DebugName));
            return;
        }

        VulkanCommandBufferLease commandBuffer = request.CommandBuffer;
        if (commandBuffer.PlantId != PlantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.PlantMismatch,
                $"Command buffer lease belongs to plant {commandBuffer.PlantId}, but submitter belongs to plant {PlantId}.",
                request.DebugName));
        }

        if (commandBuffer.IsDisposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable,
                "Cannot submit a disposed Vulkan command buffer lease.",
                request.DebugName));
        }
        else if (commandBuffer.IsRetired)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable,
                "Cannot submit a retired Vulkan command buffer lease.",
                request.DebugName));
        }
        else if (!commandBuffer.IsExecutable)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable,
                "Cannot submit a Vulkan command buffer lease that has not been ended into the executable state.",
                request.DebugName));
        }

        if (commandBuffer.HasActiveRenderPass)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferNotExecutable,
                "Cannot submit a Vulkan command buffer lease while it has an active render pass scope.",
                request.DebugName));
        }
    }


    private bool RetireSubmittedCommandBuffer(
        VulkanCommandBufferLease commandBuffer,
        ulong signalValue,
        string debugName,
        List<VulkanCommandSubmitDiagnostic> diagnostics)
    {
        try
        {
            commandBufferPool.Retire(commandBuffer, signalValue);
            return true;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            diagnostics.Add(Diagnostic(
                VulkanCommandSubmitDiagnosticCodes.CommandBufferRetireFailed,
                $"Command buffer retirement failed after submit: {ex.Message}",
                debugName));
            return false;
        }
    }

    private Result SubmitAndSignal(CommandBuffer commandBuffer, VulkanSemaphore signalSemaphore, ulong signalValue)
    {
        TimelineSemaphoreSubmitInfo timelineInfo = new()
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            SignalSemaphoreValueCount = 1,
            PSignalSemaphoreValues = &signalValue,
        };

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            PNext = &timelineInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
        };

        return plant.Vk.QueueSubmit(plant.GraphicsQueue, 1, &submitInfo, default);
    }

    private VulkanCommandSubmitDiagnostic Diagnostic(string code, string message, string? debugName)
        => new(
            code,
            VulkanCommandSubmitDiagnosticSeverity.Error,
            message,
            PlantId,
            string.IsNullOrWhiteSpace(debugName) ? null : debugName);

    private static string FormatFenceFailure(string prefix, VulkanFenceOperationResult result)
    {
        string details = string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix}: {details}";
    }
}
