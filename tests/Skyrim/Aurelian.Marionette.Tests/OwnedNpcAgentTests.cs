using System.Numerics;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcAgentTests
{
    private static readonly OwnedNpcIdentity Identity = new(3, 7, 0xFF000800, 0x73FBF);

    [Fact]
    public void AcceptanceDoesNotAdvanceGoalAndArrivalSelectsTheReturnLeg()
    {
        var runtime = Create();
        OwnedNpcAgentIntent outbound = NextIntent(runtime);
        Assert.Equal("Patrol", runtime.Status.SelectedOption);
        Assert.Equal(OwnedNpcAgentAction.Walk, outbound.Action);
        runtime.Receive(new OwnedNpcAgentReceipt(outbound.Id, Identity, true, 11, ""));
        runtime.Observe(Fact(2, Vector3.Zero, Movement(11, "walking", outbound.Target)));
        for (int tick = 0; tick < 20; tick++)
        {
            runtime.Tick(0.1f);
            Assert.Null(runtime.TakeIntent());
        }
        Assert.Equal(0, runtime.Status.CompletedLegs);
        runtime.Observe(Fact(3, outbound.Target, Movement(11, "arrived", outbound.Target, 0)));
        OwnedNpcAgentIntent inbound = NextIntent(runtime);
        Assert.Equal(1, runtime.Status.CompletedLegs);
        Assert.Equal(1, runtime.Status.Waypoint);
        Assert.Equal(Vector3.Zero, inbound.Target);
        Assert.NotEqual(outbound.Id, inbound.Id);
    }

    [Fact]
    public void FailedMovementRetriesSameGoalAfterBackoffThenReleasesAtTheBudget()
    {
        var runtime = Create(maximumRetries: 1);
        OwnedNpcAgentIntent first = NextIntent(runtime);
        runtime.Receive(new OwnedNpcAgentReceipt(first.Id, Identity, true, 11, ""));
        runtime.Observe(Fact(2, Vector3.Zero, Movement(11, "blocked", first.Target)));
        OwnedNpcAgentIntent retry = NextIntent(runtime);
        Assert.Equal("Recover", runtime.Status.SelectedOption);
        Assert.Equal(first.Target, retry.Target);
        Assert.Equal(0, runtime.Status.CompletedLegs);
        runtime.Receive(new OwnedNpcAgentReceipt(retry.Id, Identity, true, 12, ""));
        runtime.Observe(Fact(3, Vector3.Zero, Movement(12, "timed_out", retry.Target)));
        OwnedNpcAgentIntent release = NextIntent(runtime);
        Assert.Equal(OwnedNpcAgentAction.Release, release.Action);
        Assert.Equal("RetryBudgetExhausted", runtime.Status.SelectedOption);
        runtime.Receive(new OwnedNpcAgentReceipt(release.Id, Identity, true, 0, "retirement_requested"));
        for (int tick = 0; tick < 30; tick++)
        {
            runtime.Tick(0.1f);
            Assert.Null(runtime.TakeIntent());
        }
        Assert.Equal("stopped", runtime.Status.State);
    }

    [Fact]
    public void ARefusedWriteConsumesTheRetryBudgetWithoutInventingMovement()
    {
        var runtime = Create(maximumRetries: 0);
        OwnedNpcAgentIntent first = NextIntent(runtime);
        runtime.Receive(new OwnedNpcAgentReceipt(first.Id, Identity, false, 0, "stale_npc_observation"));
        OwnedNpcAgentIntent release = NextIntent(runtime);
        Assert.Equal(OwnedNpcAgentAction.Release, release.Action);
        Assert.Equal(0, runtime.Status.CompletedLegs);
        Assert.Equal(1, runtime.Status.ConsecutiveFailures);
    }

    [Fact]
    public void BodyLossDuringWalkingRequestsRetirementInsteadOfAReplacementMove()
    {
        var runtime = Create();
        OwnedNpcAgentIntent walk = NextIntent(runtime);
        runtime.Receive(new OwnedNpcAgentReceipt(walk.Id, Identity, true, 11, ""));
        runtime.Observe(Fact(0, Vector3.Zero, null) with { Available = false });
        Assert.Equal(OwnedNpcAgentAction.Release, NextIntent(runtime).Action);
        Assert.Equal("BodyLost", runtime.Status.SelectedOption);
    }

    [Fact]
    public void AForeignTerminalMovementIsNotAnArrivalForThisOperation()
    {
        var runtime = Create(maximumRetries: 0);
        OwnedNpcAgentIntent walk = NextIntent(runtime);
        runtime.Receive(new OwnedNpcAgentReceipt(walk.Id, Identity, true, 11, ""));
        runtime.Observe(Fact(2, walk.Target, Movement(99, "arrived", walk.Target, 0)));
        Assert.Equal(OwnedNpcAgentAction.Release, NextIntent(runtime).Action);
        Assert.Equal(0, runtime.Status.CompletedLegs);
    }

    [Fact]
    public void RejectsStaleFactsForeignLeasesAndDuplicateReceipts()
    {
        var runtime = Create();
        Assert.Throws<InvalidDataException>(() => runtime.Observe(Fact(1, Vector3.Zero, null)));
        Assert.Throws<InvalidDataException>(() => runtime.Observe(Fact(2, Vector3.Zero, null)
            with { Identity = Identity with { WorldGeneration = 4 } }));
        OwnedNpcAgentIntent walk = NextIntent(runtime);
        var receipt = new OwnedNpcAgentReceipt(walk.Id, Identity, true, 11, "");
        Assert.Throws<InvalidDataException>(() => runtime.Receive(receipt with { IntentId = walk.Id + 1 }));
        runtime.Receive(receipt);
        Assert.Throws<InvalidDataException>(() => runtime.Receive(receipt));
    }

    [Fact]
    public void BodyLossCannotResetPhysicalObservationOrdering()
    {
        var runtime = Create();
        runtime.Observe(Fact(0, Vector3.Zero, null) with { Available = false });
        Assert.Throws<InvalidDataException>(() => runtime.Observe(Fact(1, Vector3.Zero, null)));
    }

    [Fact]
    public void ReadyClockCanFreezeWithoutAdvancingRestOrRepeatingCommands()
    {
        var runtime = new OwnedNpcAgentRuntime(Fact(1, Vector3.Zero, null), restSeconds: 2);
        OwnedNpcAgentIntent walk = NextIntent(runtime);
        runtime.Receive(new OwnedNpcAgentReceipt(walk.Id, Identity, true, 11, ""));
        runtime.Observe(Fact(2, walk.Target, Movement(11, "arrived", walk.Target, 0)));
        for (int tick = 0; tick < 10; tick++) runtime.Tick(0);
        Assert.Equal("resting", runtime.Status.State);
        Assert.Null(runtime.TakeIntent());
        Assert.Equal(OwnedNpcAgentAction.Walk, NextIntent(runtime).Action);
    }

    private static OwnedNpcAgentRuntime Create(int maximumRetries = 2)
    {
        return new OwnedNpcAgentRuntime(Fact(1, Vector3.Zero, null), restSeconds: 0.3f,
            maximumRetries: maximumRetries);
    }

    private static OwnedNpcAgentFact Fact(ulong sequence, Vector3 position, OwnedNpcMovement? movement)
    {
        return new OwnedNpcAgentFact(Identity, sequence, true, position, movement);
    }

    private static OwnedNpcMovement Movement(ulong id, string state, Vector3 target, float distance = 300)
    {
        return new OwnedNpcMovement(id, state, "", target.X, target.Y, target.Z, 48,
            distance, 1, 0xFF000900, true);
    }

    private static OwnedNpcAgentIntent NextIntent(OwnedNpcAgentRuntime runtime)
    {
        for (int tick = 0; tick < 100; tick++)
        {
            runtime.Tick(0.1f);
            if (runtime.TakeIntent() is { } intent) return intent;
        }
        throw new InvalidOperationException("agent_produced_no_intent:" + runtime.Status);
    }
}
