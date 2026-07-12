using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBarrierMapping(
    VulkanResourceLayout Layout,
    ImageLayout ImageLayout,
    AccessFlags AccessMask,
    PipelineStageFlags StageMask,
    VulkanResourceAccess AurelianAccess,
    VulkanBarrierStage AurelianStages);

public static class VulkanBarrierMappings
{
    public static VulkanBarrierPlanResult Map(VulkanResourceLayout layout)
    {
        VulkanBarrierMapping? mapping = layout switch
        {
            VulkanResourceLayout.Undefined => new(
                layout,
                ImageLayout.Undefined,
                AccessFlags.None,
                PipelineStageFlags.TopOfPipeBit,
                VulkanResourceAccess.None,
                VulkanBarrierStage.None),
            VulkanResourceLayout.General => new(
                layout,
                ImageLayout.General,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.AllCommandsBit,
                VulkanResourceAccess.ShaderRead | VulkanResourceAccess.ShaderWrite,
                VulkanBarrierStage.AllCommands),
            VulkanResourceLayout.TransferSource => TransferSource(layout),
            VulkanResourceLayout.TransferDestination => TransferDestination(layout),
            VulkanResourceLayout.ShaderResourceVertex => new(
                layout,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.VertexShaderBit,
                VulkanResourceAccess.ShaderRead,
                VulkanBarrierStage.VertexShader),
            VulkanResourceLayout.ShaderResourceFragment => new(
                layout,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.FragmentShaderBit,
                VulkanResourceAccess.ShaderRead,
                VulkanBarrierStage.FragmentShader),
            VulkanResourceLayout.ShaderResourceCompute => new(
                layout,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit,
                VulkanResourceAccess.ShaderRead,
                VulkanBarrierStage.ComputeShader),
            VulkanResourceLayout.ShaderResourceAll => new(
                layout,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.AllGraphicsBit | PipelineStageFlags.ComputeShaderBit,
                VulkanResourceAccess.ShaderRead,
                VulkanBarrierStage.AllGraphics | VulkanBarrierStage.ComputeShader),
            VulkanResourceLayout.StorageReadWrite => new(
                layout,
                ImageLayout.General,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
                VulkanResourceAccess.ShaderRead | VulkanResourceAccess.ShaderWrite,
                VulkanBarrierStage.ComputeShader | VulkanBarrierStage.FragmentShader),
            VulkanResourceLayout.ColorAttachment => new(
                layout,
                ImageLayout.ColorAttachmentOptimal,
                AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                PipelineStageFlags.ColorAttachmentOutputBit,
                VulkanResourceAccess.ColorAttachmentRead | VulkanResourceAccess.ColorAttachmentWrite,
                VulkanBarrierStage.ColorAttachmentOutput),
            VulkanResourceLayout.DepthStencilAttachment => new(
                layout,
                ImageLayout.DepthStencilAttachmentOptimal,
                AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
                PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                VulkanResourceAccess.DepthStencilRead | VulkanResourceAccess.DepthStencilWrite,
                VulkanBarrierStage.EarlyFragmentTests | VulkanBarrierStage.LateFragmentTests),
            VulkanResourceLayout.Present => new(
                layout,
                ImageLayout.PresentSrcKhr,
                AccessFlags.MemoryReadBit,
                PipelineStageFlags.BottomOfPipeBit,
                VulkanResourceAccess.PresentRead,
                VulkanBarrierStage.BottomOfPipe),
            VulkanResourceLayout.CrossPlantTransferSource => TransferSource(layout),
            VulkanResourceLayout.CrossPlantTransferDestination => TransferDestination(layout),
            _ => null,
        };

        if (mapping is null)
        {
            return VulkanBarrierPlanResult.Rejected([
                new VulkanBarrierDiagnostic(
                    VulkanBarrierDiagnosticCodes.UnsupportedLayout,
                    VulkanBarrierDiagnosticSeverity.Error,
                    $"Unsupported Vulkan resource layout value '{layout}'."),
            ]);
        }

        IReadOnlyList<VulkanBarrierDiagnostic> diagnostics = IsCrossPlant(layout)
            ? [new VulkanBarrierDiagnostic(
                VulkanBarrierDiagnosticCodes.CrossPlantOwnershipTransferDeferred,
                VulkanBarrierDiagnosticSeverity.Info,
                "Cross-plant transfer layouts currently map like single-plant transfer layouts; queue-family ownership transfer indices are deferred to command emission M1.")]
            : [];

        return new VulkanBarrierPlanResult(VulkanBarrierStatus.Planned, null, mapping, diagnostics);
    }

    private static VulkanBarrierMapping TransferSource(VulkanResourceLayout layout)
        => new(
            layout,
            ImageLayout.TransferSrcOptimal,
            AccessFlags.TransferReadBit,
            PipelineStageFlags.TransferBit,
            VulkanResourceAccess.TransferRead,
            VulkanBarrierStage.Transfer);

    private static VulkanBarrierMapping TransferDestination(VulkanResourceLayout layout)
        => new(
            layout,
            ImageLayout.TransferDstOptimal,
            AccessFlags.TransferWriteBit,
            PipelineStageFlags.TransferBit,
            VulkanResourceAccess.TransferWrite,
            VulkanBarrierStage.Transfer);

    private static bool IsCrossPlant(VulkanResourceLayout layout)
        => layout is VulkanResourceLayout.CrossPlantTransferSource or VulkanResourceLayout.CrossPlantTransferDestination;
}
