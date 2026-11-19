using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Rendering;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class ResolutionPolicyTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1d / 60);

    [Fact]
    public void BudgetPressureChoosesLowerScaleAndHeadroomRestoresNative()
    {
        var policy = new ResolutionPolicyController(16, traceCapacity: 4096);
        for (int frame = 0; frame < 90; frame++)
        {
            var scale = policy.Tick(Frame);
            policy.Observe(scale == RenderScale3D.Native ? 24 : 12);
        }
        Assert.Equal(RenderScale3D.TwoThirds, policy.Current);
        for (int frame = 0; frame < 90; frame++)
        {
            var scale = policy.Tick(Frame);
            policy.Observe(scale == RenderScale3D.Native ? 8 : 5);
        }
        Assert.Equal(RenderScale3D.Native, policy.Current);
    }

    [Fact]
    public void ScalesRemainCommittedDuringAReversalAndNearTheBudget()
    {
        var policy = new ResolutionPolicyController(10, traceCapacity: 4096);
        for (int frame = 0; frame < 60 && policy.Current != RenderScale3D.Half; frame++)
        {
            policy.Tick(Frame);
            policy.Observe(32);
        }
        Assert.Equal(RenderScale3D.Half, policy.Current);
        for (int frame = 0; frame < 14; frame++)
        {
            policy.Observe(2);
            Assert.Equal(RenderScale3D.Half, policy.Tick(Frame));
        }
        Assert.Contains(policy.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "MinCommitActive");

        var near = new ResolutionPolicyController(10, traceCapacity: 4096);
        for (int frame = 0; frame < 90; frame++)
        {
            near.Observe(10.18);
            Assert.Equal(RenderScale3D.Native, near.Tick(Frame));
        }
        Assert.Contains(near.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "HysteresisBlock");
    }

    [Fact]
    public void IsolatedSlowFrameDoesNotSpendResolutionBudget()
    {
        var policy = new ResolutionPolicyController(10);
        for (int frame = 0; frame < 90; frame++)
        {
            Assert.Equal(RenderScale3D.Native, policy.Tick(Frame));
            policy.Observe(frame == 40 ? 85 : 3);
        }
        Assert.Equal(3, policy.SmoothedGpuMilliseconds, precision: 6);
    }

    [Fact]
    public void AnUnreachableBudgetStillChoosesTheLowestCostScale()
    {
        var policy = new ResolutionPolicyController(4);
        for (int frame = 0; frame < 120; frame++)
        {
            var scale = policy.Tick(Frame);
            double milliseconds = scale switch
            {
                RenderScale3D.Native => 38,
                RenderScale3D.TwoThirds => 24,
                _ => 13,
            };
            policy.Observe(milliseconds);
        }
        Assert.Equal(RenderScale3D.Half, policy.Current);
        Assert.True(policy.SmoothedGpuMilliseconds > policy.BudgetMilliseconds);
    }
}
