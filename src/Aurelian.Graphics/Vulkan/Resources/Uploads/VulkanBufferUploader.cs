using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;
using NativeBuffer = Silk.NET.Vulkan.Buffer;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed unsafe class VulkanBufferUploader : IDisposable
{
    private const ulong UploadWaitTimeoutNanoseconds = 5_000_000_000UL;

    private readonly AurelianVulkanPlant plant;
    private readonly IVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commandBufferPool;
    private readonly VulkanFenceBundle fences;
    private bool disposed;

    public VulkanBufferUploader(
        AurelianVulkanPlant plant,
        IVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commandBufferPool,
        VulkanFenceBundle fences)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commandBufferPool);
        ArgumentNullException.ThrowIfNull(fences);

        PlantId plantId = plant.Context.Id;
        if (allocator.PlantId != plantId || commandBufferPool.PlantId != plantId || fences.CommandListFence.PlantId != plantId)
        {
            throw new ArgumentException("Uploader plant, allocator, command buffer pool, and command-list fence must belong to the same plant.");
        }

        this.plant = plant;
        this.allocator = allocator;
        this.commandBufferPool = commandBufferPool;
        this.fences = fences;
        PlantId = plantId;
    }

    public PlantId PlantId { get; }

    public VulkanBufferUploadResult Upload(VulkanBufferUploadRequest request)
    {
        List<VulkanBufferUploadDiagnostic> diagnostics = [];
        if (disposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.UploaderDisposed,
                "Cannot upload through a disposed Vulkan buffer uploader.",
                request?.DebugName));
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, null, diagnostics);
        }

        Validate(request, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, null, diagnostics);
        }

        AurelianVulkanBuffer? staging = null;
        VulkanCommandBufferLease? lease = null;
        ulong? signalValue = null;
        bool submitted = false;

        try
        {
            VulkanBufferCreateResult stagingResult = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(
                    PlantId,
                    (ulong)request.Data.Length,
                    VulkanBufferUsage.TransferSource,
                    VulkanMemoryUsage.CpuToGpu,
                    string.IsNullOrWhiteSpace(request.DebugName) ? "upload.staging" : $"{request.DebugName}.staging",
                    MapOnCreate: true));

            if (!stagingResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.StagingBufferCreationFailed,
                    FormatStagingCreationFailure(stagingResult),
                    request.DebugName));
                return new VulkanBufferUploadResult(MapFailedStatus(stagingResult.Status), null, diagnostics);
            }

            staging = stagingResult.Buffer!;
            VulkanBufferWriteResult writeResult = staging.Write(request.Data.Span);
            if (!writeResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.StagingBufferWriteFailed,
                    FormatStagingWriteFailure(writeResult),
                    request.DebugName));
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            VulkanFenceOperationResult completedResult = fences.CommandListFence.QueryCompletedValue();
            if (!completedResult.Success || completedResult.Value is null)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.FenceSignalValueUnavailable,
                    FormatFenceFailure("Command-list fence completed value query failed", completedResult),
                    request.DebugName));
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            lease = commandBufferPool.Rent(completedResult.Value.Value);
            VulkanCommandBufferOperationResult beginResult = lease.Begin();
            if (!beginResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.CommandBufferBeginFailed,
                    FormatCommandBufferFailure("Vulkan command buffer begin failed", beginResult),
                    request.DebugName));
                _ = lease.Reset();
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            RecordCopy(lease.CommandBuffer, staging.NativeBuffer, request.Destination.NativeBuffer, request.DestinationOffset, (ulong)request.Data.Length);

            VulkanCommandBufferOperationResult endResult = lease.End();
            if (!endResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.CommandBufferEndFailed,
                    FormatCommandBufferFailure("Vulkan command buffer end failed", endResult),
                    request.DebugName));
                _ = lease.Reset();
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            signalValue = fences.CommandListFence.AllocateSignalValue();
            Result submitResult = Submit(lease.CommandBuffer, fences.CommandListFence.Semaphore, signalValue.Value);
            if (submitResult != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.QueueSubmitFailed,
                    $"vkQueueSubmit failed with result {submitResult}.",
                    request.DebugName));
                _ = lease.Reset();
                signalValue = null;
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            submitted = true;
            VulkanFenceOperationResult waitResult = fences.CommandListFence.WaitForValue(signalValue.Value, UploadWaitTimeoutNanoseconds);
            if (!waitResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.QueueSubmitFailed,
                    FormatFenceFailure($"Timed out or failed waiting for command-list fence value {signalValue.Value}", waitResult),
                    request.DebugName));
                _ = plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, signalValue, diagnostics);
            }

            commandBufferPool.Retire(lease, signalValue.Value);
            lease = null;
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Submitted, signalValue, diagnostics);
        }
        catch (ObjectDisposedException ex)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.UploaderDisposed,
                $"Upload encountered disposed Vulkan upload dependency: {ex.Message}",
                request.DebugName));
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, signalValue, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.QueueSubmitFailed,
                $"Unexpected Vulkan buffer upload failure: {ex.Message}",
                request.DebugName));
            if (submitted)
            {
                _ = plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
            }

            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, signalValue, diagnostics);
        }
        finally
        {
            if (lease is not null && !submitted)
            {
                _ = lease.Reset();
            }

            staging?.Dispose();
        }
    }

    public void Dispose()
    {
        disposed = true;
    }

    private void Validate(VulkanBufferUploadRequest request, List<VulkanBufferUploadDiagnostic> diagnostics)
    {
        if (request is null || request.Destination is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.PlantMismatch,
                "Upload destination buffer is required.",
                request?.DebugName));
            return;
        }

        if (request.Data.IsEmpty)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.EmptyUpload,
                "Upload data must contain at least one byte.",
                request.DebugName));
        }

        if (request.Destination.PlantId != PlantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.PlantMismatch,
                $"Upload destination belongs to plant {request.Destination.PlantId}, not uploader plant {PlantId}.",
                request.DebugName));
        }

        if ((request.Destination.Usage & VulkanBufferUsage.TransferDestination) == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.DestinationMissingTransferDestinationUsage,
                "Upload destination buffer must include TransferDestination usage.",
                request.DebugName));
        }

        ulong byteCount = (ulong)request.Data.Length;
        if (request.DestinationOffset > request.Destination.SizeBytes || byteCount > request.Destination.SizeBytes - request.DestinationOffset)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.UploadOutOfBounds,
                "Upload range exceeds the destination buffer size.",
                request.DebugName));
        }
    }

    private void RecordCopy(
        CommandBuffer commandBuffer,
        NativeBuffer source,
        NativeBuffer destination,
        ulong destinationOffset,
        ulong sizeBytes)
    {
        BufferCopy copy = new()
        {
            SrcOffset = 0,
            DstOffset = destinationOffset,
            Size = sizeBytes,
        };

        plant.Vk.CmdCopyBuffer(commandBuffer, source, destination, 1, &copy);
    }

    private Result Submit(CommandBuffer commandBuffer, VulkanSemaphore signalSemaphore, ulong signalValue)
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

    private VulkanBufferUploadDiagnostic Diagnostic(string code, string message, string? debugName)
        => new(code, message, PlantId, string.IsNullOrWhiteSpace(debugName) ? null : debugName);

    private static VulkanBufferUploadStatus MapFailedStatus(VulkanBufferStatus status)
        => status == VulkanBufferStatus.Rejected ? VulkanBufferUploadStatus.Rejected : VulkanBufferUploadStatus.Failed;

    private static string FormatStagingCreationFailure(VulkanBufferCreateResult result)
        => FormatDiagnostics("Staging buffer creation failed through VulkanBufferFactory", result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatStagingWriteFailure(VulkanBufferWriteResult result)
        => FormatDiagnostics("Staging buffer write failed", result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatCommandBufferFailure(string prefix, VulkanCommandBufferOperationResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatFenceFailure(string prefix, VulkanFenceOperationResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatDiagnostics(string prefix, IEnumerable<string> diagnostics)
    {
        string details = string.Join("; ", diagnostics);
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix}: {details}";
    }
}
