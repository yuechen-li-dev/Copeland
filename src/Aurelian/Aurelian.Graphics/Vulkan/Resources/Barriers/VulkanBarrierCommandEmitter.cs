using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;
using NativeBuffer = Silk.NET.Vulkan.Buffer;

namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public static unsafe class VulkanBarrierCommandEmitter
{
    private const uint QueueFamilyIgnored = uint.MaxValue;

    public static VulkanBarrierEmissionResult Emit(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        VulkanBarrierBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.IsEmpty)
        {
            return NoOp(VulkanBarrierEmissionDiagnosticCodes.EmptyBatch, "Barrier batch is empty; no pipeline barrier was recorded.");
        }

        return Rejected(
            VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
            "Pure VulkanBarrierBatch plans do not carry native resource handles. Use texture or buffer barrier emission requests.");
    }

    public static VulkanBarrierEmissionResult EmitTextureBarriers(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        IReadOnlyList<VulkanTextureBarrierEmission> barriers)
        => Emit(plant, commandBuffer, [], barriers);

    public static VulkanBarrierEmissionResult EmitBufferBarriers(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        IReadOnlyList<VulkanBufferBarrierEmission> barriers)
        => Emit(plant, commandBuffer, barriers, []);

    public static VulkanBarrierEmissionResult Emit(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        IReadOnlyList<VulkanBufferBarrierEmission> bufferBarriers,
        IReadOnlyList<VulkanTextureBarrierEmission> textureBarriers)
        => Emit(plant, commandBuffer, bufferBarriers, textureBarriers, []);

    public static VulkanBarrierEmissionResult Emit(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        IReadOnlyList<VulkanBufferBarrierEmission> bufferBarriers,
        IReadOnlyList<VulkanTextureBarrierEmission> textureBarriers,
        IReadOnlyList<VulkanPresentationTargetBarrierEmission> presentationTargetBarriers)
    {
        ArgumentNullException.ThrowIfNull(bufferBarriers);
        ArgumentNullException.ThrowIfNull(textureBarriers);
        ArgumentNullException.ThrowIfNull(presentationTargetBarriers);

        if (bufferBarriers.Count == 0 && textureBarriers.Count == 0 && presentationTargetBarriers.Count == 0)
        {
            return NoOp(VulkanBarrierEmissionDiagnosticCodes.EmptyBatch, "Barrier emission request is empty; no pipeline barrier was recorded.");
        }

        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(commandBuffer);

        List<VulkanBarrierDiagnostic> diagnostics = [];
        ValidatePlantAndCommandBuffer(plant, commandBuffer, diagnostics);
        ValidateBufferBarriers(plant, bufferBarriers, diagnostics);
        ValidateTextureBarriers(plant, textureBarriers, diagnostics);
        ValidatePresentationTargetBarriers(plant, presentationTargetBarriers, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == VulkanBarrierDiagnosticSeverity.Error))
        {
            return new VulkanBarrierEmissionResult(VulkanBarrierEmissionStatus.Rejected, 0, 0, diagnostics);
        }

        try
        {
            BufferMemoryBarrier[] nativeBufferBarriers = bufferBarriers
                .Select(static barrier => CreateBufferBarrier(barrier.Buffer.NativeBuffer, barrier.Plan))
                .ToArray();
            ImageMemoryBarrier[] nativeImageBarriers = textureBarriers
                .Select(static barrier => CreateImageBarrier(barrier.Texture.NativeImage, barrier.Plan))
                .Concat(presentationTargetBarriers.Select(static barrier => CreateImageBarrier(barrier.Target.NativeImage, barrier.Plan)))
                .ToArray();

            PipelineStageFlags sourceStages = PipelineStageFlags.None;
            PipelineStageFlags destinationStages = PipelineStageFlags.None;
            foreach (VulkanBufferBarrierEmission barrier in bufferBarriers)
            {
                sourceStages |= MapStages(barrier.Plan.OldStages, source: true);
                destinationStages |= MapStages(barrier.Plan.NewStages, source: false);
            }

            foreach (VulkanTextureBarrierEmission barrier in textureBarriers)
            {
                sourceStages |= EnsureStage(barrier.Plan.OldMapping.StageMask, source: true);
                destinationStages |= EnsureStage(barrier.Plan.NewMapping.StageMask, source: false);
            }

            foreach (VulkanPresentationTargetBarrierEmission barrier in presentationTargetBarriers)
            {
                sourceStages |= EnsureStage(barrier.Plan.OldMapping.StageMask, source: true);
                destinationStages |= EnsureStage(barrier.Plan.NewMapping.StageMask, source: false);
            }

            sourceStages = EnsureStage(sourceStages, source: true);
            destinationStages = EnsureStage(destinationStages, source: false);

            fixed (BufferMemoryBarrier* bufferBarrierPtr = nativeBufferBarriers)
            fixed (ImageMemoryBarrier* imageBarrierPtr = nativeImageBarriers)
            {
                plant.Vk.CmdPipelineBarrier(
                    commandBuffer.CommandBuffer,
                    sourceStages,
                    destinationStages,
                    DependencyFlags.None,
                    0,
                    null,
                    (uint)nativeBufferBarriers.Length,
                    nativeBufferBarriers.Length == 0 ? null : bufferBarrierPtr,
                    (uint)nativeImageBarriers.Length,
                    nativeImageBarriers.Length == 0 ? null : imageBarrierPtr);
            }

            return new VulkanBarrierEmissionResult(
                VulkanBarrierEmissionStatus.Emitted,
                nativeImageBarriers.Length,
                nativeBufferBarriers.Length,
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new VulkanBarrierDiagnostic(
                VulkanBarrierEmissionDiagnosticCodes.BarrierEmissionFailed,
                VulkanBarrierDiagnosticSeverity.Error,
                $"Vulkan pipeline barrier emission failed: {exception.Message}"));
            return new VulkanBarrierEmissionResult(VulkanBarrierEmissionStatus.Failed, 0, 0, diagnostics);
        }
    }

    private static void ValidatePlantAndCommandBuffer(
        AurelianVulkanPlant plant,
        VulkanCommandBufferLease commandBuffer,
        List<VulkanBarrierDiagnostic> diagnostics)
    {
        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(new VulkanBarrierDiagnostic(
                VulkanBarrierEmissionDiagnosticCodes.InvalidCommandBufferState,
                VulkanBarrierDiagnosticSeverity.Error,
                "Cannot emit Vulkan barriers for a disposed or uninitialized plant."));
        }

        if (commandBuffer.PlantId != plant.Context.Id)
        {
            diagnostics.Add(new VulkanBarrierDiagnostic(
                VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                VulkanBarrierDiagnosticSeverity.Error,
                $"Command buffer plant {commandBuffer.PlantId} does not match emitter plant {plant.Context.Id}."));
        }

        if (!commandBuffer.IsRecording)
        {
            diagnostics.Add(new VulkanBarrierDiagnostic(
                VulkanBarrierEmissionDiagnosticCodes.InvalidCommandBufferState,
                VulkanBarrierDiagnosticSeverity.Error,
                "Vulkan barrier emission requires a command buffer in Recording state."));
        }
    }

    private static void ValidateBufferBarriers(
        AurelianVulkanPlant plant,
        IReadOnlyList<VulkanBufferBarrierEmission> barriers,
        List<VulkanBarrierDiagnostic> diagnostics)
    {
        foreach (VulkanBufferBarrierEmission barrier in barriers)
        {
            if (barrier.Buffer.PlantId != plant.Context.Id)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Buffer '{barrier.Plan.ResourceName}' belongs to plant {barrier.Buffer.PlantId}, not emitter plant {plant.Context.Id}.",
                    barrier.Plan.ResourceName));
            }

            if (barrier.Buffer.IsDisposed || barrier.Buffer.NativeBuffer.Handle == 0)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Buffer '{barrier.Plan.ResourceName}' is disposed or has no native Vulkan buffer handle.",
                    barrier.Plan.ResourceName));
            }

            if (barrier.Plan.SizeBytes == 0 || barrier.Plan.Offset > barrier.Buffer.SizeBytes || barrier.Plan.SizeBytes > barrier.Buffer.SizeBytes - barrier.Plan.Offset)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Buffer barrier '{barrier.Plan.ResourceName}' range is empty or outside the buffer bounds.",
                    barrier.Plan.ResourceName));
            }
        }
    }

    private static void ValidateTextureBarriers(
        AurelianVulkanPlant plant,
        IReadOnlyList<VulkanTextureBarrierEmission> barriers,
        List<VulkanBarrierDiagnostic> diagnostics)
    {
        foreach (VulkanTextureBarrierEmission barrier in barriers)
        {
            if (barrier.Texture.PlantId != plant.Context.Id)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Texture '{barrier.Plan.ResourceName}' belongs to plant {barrier.Texture.PlantId}, not emitter plant {plant.Context.Id}.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Texture.IsDisposed || barrier.Texture.NativeImage.Handle == 0)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Texture '{barrier.Plan.ResourceName}' is disposed or has no native Vulkan image handle.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Plan.LevelCount == 0 || barrier.Plan.LayerCount == 0
                || barrier.Plan.BaseMipLevel > barrier.Texture.MipLevels
                || barrier.Plan.LevelCount > barrier.Texture.MipLevels - barrier.Plan.BaseMipLevel
                || barrier.Plan.BaseArrayLayer > barrier.Texture.ArrayLayers
                || barrier.Plan.LayerCount > barrier.Texture.ArrayLayers - barrier.Plan.BaseArrayLayer)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Texture barrier '{barrier.Plan.ResourceName}' subresource range is empty or outside the texture bounds.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Plan.OldLayout is VulkanResourceLayout.CrossPlantTransferSource or VulkanResourceLayout.CrossPlantTransferDestination
                || barrier.Plan.NewLayout is VulkanResourceLayout.CrossPlantTransferSource or VulkanResourceLayout.CrossPlantTransferDestination)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    "Cross-plant queue-family ownership transfers are deferred and cannot be emitted by A34 M1.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }
        }
    }


    private static void ValidatePresentationTargetBarriers(
        AurelianVulkanPlant plant,
        IReadOnlyList<VulkanPresentationTargetBarrierEmission> barriers,
        List<VulkanBarrierDiagnostic> diagnostics)
    {
        foreach (VulkanPresentationTargetBarrierEmission barrier in barriers)
        {
            if (barrier.Target.PlantId != plant.Context.Id)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Presentation target '{barrier.Plan.ResourceName}' belongs to plant {barrier.Target.PlantId}, not emitter plant {plant.Context.Id}.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Target.NativeImage.Handle == 0)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Presentation target '{barrier.Plan.ResourceName}' has no native Vulkan image handle.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Plan.LevelCount != 1 || barrier.Plan.LayerCount != 1 || barrier.Plan.BaseMipLevel != 0 || barrier.Plan.BaseArrayLayer != 0)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Presentation target barrier '{barrier.Plan.ResourceName}' must address the single swapchain color subresource.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }

            if (barrier.Plan.OldLayout is VulkanResourceLayout.CrossPlantTransferSource or VulkanResourceLayout.CrossPlantTransferDestination
                || barrier.Plan.NewLayout is VulkanResourceLayout.CrossPlantTransferSource or VulkanResourceLayout.CrossPlantTransferDestination)
            {
                diagnostics.Add(new VulkanBarrierDiagnostic(
                    VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan,
                    VulkanBarrierDiagnosticSeverity.Error,
                    "Cross-plant queue-family ownership transfers are deferred and cannot be emitted for presentation targets by A53 M0.",
                    barrier.Plan.ResourceName,
                    barrier.Plan.BaseMipLevel,
                    barrier.Plan.BaseArrayLayer));
            }
        }
    }

    private static BufferMemoryBarrier CreateBufferBarrier(NativeBuffer buffer, VulkanBufferTransitionPlan plan)
        => new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = MapAccess(plan.OldAccess),
            DstAccessMask = MapAccess(plan.NewAccess),
            SrcQueueFamilyIndex = QueueFamilyIgnored,
            DstQueueFamilyIndex = QueueFamilyIgnored,
            Buffer = buffer,
            Offset = plan.Offset,
            Size = plan.SizeBytes,
        };

    private static ImageMemoryBarrier CreateImageBarrier(Image image, VulkanBarrierPlan plan)
        => new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = plan.OldMapping.AccessMask,
            DstAccessMask = plan.NewMapping.AccessMask,
            OldLayout = plan.OldMapping.ImageLayout,
            NewLayout = plan.NewMapping.ImageLayout,
            SrcQueueFamilyIndex = QueueFamilyIgnored,
            DstQueueFamilyIndex = QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(
                ImageAspectFlags.ColorBit,
                plan.BaseMipLevel,
                plan.LevelCount,
                plan.BaseArrayLayer,
                plan.LayerCount),
        };

    private static VulkanBarrierEmissionResult NoOp(string code, string message)
        => new(
            VulkanBarrierEmissionStatus.NoOp,
            0,
            0,
            [new VulkanBarrierDiagnostic(code, VulkanBarrierDiagnosticSeverity.Info, message)]);

    private static VulkanBarrierEmissionResult Rejected(string code, string message)
        => new(
            VulkanBarrierEmissionStatus.Rejected,
            0,
            0,
            [new VulkanBarrierDiagnostic(code, VulkanBarrierDiagnosticSeverity.Error, message)]);

    private static AccessFlags MapAccess(VulkanResourceAccess access)
    {
        AccessFlags flags = AccessFlags.None;
        if ((access & VulkanResourceAccess.TransferRead) != 0)
        {
            flags |= AccessFlags.TransferReadBit;
        }

        if ((access & VulkanResourceAccess.TransferWrite) != 0)
        {
            flags |= AccessFlags.TransferWriteBit;
        }

        if ((access & VulkanResourceAccess.ShaderRead) != 0)
        {
            flags |= AccessFlags.ShaderReadBit;
        }

        if ((access & VulkanResourceAccess.ShaderWrite) != 0)
        {
            flags |= AccessFlags.ShaderWriteBit;
        }

        if ((access & VulkanResourceAccess.ColorAttachmentRead) != 0)
        {
            flags |= AccessFlags.ColorAttachmentReadBit;
        }

        if ((access & VulkanResourceAccess.ColorAttachmentWrite) != 0)
        {
            flags |= AccessFlags.ColorAttachmentWriteBit;
        }

        if ((access & VulkanResourceAccess.DepthStencilRead) != 0)
        {
            flags |= AccessFlags.DepthStencilAttachmentReadBit;
        }

        if ((access & VulkanResourceAccess.DepthStencilWrite) != 0)
        {
            flags |= AccessFlags.DepthStencilAttachmentWriteBit;
        }

        if ((access & VulkanResourceAccess.HostRead) != 0)
        {
            flags |= AccessFlags.HostReadBit;
        }

        if ((access & VulkanResourceAccess.HostWrite) != 0)
        {
            flags |= AccessFlags.HostWriteBit;
        }

        if ((access & VulkanResourceAccess.PresentRead) != 0)
        {
            flags |= AccessFlags.MemoryReadBit;
        }

        return flags;
    }

    private static PipelineStageFlags MapStages(VulkanBarrierStage stages, bool source)
    {
        PipelineStageFlags flags = PipelineStageFlags.None;
        if ((stages & VulkanBarrierStage.Host) != 0)
        {
            flags |= PipelineStageFlags.HostBit;
        }

        if ((stages & VulkanBarrierStage.Transfer) != 0)
        {
            flags |= PipelineStageFlags.TransferBit;
        }

        if ((stages & VulkanBarrierStage.VertexShader) != 0)
        {
            flags |= PipelineStageFlags.VertexShaderBit;
        }

        if ((stages & VulkanBarrierStage.FragmentShader) != 0)
        {
            flags |= PipelineStageFlags.FragmentShaderBit;
        }

        if ((stages & VulkanBarrierStage.ComputeShader) != 0)
        {
            flags |= PipelineStageFlags.ComputeShaderBit;
        }

        if ((stages & VulkanBarrierStage.ColorAttachmentOutput) != 0)
        {
            flags |= PipelineStageFlags.ColorAttachmentOutputBit;
        }

        if ((stages & VulkanBarrierStage.EarlyFragmentTests) != 0)
        {
            flags |= PipelineStageFlags.EarlyFragmentTestsBit;
        }

        if ((stages & VulkanBarrierStage.LateFragmentTests) != 0)
        {
            flags |= PipelineStageFlags.LateFragmentTestsBit;
        }

        if ((stages & VulkanBarrierStage.BottomOfPipe) != 0)
        {
            flags |= PipelineStageFlags.BottomOfPipeBit;
        }

        if ((stages & VulkanBarrierStage.AllGraphics) != 0)
        {
            flags |= PipelineStageFlags.AllGraphicsBit;
        }

        if ((stages & VulkanBarrierStage.AllCommands) != 0)
        {
            flags |= PipelineStageFlags.AllCommandsBit;
        }

        return EnsureStage(flags, source);
    }

    private static PipelineStageFlags EnsureStage(PipelineStageFlags stages, bool source)
        => stages == PipelineStageFlags.None
            ? source ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.BottomOfPipeBit
            : stages;
}
