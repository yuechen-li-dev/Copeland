using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Runtime.Rendering;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class DiffuseRegionalPolicyTests
{
    private static readonly DiffuseProbeGrid Grid = new(Vector3.Zero, new(3), 4, 4, 4, 128);
    private static readonly DiffuseRegionPartition3D Partition = new();

    [Fact]
    public void HardCapAdmitsEveryInvalidatedRegionAndRejectsOldEpochs()
    {
        var policy = Create();
        var seen = new HashSet<int>();
        for (int frame = 0; frame < 4; frame++)
        {
            var plan = policy.Tick(TimeSpan.FromSeconds(.02));
            Assert.Equal(2048, policy.LastScheduledRayUpdates);
            int active = Enumerable.Range(0, 4).Single(index => plan.Regions[index].RaysPerProbe > 0);
            Assert.True(seen.Add(active));
            Assert.True(policy.Observe(Feedback(frame, plan, .2, 0)));
        }
        Assert.Equal(4, seen.Count);
        var old = policy.Tick(TimeSpan.FromSeconds(.02));
        policy.Invalidate([2]);
        policy.Observe(Feedback(5, old, 0, 0));
        var next = policy.Tick(TimeSpan.FromSeconds(.02));
        Assert.False(policy.States[2].CompleteCoverage);
        Assert.Equal(DiffuseRegionStrategy3D.Runtime, next.Regions[2].Strategy);
        Assert.Equal(128, next.Regions[2].RaysPerProbe);
        Assert.True(policy.RejectedObservations > 0);
    }

    [Fact]
    public void StableRegionsCompileAuditAndRespectCommitAndHysteresis()
    {
        var policy = Create(maximum: 8192);
        long sequence = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            var plan = policy.Tick(TimeSpan.FromSeconds(.02));
            policy.Observe(Feedback(sequence++, plan, 0, 0));
        }
        Assert.All(policy.States, state => Assert.Equal(DiffuseRegionStrategy3D.Compiled, state.Selected));
        Assert.Contains(policy.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "MinCommitActive");
        // Small fluctuations across the compiled/residual utility boundary must not chatter.
        var synthetic = new DiffuseRegionalPlan3D(Partition, policy.States.Select(state =>
            new DiffuseRegionCommand3D(DiffuseRegionStrategy3D.Residual, 16, state.Epoch)), policy.Tick(TimeSpan.Zero).PolicyIdentity);
        for (int frame = 0; frame < 5; frame++)
        {
            policy.Observe(Feedback(sequence++, synthetic, 0, .045));
            policy.Tick(TimeSpan.FromSeconds(.02));
        }
        Assert.Contains(policy.Inspector.Observe().Trace, entry => entry.Decision?.Reason == "HysteresisBlock");
        bool audit = false;
        for (int frame = 0; frame < 80; frame++)
        {
            var plan = policy.Tick(TimeSpan.FromSeconds(.02));
            audit |= plan.Regions.Any(command => command.Strategy == DiffuseRegionStrategy3D.Residual && command.RaysPerProbe > 0);
            policy.Observe(Feedback(sequence++, plan, 0, 0));
        }
        Assert.True(audit);
    }

    [Fact]
    public void InvalidFeedbackCannotPartiallyAdvanceCoverageAndDuplicateFramesAreRejected()
    {
        var policy = Create();
        var plan = policy.Tick(TimeSpan.Zero);
        var valid = Feedback(0, plan, 0, 0);
        var duplicate = valid with { Regions = Enumerable.Repeat(valid.Regions[0], 4).ToImmutableArray() };
        Assert.Throws<ArgumentException>(() => policy.Observe(duplicate));
        Assert.True(policy.Observe(valid));
        Assert.False(policy.Observe(valid));
        Assert.False(Create().Observe(valid));
        Assert.Throws<ArgumentException>(() => Create(maximum: 2047));
    }

    [Fact]
    public void InvalidationOverridesCompiledApplicationWhileDominatusHonorsItsCommit()
    {
        var policy = Create(maximum: 8192);
        for (int frame = 0; frame < 30; frame++)
        {
            var plan = policy.Tick(TimeSpan.FromSeconds(.02));
            policy.Observe(Feedback(frame, plan, 0, 0));
            if (policy.States.All(state => state.Selected == DiffuseRegionStrategy3D.Compiled)) break;
        }
        policy.Invalidate([0]);
        var dirty = policy.Tick(TimeSpan.FromSeconds(.001));
        Assert.Equal(DiffuseRegionStrategy3D.Compiled, policy.States[0].Selected);
        Assert.Equal(DiffuseRegionStrategy3D.Runtime, dirty.Regions[0].Strategy);
        Assert.Equal(128, dirty.Regions[0].RaysPerProbe);
        Assert.All(dirty.Regions.Skip(1), command => Assert.Equal(DiffuseRegionStrategy3D.Compiled, command.Strategy));
    }

    private static DiffuseRegionalPolicyController Create(int maximum = 2048) => new(Grid, Partition,
        new() { MaximumRayUpdates = maximum, AuditSeconds = .75 }, traceCapacity: 10000);

    private static DiffuseRegionalObservation3D Feedback(long sequence, DiffuseRegionalPlan3D plan, double baseline, double change) =>
        new(sequence, Grid, Partition, plan.Regions.Select((command, index) => new DiffuseRegionMeasurement3D(index,
            command.Epoch, command.Strategy, 16, command.RaysPerProbe, baseline, change)).ToImmutableArray(), plan.PolicyIdentity);
}
