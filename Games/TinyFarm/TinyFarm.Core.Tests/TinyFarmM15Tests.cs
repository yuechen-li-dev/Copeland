using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM15Tests
{
    [Fact]
    public void InternalMovementCore_MatchesPublicAcceptedMovementFacingRestAndEvent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState initial = TinyFarmM14ControlStates.Create(definitions, "wander");
        SetResting(initial, TinyFarmIds.Elias, true);

        AssertCoreMatchesPublic(
            definitions,
            initial,
            TinyFarmIds.Elias,
            new SpatialMoveIntent(1, 0, TinyFarmSession.NpcDistancePerLocomotionStep));
    }

    [Fact]
    public void InternalMovementCore_MatchesBlockedOutOfBoundsInvalidAndUnknownActorFailures()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState blocked = TinyFarmM14ControlStates.Create(definitions, "wander");
        SetPlacement(
            blocked,
            TinyFarmIds.Elias,
            new ScenePosition((11 * 1024) + 900, (6 * 1024) + 512));
        AssertCoreMatchesPublic(
            definitions,
            blocked,
            TinyFarmIds.Elias,
            new SpatialMoveIntent(1, 0, 256));

        TinyFarmState outOfBounds = TinyFarmM14ControlStates.Create(definitions, "wander");
        SetPlacement(outOfBounds, TinyFarmIds.Elias, new ScenePosition(8, 8));
        AssertCoreMatchesPublic(
            definitions,
            outOfBounds,
            TinyFarmIds.Elias,
            new SpatialMoveIntent(-1, 0, 16));

        TinyFarmState invalid = TinyFarmM14ControlStates.Create(definitions, "wander");
        AssertCoreMatchesPublic(
            definitions,
            invalid,
            TinyFarmIds.Elias,
            new SpatialMoveIntent(1, 0, 0));
        AssertCoreMatchesPublic(
            definitions,
            invalid,
            new ActorId("missing"),
            new SpatialMoveIntent(1, 0, 16));
    }

    [Fact]
    public void FixedPlayerLocomotion_MatchesPublicSpatialIntentPath()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState initial = TinyFarmM14ControlStates.Create(definitions, "wander");
        var fixedHost = new TinyFarmSimulationHost(
            new TinyFarmSession(initial, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        var publicHost = new TinyFarmSimulationHost(
            new TinyFarmSession(initial, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);

        fixedHost.SetPlayerMovement(1, 0);
        TinyFarmHostAdvanceResult fixedResult = fixedHost.AdvanceHostTime(TimeSpan.FromMilliseconds(17));
        TinyFarmStepResult publicResult = publicHost.ExecuteIntent(
            new SpatialMoveIntent(1, 0, ScenePosition.UnitsPerTile / 8));

        Assert.Equal(
            TinyFarmSemanticHash.Compute(publicHost.Session.State),
            TinyFarmSemanticHash.Compute(fixedHost.Session.State));
        Assert.Equal(publicResult.Results.Single().Status, fixedResult.Results.Single().Status);
        Assert.Equal(publicResult.Results.Single().Reason, fixedResult.Results.Single().Reason);
        Assert.Equal(publicResult.Results.Single().Events, fixedResult.Results.Single().Events);
    }

    [Fact]
    public void MovementCore_HasOnlyLocalizedReplacementAllocation()
    {
        TinyFarmM15CoreMeasurement measurement = TinyFarmM15Scenario.MeasureMovementCore(10_000);

        Assert.True(
            measurement.BytesPerReduction < 128,
            $"Movement core allocated {measurement.BytesPerReduction:F2} B/reduction.");
    }

    [Fact]
    public void FullAuthoritativeFollower_IsAllocationBoundedAfterWarmup()
    {
        TinyFarmM15MovementMeasurement measurement =
            TinyFarmM15Scenario.MeasureAuthoritativeLocomotion(100_000);

        Assert.Equal(100_000, measurement.ReductionCount);
        Assert.True(
            measurement.BytesPerReduction < 1_024,
            $"Authoritative follower allocated {measurement.BytesPerReduction:F2} B/reduction.");
        Assert.True(measurement.PathQueries < 500);
        Assert.True(measurement.PolicyEvaluations < 1_500);
    }

    [Fact]
    public void EvidenceStateSnapshots_AreOptInAndMeasurablyMoreExpensive()
    {
        TinyFarmM15MovementMeasurement runtime =
            TinyFarmM15Scenario.MeasureAuthoritativeLocomotion(10_000);
        TinyFarmM15MovementMeasurement observed =
            TinyFarmM15Scenario.MeasureObservedLocomotion(10_000);

        Assert.True(observed.BytesPerReduction > runtime.BytesPerReduction + 1_000);
    }

    [Fact]
    public void SpatialMoveIntent_RemainsThePublicPolymorphicReferenceIntent()
    {
        Assert.False(typeof(SpatialMoveIntent).IsValueType);
        Assert.True(typeof(GameIntent).IsAssignableFrom(typeof(SpatialMoveIntent)));
    }

    private static void AssertCoreMatchesPublic(
        TinyFarmDefinitions definitions,
        TinyFarmState initial,
        ActorId actor,
        SpatialMoveIntent intent)
    {
        TinyFarmState publicInput = initial.DeepCopy();
        TinyFarmState coreState = initial.DeepCopy();
        var envelope = new IntentEnvelope(actor, intent, initial.Minute, 1, IntentSourceKind.Dominatus);
        var resolver = new TinyFarmResolver(definitions);
        string initialHash = TinyFarmSemanticHash.Compute(initial);

        ResolutionBatchResult publicBatch = resolver.Resolve(publicInput, [envelope]);
        SpatialMoveReductionResult reduction = resolver.ResolveSpatialMoveCore(
            coreState,
            actor,
            intent.DeltaX,
            intent.DeltaY,
            intent.Distance);
        IntentResult coreResult = TinyFarmResolver.MaterializeSpatialMoveResult(envelope, reduction);
        IntentResult publicResult = publicBatch.Results.Single();

        Assert.Equal(publicResult.Status, coreResult.Status);
        Assert.Equal(publicResult.Reason, coreResult.Reason);
        Assert.Equal(publicResult.Events, coreResult.Events);
        Assert.Equal(TinyFarmSemanticHash.Compute(publicBatch.State), TinyFarmSemanticHash.Compute(coreState));
        if (publicResult.Status != IntentResultStatus.Accepted)
        {
            Assert.Equal(initialHash, TinyFarmSemanticHash.Compute(coreState));
        }
    }

    private static void SetPlacement(TinyFarmState state, ActorId actor, ScenePosition position)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        ActorSceneState current = state.MutableActorScenes[index];
        state.MutableActorScenes[index] = current with
        {
            Scene = TinyFarmSceneIds.Farm,
            WorldPosition = position
        };
    }

    private static void SetResting(TinyFarmState state, ActorId actor, bool resting)
    {
        int index = state.MutableActorEnergy.FindIndex(item => item.Actor == actor);
        ActorEnergyState current = state.MutableActorEnergy[index];
        state.MutableActorEnergy[index] = current with { IsResting = resting };
    }
}
