using Aurelian.Runtime.Rendering;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class VisibilityPolicyTests
{
    [Fact]
    public void DominatusExploresAndChoosesMeasuredOpportunityWithCommitAndHysteresis()
    {
        var controller = new VisibilityPolicyController(traceCapacity: 4096);
        var modes = new HashSet<VisibilityPolicyMode>();
        long sequence = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            var mode = controller.Tick(TimeSpan.FromSeconds(1d / 60), true, true);
            modes.Add(mode);
            controller.RecordSubmission(++sequence, mode);
            controller.Complete(sequence, Cost(mode));
        }
        Assert.Equal(4, modes.Count);
        Assert.Equal(VisibilityPolicyMode.Lod, controller.Current);
        Assert.Contains(controller.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "MinCommitActive");
        // A small counterfactual gain must not flip the selected mode.
        for (int frame = 0; frame < 40; frame++)
        {
            controller.Tick(TimeSpan.FromSeconds(1d / 60), true, true);
            controller.RecordSubmission(++sequence, VisibilityPolicyMode.Both);
            controller.Complete(sequence, 1.98);
        }
        Assert.Equal(VisibilityPolicyMode.Lod, controller.Current);
        Assert.Contains(controller.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "HysteresisBlock");
        Assert.Equal(VisibilityPolicyMode.Baseline, controller.Tick(TimeSpan.Zero, false, false));
    }

    [Fact]
    public void QueuedObservationsAreAttributedOnceAndInvalidationRejectsStaleWork()
    {
        var controller = new VisibilityPolicyController();
        controller.Tick(TimeSpan.Zero, true, true);
        for (int frame = 1; frame <= 12; frame++) controller.RecordSubmission(frame, VisibilityPolicyMode.Baseline);
        Assert.False(controller.Complete(1, 5)); // Admission/warmup is not a steady sample.
        Assert.True(controller.Complete(9, 5));
        Assert.False(controller.Complete(9, 5));
        controller.InvalidateMeasurements();
        Assert.False(controller.Complete(10, 5));
        Assert.All(controller.Costs, cost => Assert.Equal(0, cost.Samples));
    }

    private static double Cost(VisibilityPolicyMode mode) => mode switch
    {
        VisibilityPolicyMode.Baseline => 5,
        VisibilityPolicyMode.Lod => 2,
        VisibilityPolicyMode.Hzb => 6,
        VisibilityPolicyMode.Both => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
