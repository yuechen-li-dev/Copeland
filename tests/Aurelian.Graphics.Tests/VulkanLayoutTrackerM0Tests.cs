using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanLayoutTrackerM0Tests
{
    [Fact]
    public void VulkanLayoutTracker_InitialLayout_AppliesToAllSubresources()
    {
        VulkanLayoutTracker tracker = new(2, 3, VulkanResourceLayout.Undefined);

        for (uint mip = 0; mip < 2; mip++)
        {
            for (uint layer = 0; layer < 3; layer++)
            {
                Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(mip, layer));
            }
        }
    }

    [Fact]
    public void VulkanLayoutTracker_Transition_SameLayout_ReturnsNoOp()
    {
        VulkanLayoutTracker tracker = new(1, 1, VulkanResourceLayout.TransferSource);

        VulkanBarrierPlanResult result = tracker.Transition("tex.same", 0, 0, VulkanResourceLayout.TransferSource);

        Assert.True(result.Success);
        Assert.Equal(VulkanBarrierStatus.NoOp, result.Status);
        Assert.Null(result.Plan);
        Assert.Equal(VulkanResourceLayout.TransferSource, tracker.Get(0, 0));
    }

    [Fact]
    public void VulkanLayoutTracker_Transition_UpdatesOnlyTargetSubresource()
    {
        VulkanLayoutTracker tracker = new(2, 2, VulkanResourceLayout.Undefined);

        VulkanBarrierPlanResult result = tracker.Transition("tex.target", 1, 0, VulkanResourceLayout.ShaderResourceFragment);

        Assert.True(result.Success);
        Assert.Equal(VulkanBarrierStatus.Planned, result.Status);
        Assert.NotNull(result.Plan);
        Assert.Equal(1U, result.Plan!.BaseMipLevel);
        Assert.Equal(0U, result.Plan.BaseArrayLayer);
        Assert.Equal(VulkanResourceLayout.ShaderResourceFragment, tracker.Get(1, 0));
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(0, 0));
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(0, 1));
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(1, 1));
    }

    [Fact]
    public void VulkanLayoutTracker_Transition_InvalidSubresource_ReturnsDiagnostic()
    {
        VulkanLayoutTracker tracker = new(1, 1, VulkanResourceLayout.Undefined);

        VulkanBarrierPlanResult result = tracker.Transition("tex.invalid", 1, 0, VulkanResourceLayout.TransferDestination);

        Assert.False(result.Success);
        Assert.Equal(VulkanBarrierStatus.Rejected, result.Status);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == VulkanBarrierDiagnosticCodes.InvalidSubresource
            && diagnostic.Severity == VulkanBarrierDiagnosticSeverity.Error
            && diagnostic.ResourceName == "tex.invalid");
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(0, 0));
    }

    [Fact]
    public void VulkanLayoutTracker_TransitionAll_ProducesOnlyNeededPlans()
    {
        VulkanLayoutTracker tracker = new(2, 2, VulkanResourceLayout.Undefined);
        VulkanBarrierPlanResult first = tracker.Transition("tex.batch", 0, 1, VulkanResourceLayout.TransferDestination);
        Assert.True(first.Success);

        VulkanBarrierBatch batch = tracker.TransitionAll("tex.batch", VulkanResourceLayout.TransferDestination);

        Assert.False(batch.IsEmpty);
        Assert.Equal(3, batch.Plans.Count);
        Assert.DoesNotContain(batch.Plans, plan => plan.BaseMipLevel == 0 && plan.BaseArrayLayer == 1);
        Assert.All(batch.Plans, plan => Assert.Equal(VulkanResourceLayout.TransferDestination, plan.NewLayout));
    }

    [Fact]
    public void VulkanLayoutTracker_FlatIndexing_SeparatesMipsAndArrayLayers()
    {
        VulkanLayoutTracker tracker = new(3, 2, VulkanResourceLayout.Undefined);

        Assert.True(tracker.Transition("tex.flat", 0, 1, VulkanResourceLayout.TransferSource).Success);
        Assert.True(tracker.Transition("tex.flat", 1, 0, VulkanResourceLayout.ShaderResourceVertex).Success);
        Assert.True(tracker.Transition("tex.flat", 2, 1, VulkanResourceLayout.ColorAttachment).Success);

        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(0, 0));
        Assert.Equal(VulkanResourceLayout.TransferSource, tracker.Get(0, 1));
        Assert.Equal(VulkanResourceLayout.ShaderResourceVertex, tracker.Get(1, 0));
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(1, 1));
        Assert.Equal(VulkanResourceLayout.Undefined, tracker.Get(2, 0));
        Assert.Equal(VulkanResourceLayout.ColorAttachment, tracker.Get(2, 1));
    }
}
