using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Silk.NET.Vulkan;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBarrierMappingM0Tests
{
    [Fact]
    public void VulkanBarrierMappings_TransferSource_MapsToTransferSrcOptimal()
    {
        VulkanBarrierPlanResult result = VulkanBarrierMappings.Map(VulkanResourceLayout.TransferSource);

        Assert.True(result.Success);
        Assert.Equal(VulkanBarrierStatus.Planned, result.Status);
        Assert.NotNull(result.Mapping);
        Assert.Equal(ImageLayout.TransferSrcOptimal, result.Mapping!.ImageLayout);
        Assert.Equal(AccessFlags.TransferReadBit, result.Mapping.AccessMask);
        Assert.Equal(PipelineStageFlags.TransferBit, result.Mapping.StageMask);
        Assert.Equal(VulkanResourceAccess.TransferRead, result.Mapping.AurelianAccess);
        Assert.Equal(VulkanBarrierStage.Transfer, result.Mapping.AurelianStages);
    }

    [Fact]
    public void VulkanBarrierMappings_TransferDestination_MapsToTransferDstOptimal()
    {
        VulkanBarrierPlanResult result = VulkanBarrierMappings.Map(VulkanResourceLayout.TransferDestination);

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.Equal(ImageLayout.TransferDstOptimal, result.Mapping!.ImageLayout);
        Assert.Equal(AccessFlags.TransferWriteBit, result.Mapping.AccessMask);
        Assert.Equal(PipelineStageFlags.TransferBit, result.Mapping.StageMask);
    }

    [Fact]
    public void VulkanBarrierMappings_ShaderResourceVertex_IncludesVertexShaderStage()
    {
        VulkanBarrierPlanResult result = VulkanBarrierMappings.Map(VulkanResourceLayout.ShaderResourceVertex);

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, result.Mapping!.ImageLayout);
        Assert.True(result.Mapping.StageMask.HasFlag(PipelineStageFlags.VertexShaderBit));
        Assert.True(result.Mapping.AurelianStages.HasFlag(VulkanBarrierStage.VertexShader));
    }

    [Fact]
    public void VulkanBarrierMappings_DepthStencilAttachment_DoesNotIncludeColorAttachmentStage()
    {
        VulkanBarrierPlanResult result = VulkanBarrierMappings.Map(VulkanResourceLayout.DepthStencilAttachment);

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.True(result.Mapping!.StageMask.HasFlag(PipelineStageFlags.EarlyFragmentTestsBit));
        Assert.True(result.Mapping.StageMask.HasFlag(PipelineStageFlags.LateFragmentTestsBit));
        Assert.False(result.Mapping.StageMask.HasFlag(PipelineStageFlags.ColorAttachmentOutputBit));
        Assert.False(result.Mapping.AurelianStages.HasFlag(VulkanBarrierStage.ColorAttachmentOutput));
    }

    [Fact]
    public void VulkanBarrierMappings_CrossPlantTransferSource_MapsAsTransferSourceWithDeferredDiagnosticOrNote()
    {
        VulkanBarrierPlanResult result = VulkanBarrierMappings.Map(VulkanResourceLayout.CrossPlantTransferSource);

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.Equal(ImageLayout.TransferSrcOptimal, result.Mapping!.ImageLayout);
        Assert.Equal(VulkanResourceAccess.TransferRead, result.Mapping.AurelianAccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == VulkanBarrierDiagnosticCodes.CrossPlantOwnershipTransferDeferred
            && diagnostic.Severity == VulkanBarrierDiagnosticSeverity.Info);
    }
}
