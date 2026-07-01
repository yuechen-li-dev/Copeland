using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed unsafe class VulkanTextureUploader : IDisposable
{
    private const ulong UploadWaitTimeoutNanoseconds = 5_000_000_000UL;
    private const uint M0BytesPerPixel = 4;

    private readonly AurelianVulkanPlant plant;
    private readonly IVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commandBufferPool;
    private readonly VulkanFenceBundle fences;
    private bool disposed;

    public VulkanTextureUploader(
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
            throw new ArgumentException("Texture uploader plant, allocator, command buffer pool, and command-list fence must belong to the same plant.");
        }

        this.plant = plant;
        this.allocator = allocator;
        this.commandBufferPool = commandBufferPool;
        this.fences = fences;
        PlantId = plantId;
    }

    public PlantId PlantId { get; }

    public VulkanTextureUploadResult Upload(VulkanTextureUploadRequest request)
    {
        List<VulkanTextureUploadDiagnostic> diagnostics = [];
        if (disposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.UploaderDisposed,
                "Cannot upload through a disposed Vulkan texture uploader.",
                request?.DebugName));
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Rejected, null, diagnostics);
        }

        Validate(request, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Rejected, null, diagnostics);
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
                    (ulong)request.RgbaBytes.Length,
                    VulkanBufferUsage.TransferSource,
                    VulkanMemoryUsage.CpuToGpu,
                    string.IsNullOrWhiteSpace(request.DebugName) ? "texture-upload.staging" : $"{request.DebugName}.staging",
                    MapOnCreate: true));

            if (!stagingResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.StagingBufferCreationFailed,
                    FormatStagingCreationFailure(stagingResult),
                    request.DebugName));
                return new VulkanTextureUploadResult(MapFailedStatus(stagingResult.Status), null, diagnostics);
            }

            staging = stagingResult.Buffer!;
            VulkanBufferWriteResult writeResult = staging.Write(request.RgbaBytes.Span);
            if (!writeResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.StagingBufferWriteFailed,
                    FormatStagingWriteFailure(writeResult),
                    request.DebugName));
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            VulkanFenceOperationResult completedResult = fences.CommandListFence.QueryCompletedValue();
            if (!completedResult.Success || completedResult.Value is null)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.FenceSignalValueUnavailable,
                    FormatFenceFailure("Command-list fence completed value query failed", completedResult),
                    request.DebugName));
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            lease = commandBufferPool.Rent(completedResult.Value.Value);
            VulkanCommandBufferOperationResult beginResult = lease.Begin();
            if (!beginResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.CommandBufferBeginFailed,
                    FormatCommandBufferFailure("Vulkan command buffer begin failed", beginResult),
                    request.DebugName));
                _ = lease.Reset();
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            VulkanTextureUploadResult? transferBarrierResult = TransitionTexture(
                request.Destination,
                lease,
                VulkanResourceLayout.TransferDestination,
                request.DebugName,
                diagnostics);
            if (transferBarrierResult is not null)
            {
                return transferBarrierResult;
            }

            try
            {
                RecordCopy(lease.CommandBuffer, staging.NativeBuffer, request.Destination.NativeImage, request.Destination.Width, request.Destination.Height);
            }
            catch (Exception ex)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.CopyBufferToImageFailed,
                    $"vkCmdCopyBufferToImage recording failed: {ex.GetType().Name}: {ex.Message}",
                    request.DebugName));
                _ = lease.Reset();
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            VulkanTextureUploadResult? shaderBarrierResult = TransitionTexture(
                request.Destination,
                lease,
                VulkanResourceLayout.ShaderResourceFragment,
                request.DebugName,
                diagnostics);
            if (shaderBarrierResult is not null)
            {
                return shaderBarrierResult;
            }

            VulkanCommandBufferOperationResult endResult = lease.End();
            if (!endResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.CommandBufferEndFailed,
                    FormatCommandBufferFailure("Vulkan command buffer end failed", endResult),
                    request.DebugName));
                _ = lease.Reset();
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            signalValue = fences.CommandListFence.AllocateSignalValue();
            Result submitResult = Submit(lease.CommandBuffer, fences.CommandListFence.Semaphore, signalValue.Value);
            if (submitResult != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.QueueSubmitFailed,
                    $"vkQueueSubmit failed with result {submitResult}.",
                    request.DebugName));
                _ = lease.Reset();
                signalValue = null;
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
            }

            submitted = true;
            VulkanFenceOperationResult waitResult = fences.CommandListFence.WaitForValue(signalValue.Value, UploadWaitTimeoutNanoseconds);
            if (!waitResult.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureUploadDiagnosticCodes.QueueSubmitFailed,
                    FormatFenceFailure($"Timed out or failed waiting for command-list fence value {signalValue.Value}", waitResult),
                    request.DebugName));
                _ = plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
                return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, signalValue, diagnostics);
            }

            commandBufferPool.Retire(lease, signalValue.Value);
            lease = null;
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Submitted, signalValue, diagnostics);
        }
        catch (ObjectDisposedException ex)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.UploaderDisposed,
                $"Texture upload encountered disposed Vulkan upload dependency: {ex.Message}",
                request.DebugName));
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Rejected, signalValue, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.QueueSubmitFailed,
                $"Unexpected Vulkan texture upload failure: {ex.Message}",
                request.DebugName));
            if (submitted)
            {
                _ = plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
            }

            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, signalValue, diagnostics);
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

    private void Validate(VulkanTextureUploadRequest request, List<VulkanTextureUploadDiagnostic> diagnostics)
    {
        if (request is null || request.Destination is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.PlantMismatch,
                "Upload destination texture is required.",
                request?.DebugName));
            return;
        }

        if (request.RgbaBytes.IsEmpty)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.EmptyUpload,
                "Texture upload data must contain at least one byte.",
                request.DebugName));
        }

        if (request.Destination.IsDisposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.DestinationTextureDisposed,
                "Upload destination texture is disposed.",
                request.DebugName));
            return;
        }

        if (request.Destination.PlantId != PlantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.PlantMismatch,
                $"Upload destination belongs to plant {request.Destination.PlantId}, not uploader plant {PlantId}.",
                request.DebugName));
        }

        if ((request.Destination.Usage & VulkanTextureUsage.TransferDestination) == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.DestinationMissingTransferDestinationUsage,
                "Upload destination texture must include TransferDestination usage.",
                request.DebugName));
        }

        if (!IsSupportedM0Format(request.Destination.Format))
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.UnsupportedTextureFormat,
                "Texture upload M0 supports only RGBA-like four-byte color formats.",
                request.DebugName));
        }

        if (!TryGetExpectedUploadSize(request.Destination.Width, request.Destination.Height, out ulong expectedSize)
            || (ulong)request.RgbaBytes.Length != expectedSize)
        {
            string expected = expectedSize == 0 ? "a representable whole-texture size" : $"expected whole-texture size {expectedSize} bytes";
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.UploadSizeMismatch,
                $"Texture upload data length {request.RgbaBytes.Length} bytes does not match {expected}.",
                request.DebugName));
        }
    }

    private VulkanTextureUploadResult? TransitionTexture(
        AurelianVulkanTexture texture,
        VulkanCommandBufferLease lease,
        VulkanResourceLayout newLayout,
        string debugName,
        List<VulkanTextureUploadDiagnostic> diagnostics)
    {
        VulkanBarrierPlanResult planResult = texture.LayoutTracker.Transition(TextureBarrierName(debugName), 0, 0, newLayout);
        if (!planResult.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.BarrierEmissionFailed,
                FormatBarrierPlanningFailure($"Texture layout transition planning to {newLayout} failed", planResult),
                debugName));
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
        }

        if (planResult.Status == VulkanBarrierStatus.NoOp || planResult.Plan is null)
        {
            return null;
        }

        VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.EmitTextureBarriers(
            plant,
            lease,
            [new VulkanTextureBarrierEmission(texture, planResult.Plan)]);
        if (!emitResult.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureUploadDiagnosticCodes.BarrierEmissionFailed,
                FormatBarrierEmissionFailure($"Texture layout transition emission to {newLayout} failed", emitResult),
                debugName));
            return new VulkanTextureUploadResult(VulkanTextureUploadStatus.Failed, null, diagnostics);
        }

        return null;
    }

    private void RecordCopy(CommandBuffer commandBuffer, Silk.NET.Vulkan.Buffer source, Image destination, uint width, uint height)
    {
        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1),
        };

        plant.Vk.CmdCopyBufferToImage(commandBuffer, source, destination, ImageLayout.TransferDstOptimal, 1, &region);
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

    private VulkanTextureUploadDiagnostic Diagnostic(string code, string message, string? debugName)
        => new(code, message, PlantId, string.IsNullOrWhiteSpace(debugName) ? null : debugName);

    private static bool IsSupportedM0Format(VulkanTextureFormat format)
        => format is VulkanTextureFormat.Rgba8Unorm or VulkanTextureFormat.Bgra8Unorm or VulkanTextureFormat.Rgba8Srgb or VulkanTextureFormat.Bgra8Srgb;

    private static bool TryGetExpectedUploadSize(uint width, uint height, out ulong sizeBytes)
    {
        ulong pixels = (ulong)width * height;
        if (pixels > ulong.MaxValue / M0BytesPerPixel)
        {
            sizeBytes = 0;
            return false;
        }

        sizeBytes = pixels * M0BytesPerPixel;
        return true;
    }

    private static VulkanTextureUploadStatus MapFailedStatus(VulkanBufferStatus status)
        => status == VulkanBufferStatus.Rejected ? VulkanTextureUploadStatus.Rejected : VulkanTextureUploadStatus.Failed;

    private static string TextureBarrierName(string debugName)
        => string.IsNullOrWhiteSpace(debugName) ? "texture-upload.destination" : debugName;

    private static string FormatStagingCreationFailure(VulkanBufferCreateResult result)
        => FormatDiagnostics("Staging buffer creation failed through VulkanBufferFactory", result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatStagingWriteFailure(VulkanBufferWriteResult result)
        => FormatDiagnostics("Staging buffer write failed", result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatCommandBufferFailure(string prefix, VulkanCommandBufferOperationResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatFenceFailure(string prefix, VulkanFenceOperationResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatBarrierPlanningFailure(string prefix, VulkanBarrierPlanResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatBarrierEmissionFailure(string prefix, VulkanBarrierEmissionResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"));

    private static string FormatDiagnostics(string prefix, IEnumerable<string> diagnostics)
    {
        string details = string.Join("; ", diagnostics);
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix}: {details}";
    }
}
