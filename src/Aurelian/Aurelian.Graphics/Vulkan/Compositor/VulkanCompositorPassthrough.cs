using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Rendering.Contracts.Compositor;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed unsafe class VulkanCompositorPassthrough : IVulkanCompositorPassthroughMechanism, IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanCommandBufferPool commandBufferPool;
    private readonly VulkanCommandSubmitter submitter;
    private bool disposed;

    public VulkanCompositorPassthrough(
        AurelianVulkanPlant plant,
        VulkanCommandBufferPool commandBufferPool,
        VulkanCommandSubmitter submitter)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBufferPool);
        ArgumentNullException.ThrowIfNull(submitter);

        if (commandBufferPool.PlantId != plant.Context.Id || submitter.PlantId != plant.Context.Id)
        {
            throw new ArgumentException("Vulkan compositor plant, command buffer pool, and submitter must belong to the same plant.");
        }

        this.plant = plant;
        this.commandBufferPool = commandBufferPool;
        this.submitter = submitter;
        PlantId = plant.Context.Id;
    }

    public PlantId PlantId { get; }

    public VulkanCompositorResult Dispatch(
        CompositorDispatchRequest request,
        VulkanPlantOutputImageSet plantOutputs,
        VulkanPresentationTargetImageSet presentationTargets)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<VulkanCompositorDiagnostic> diagnostics = [];

        if (disposed)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.CompositorDisposed, "Cannot dispatch through a disposed Vulkan compositor passthrough mechanism."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        if (request.Policy != CompositorPolicyKind.Passthrough)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.UnsupportedPolicy, $"Vulkan passthrough compositor supports only Passthrough policy, not {request.Policy}."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        if (request.Inputs.Count == 0)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.MissingInput, "Passthrough compositor dispatch requires exactly one plant output input."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        if (request.Inputs.Count != 1)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.MultipleInputsUnsupported, $"Passthrough compositor dispatch supports one input, but received {request.Inputs.Count}."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        PlantOutputRef inputRef = request.Inputs[0];
        VulkanPlantOutputResolutionResult sourceResult = VulkanPlantOutputResolver.Resolve(plantOutputs, inputRef);
        if (!sourceResult.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanCompositorDiagnosticCodes.PlantOutputResolutionFailed,
                FormatDiagnostics("Plant output resolution failed", sourceResult.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        VulkanPresentationTargetResolutionResult targetResult = VulkanPresentationTargetResolver.Resolve(presentationTargets, request.Target);
        if (!targetResult.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanCompositorDiagnosticCodes.PresentationTargetResolutionFailed,
                FormatDiagnostics("Presentation target resolution failed", targetResult.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        VulkanPlantOutputImage source = sourceResult.Output!;
        VulkanPresentationTargetImage target = targetResult.Target!;

        if (source.PlantId != PlantId || target.PlantId != PlantId)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.PlantOutputResolutionFailed, "A53 passthrough copy requires source and target images to belong to the compositor plant."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        if (source.Width != target.Width || source.Height != target.Height)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.SizeMismatch, $"Passthrough copy M0 requires matching dimensions; source is {source.Width}x{source.Height}, target is {target.Width}x{target.Height}."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        if (!string.Equals(source.Format, target.Format, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.FormatMismatch, $"Passthrough copy M0 requires matching formats; source is {source.Format}, target is {target.Format}."));
            return Result(VulkanCompositorStatus.Rejected, CompositorDispatchStatus.Rejected, request, null, diagnostics);
        }

        VulkanCommandBufferLease lease;
        try
        {
            lease = commandBufferPool.Rent(submitter.LastKnownCompletedFenceValue);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.CommandBufferBeginFailed, $"Command buffer rent failed: {ex.Message}"));
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        VulkanCommandBufferOperationResult begin = lease.Begin();
        if (!begin.Success)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.CommandBufferBeginFailed, FormatCommandBufferFailure("Command buffer begin failed", begin)));
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        VulkanResourceLayout originalSourceLayout = source.Texture.LayoutTracker.Get(0, 0);
        if (!EmitBarrier(lease, source.Texture.LayoutTracker.Transition($"plant-output:{source.Ref}", 0, 0, VulkanResourceLayout.TransferSource), source, diagnostics)
            || !EmitBarrier(lease, target.LayoutTracker.Transition($"presentation-target:{target.ImageIndex}", 0, 0, VulkanResourceLayout.TransferDestination), target, diagnostics))
        {
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        try
        {
            ImageCopy copyRegion = new()
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D(source.Width, source.Height, 1),
            };

            plant.Vk.CmdCopyImage(
                lease.CommandBuffer,
                source.Texture.NativeImage,
                ImageLayout.TransferSrcOptimal,
                target.NativeImage,
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.CopyImageFailed, $"vkCmdCopyImage recording failed: {exception.Message}"));
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        if (!EmitBarrier(lease, target.LayoutTracker.Transition($"presentation-target:{target.ImageIndex}", 0, 0, VulkanResourceLayout.Present), target, diagnostics))
        {
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        if (originalSourceLayout != VulkanResourceLayout.TransferSource
            && !EmitBarrier(lease, source.Texture.LayoutTracker.Transition($"plant-output:{source.Ref}", 0, 0, originalSourceLayout), source, diagnostics))
        {
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        VulkanCommandBufferOperationResult end = lease.End();
        if (!end.Success)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.CommandBufferEndFailed, FormatCommandBufferFailure("Command buffer end failed", end)));
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, null, diagnostics);
        }

        VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(lease, WaitForCompletion: true, DebugName: "a53.compositor.passthrough"));
        if (!submit.Success)
        {
            diagnostics.Add(Diagnostic(
                VulkanCompositorDiagnosticCodes.SubmitFailed,
                FormatDiagnostics("Compositor command submit failed", submit.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return Result(VulkanCompositorStatus.Failed, CompositorDispatchStatus.Failed, request, submit.SignalFenceValue, diagnostics);
        }

        return Result(VulkanCompositorStatus.Dispatched, CompositorDispatchStatus.Dispatched, request, submit.SignalFenceValue, diagnostics);
    }

    public void Dispose()
    {
        disposed = true;
    }

    private bool EmitBarrier(
        VulkanCommandBufferLease lease,
        VulkanBarrierPlanResult planResult,
        VulkanPlantOutputImage source,
        List<VulkanCompositorDiagnostic> diagnostics)
    {
        if (planResult.Status == VulkanBarrierStatus.NoOp)
        {
            return true;
        }

        if (!planResult.Success || planResult.Plan is null)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.BarrierEmissionFailed, FormatDiagnostics("Source barrier planning failed", planResult.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return false;
        }

        VulkanBarrierEmissionResult emission = VulkanBarrierCommandEmitter.EmitTextureBarriers(
            plant,
            lease,
            [new VulkanTextureBarrierEmission(source.Texture, planResult.Plan)]);
        if (!emission.Success)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.BarrierEmissionFailed, FormatDiagnostics("Source barrier emission failed", emission.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return false;
        }

        return true;
    }

    private bool EmitBarrier(
        VulkanCommandBufferLease lease,
        VulkanBarrierPlanResult planResult,
        VulkanPresentationTargetImage target,
        List<VulkanCompositorDiagnostic> diagnostics)
    {
        if (planResult.Status == VulkanBarrierStatus.NoOp)
        {
            return true;
        }

        if (!planResult.Success || planResult.Plan is null)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.BarrierEmissionFailed, FormatDiagnostics("Presentation target barrier planning failed", planResult.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return false;
        }

        VulkanBarrierEmissionResult emission = VulkanBarrierCommandEmitter.Emit(
            plant,
            lease,
            [],
            [],
            [new VulkanPresentationTargetBarrierEmission(target, planResult.Plan)]);
        if (!emission.Success)
        {
            diagnostics.Add(Diagnostic(VulkanCompositorDiagnosticCodes.BarrierEmissionFailed, FormatDiagnostics("Presentation target barrier emission failed", emission.Diagnostics.Select(static x => (x.Code, x.Message)))));
            return false;
        }

        return true;
    }

    private VulkanCompositorDiagnostic Diagnostic(string code, string message)
        => new(code, VulkanCompositorDiagnosticSeverity.Error, message, PlantId);

    private static VulkanCompositorResult Result(
        VulkanCompositorStatus status,
        CompositorDispatchStatus dispatchStatus,
        CompositorDispatchRequest request,
        ulong? signalFenceValue,
        IReadOnlyList<VulkanCompositorDiagnostic> diagnostics)
        => new(
            status,
            new CompositorDispatchResult(
                dispatchStatus,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                diagnostics.Select(static diagnostic => new CompositorDispatchDiagnostic(
                    diagnostic.Code,
                    diagnostic.Severity == VulkanCompositorDiagnosticSeverity.Error
                        ? CompositorDispatchDiagnosticSeverity.Error
                        : diagnostic.Severity == VulkanCompositorDiagnosticSeverity.Warning
                            ? CompositorDispatchDiagnosticSeverity.Warning
                            : CompositorDispatchDiagnosticSeverity.Info,
                    diagnostic.Message)).ToArray()),
            signalFenceValue,
            diagnostics);

    private static string FormatCommandBufferFailure(string prefix, VulkanCommandBufferOperationResult result)
        => FormatDiagnostics(prefix, result.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Message)));

    private static string FormatDiagnostics(string prefix, IEnumerable<(string Code, string Message)> diagnostics)
    {
        string details = string.Join("; ", diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix}: {details}";
    }
}
