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
        return Upload(request, null);
    }

    // The compute owner may retain staging after this synchronous operation.
    // Timeline completion (or queue idle on failure) precedes reuse by the caller.
    internal VulkanBufferUploadResult Upload(VulkanBufferUploadRequest request, AurelianVulkanBuffer? reusableStaging)
    {
        return Upload(request?.Destination, (request?.Data ?? default).Span,
            request?.DestinationOffset ?? 0, request?.DebugName, reusableStaging);
    }

    internal VulkanBufferUploadResult Upload(AurelianVulkanBuffer? destination, ReadOnlySpan<byte> data,
        ulong destinationOffset, string? debugName, AurelianVulkanBuffer? reusableStaging)
    {
        List<VulkanBufferUploadDiagnostic> diagnostics = [];
        if (disposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.UploaderDisposed,
                "Cannot upload through a disposed Vulkan buffer uploader.",
                debugName));
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, null, diagnostics);
        }

        if (destination is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.PlantMismatch,
                "Upload destination buffer is required.",
                debugName));
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, null, diagnostics);
        }

        Validate(destination, data.Length, destinationOffset, debugName, diagnostics);
        if (diagnostics.Count == 0 && reusableStaging is not null && (reusableStaging.IsDisposed
            || reusableStaging.PlantId != PlantId || !reusableStaging.CanWrite
            || (reusableStaging.Usage & VulkanBufferUsage.TransferSource) == 0
            || reusableStaging.SizeBytes < (ulong)data.Length))
        {
            diagnostics.Add(Diagnostic(VulkanBufferUploadDiagnosticCodes.StagingBufferCreationFailed,
                "Reusable staging must be writable, live, large enough and owned by this plant.", debugName));
        }
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
            if (reusableStaging is not null)
            {
                staging = reusableStaging;
            }
            else
            {
                VulkanBufferCreateResult stagingResult = VulkanBufferFactory.Create(
                    plant,
                    allocator,
                    new VulkanBufferCreatePlan(
                        PlantId,
                        (ulong)data.Length,
                        VulkanBufferUsage.TransferSource,
                        VulkanMemoryUsage.CpuToGpu,
                        string.IsNullOrWhiteSpace(debugName) ? "upload.staging" : $"{debugName}.staging",
                        MapOnCreate: true));

                if (!stagingResult.Success)
                {
                    diagnostics.Add(Diagnostic(
                        VulkanBufferUploadDiagnosticCodes.StagingBufferCreationFailed,
                        FormatStagingCreationFailure(stagingResult),
                        debugName));
                    return new VulkanBufferUploadResult(MapFailedStatus(stagingResult.Status), null, diagnostics);
                }

                staging = stagingResult.Buffer!;
            }
            VulkanBufferWriteResult writeResult = staging.Write(data);
            if (!writeResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.StagingBufferWriteFailed,
                    FormatStagingWriteFailure(writeResult),
                    debugName));
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            VulkanFenceOperationResult completedResult = fences.CommandListFence.QueryCompletedValue();
            if (!completedResult.Success || completedResult.Value is null)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.FenceSignalValueUnavailable,
                    FormatFenceFailure("Command-list fence completed value query failed", completedResult),
                    debugName));
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            lease = commandBufferPool.Rent(completedResult.Value.Value);
            VulkanCommandBufferOperationResult beginResult = lease.Begin();
            if (!beginResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.CommandBufferBeginFailed,
                    FormatCommandBufferFailure("Vulkan command buffer begin failed", beginResult),
                    debugName));
                _ = lease.Reset();
                return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Failed, null, diagnostics);
            }

            RecordCopy(lease.CommandBuffer, staging.NativeBuffer, destination.NativeBuffer, destinationOffset, (ulong)data.Length);

            VulkanCommandBufferOperationResult endResult = lease.End();
            if (!endResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferUploadDiagnosticCodes.CommandBufferEndFailed,
                    FormatCommandBufferFailure("Vulkan command buffer end failed", endResult),
                    debugName));
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
                    debugName));
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
                    debugName));
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
                debugName));
            return new VulkanBufferUploadResult(VulkanBufferUploadStatus.Rejected, signalValue, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.QueueSubmitFailed,
                $"Unexpected Vulkan buffer upload failure: {ex.Message}",
                debugName));
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

            if (reusableStaging is null)
            {
                staging?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
    }

    private void Validate(AurelianVulkanBuffer destination, int length, ulong destinationOffset, string? debugName,
        List<VulkanBufferUploadDiagnostic> diagnostics)
    {
        if (length == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.EmptyUpload,
                "Upload data must contain at least one byte.",
                debugName));
        }

        if (destination.PlantId != PlantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.PlantMismatch,
                $"Upload destination belongs to plant {destination.PlantId}, not uploader plant {PlantId}.",
                debugName));
        }

        if ((destination.Usage & VulkanBufferUsage.TransferDestination) == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.DestinationMissingTransferDestinationUsage,
                "Upload destination buffer must include TransferDestination usage.",
                debugName));
        }

        ulong byteCount = (ulong)length;
        if (destinationOffset > destination.SizeBytes || byteCount > destination.SizeBytes - destinationOffset)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferUploadDiagnosticCodes.UploadOutOfBounds,
                "Upload range exceeds the destination buffer size.",
                debugName));
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
