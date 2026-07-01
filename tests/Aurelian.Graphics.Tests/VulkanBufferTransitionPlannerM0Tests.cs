using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBufferTransitionPlannerM0Tests
{
    [Fact]
    public void VulkanBufferTransitionPlanner_HostWriteToTransferRead_HasExpectedAccessAndStages()
    {
        VulkanBufferTransitionPlan plan = VulkanBufferTransitionPlanner.HostWriteToTransferRead("staging", 4096);

        Assert.Equal("staging", plan.ResourceName);
        Assert.Equal(VulkanResourceAccess.HostWrite, plan.OldAccess);
        Assert.Equal(VulkanResourceAccess.TransferRead, plan.NewAccess);
        Assert.Equal(VulkanBarrierStage.Host, plan.OldStages);
        Assert.Equal(VulkanBarrierStage.Transfer, plan.NewStages);
        Assert.Equal(0UL, plan.Offset);
        Assert.Equal(4096UL, plan.SizeBytes);
    }

    [Fact]
    public void VulkanBufferTransitionPlanner_TransferWriteToVertexRead_HasExpectedAccessAndStages()
    {
        VulkanBufferTransitionPlan plan = VulkanBufferTransitionPlanner.TransferWriteToVertexRead("vertex", 8192);

        Assert.Equal("vertex", plan.ResourceName);
        Assert.Equal(VulkanResourceAccess.TransferWrite, plan.OldAccess);
        Assert.Equal(VulkanResourceAccess.ShaderRead, plan.NewAccess);
        Assert.Equal(VulkanBarrierStage.Transfer, plan.OldStages);
        Assert.Equal(VulkanBarrierStage.VertexShader, plan.NewStages);
        Assert.Equal(0UL, plan.Offset);
        Assert.Equal(8192UL, plan.SizeBytes);
    }
}
