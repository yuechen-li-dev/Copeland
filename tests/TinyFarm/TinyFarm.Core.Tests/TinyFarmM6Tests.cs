using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM6Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void Anchors_AreStableUniqueWalkableAuthoredSemanticAddresses()
    {
        TinyFarmScenes.Validate(TinyFarmScenes.All);
        SceneAnchorDefinition[] anchors = TinyFarmScenes.All.SelectMany(scene => scene.Anchors).ToArray();
        Assert.NotEmpty(anchors);
        Assert.Equal(anchors.Length, anchors.Select(anchor => anchor.Id).Distinct().Count());
        Assert.All(anchors, anchor =>
        {
            SceneDefinition scene = TinyFarmScenes.Get(anchor.Scene);
            Assert.True(TinyFarmScenes.IsInBounds(scene, anchor.Position));
            Assert.False(TinyFarmScenes.IsBlocked(scene, anchor.Position));
            Assert.DoesNotContain(anchor.Id.Value, "#", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AnchorValidation_RejectsDuplicateOutOfBoundsAndInvalidReferences()
    {
        SceneAnchorId duplicate = new("duplicate.anchor");
        SceneDefinition first = EmptyScene(
            new SceneId("first"),
            [new SceneAnchorDefinition(duplicate, new SceneId("first"), At(1, 1), SceneAnchorKind.Spawn)]);
        SceneDefinition second = EmptyScene(
            new SceneId("second"),
            [new SceneAnchorDefinition(duplicate, new SceneId("second"), At(1, 1), SceneAnchorKind.Spawn)]);
        Assert.Throws<InvalidDataException>(() => TinyFarmScenes.Validate([first, second]));

        SceneDefinition invalid = EmptyScene(
            new SceneId("invalid"),
            [new SceneAnchorDefinition(
                new SceneAnchorId("invalid.anchor"),
                new SceneId("invalid"),
                At(9, 9),
                SceneAnchorKind.Work,
                new LocationId("missing"))]);
        Assert.Throws<InvalidDataException>(() => TinyFarmScenes.Validate([invalid]));
    }

    [Fact]
    public void Routes_TargetSpawnKindAnchorsWithoutSeparateCoordinateTruth()
    {
        Assert.All(TinyFarmScenes.All.SelectMany(scene => scene.Routes), route =>
        {
            SceneAnchorDefinition target = TinyFarmScenes.GetAnchor(route.TargetAnchor);
            Assert.Equal(route.TargetScene, target.Scene);
            Assert.Equal(SceneAnchorKind.Spawn, target.Kind);
        });
    }

    [Fact]
    public void DominatusSchedule_ResolvesToSemanticAnchor()
    {
        Assert.Equal(TinyFarmAnchorIds.FarmWorkArea, TinyFarmNpcController.ScheduledAnchor(TinyFarmIds.Elias, 8 * 60));
        Assert.Equal(TinyFarmAnchorIds.RiversideMeetingPoint, TinyFarmNpcController.ScheduledAnchor(TinyFarmIds.Elias, 13 * 60));
        Assert.Equal(TinyFarmAnchorIds.StoreCounter, TinyFarmNpcController.ScheduledAnchor(TinyFarmIds.Sela, 10 * 60));
        Assert.Equal(TinyFarmIds.GeneralStore, TinyFarmNpcController.ScheduledDestination(TinyFarmIds.Sela, 10 * 60));
    }

    [Fact]
    public void ActiveNpc_WalksTowardAnchorAndEmitsArrival()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);

        ScenePosition before = session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        bool arrived = false;
        for (int stepIndex = 0; stepIndex < 128 && !arrived; stepIndex++)
        {
            TinyFarmStepResult step = session.Step(new LookIntent());
            arrived = step.Results.SelectMany(result => result.Events).Any(gameEvent =>
                gameEvent.Kind == GameEventKind.AnchorReached
                && gameEvent.Actor == TinyFarmIds.Elias
                && gameEvent.Anchor == TinyFarmAnchorIds.FarmWorkArea);
        }

        Assert.NotEqual(before, session.State.ActorScene(TinyFarmIds.Elias).WorldPosition);
        Assert.True(arrived);
    }

    [Fact]
    public void InactiveNpcs_AdvanceCoarselyWithoutNavigationQueries()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(6, 6), ActorFacing.Down);
        var planner = new CountingPlanner();
        var session = new TinyFarmSession(state, definitions, planner);

        session.Step(new WaitIntent(300));

        Assert.Equal(0, planner.QueryCount);
        Assert.Equal(0, session.NavigationPlanCount);
        Assert.All(session.State.Actors.Where(actor => !actor.IsPlayer), actor =>
        {
            ActorSceneState placement = session.State.ActorScene(actor.Id);
            Assert.Equal(TinyFarmScenes.SceneForLocation(actor.Location), placement.Scene);
        });
    }

    [Fact]
    public void InactiveToActive_RealizesDeterministicallyThenWalksToAnchor()
    {
        (ActorSceneState Placement, string Hash, int Plans) first = ActivateTownMara();
        (ActorSceneState Placement, string Hash, int Plans) second = ActivateTownMara();

        Assert.Equal(first, second);
        Assert.True(first.Plans > 0);
        Assert.Equal(TinyFarmSceneIds.Town, first.Placement.Scene);
    }

    [Fact]
    public void ActiveToInactive_DiscardsPathAndKeepsOneConsistentAuthority()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Farm, At(16, 6), ActorFacing.Right);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var planner = new CountingPlanner();
        var session = new TinyFarmSession(state, definitions, planner);

        session.Step(new LookIntent());
        int activeQueries = planner.QueryCount;
        session.Step(new InteractIntent(new SceneObjectId("farm-exit")));
        session.Step(new WaitIntent(60));

        Assert.Equal(activeQueries, planner.QueryCount);
        Assert.Equal(1, session.ActivationCount);
        Assert.Equal(1, session.DeactivationCount);
        ActorState elias = session.State.Actor(TinyFarmIds.Elias);
        Assert.Equal(TinyFarmScenes.SceneForLocation(elias.Location), session.State.ActorScene(elias.Id).Scene);
    }

    [Fact]
    public void SaveLoadActiveNpc_RestoresExactPositionAndRecomputesPath()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);
        session.Step(new LookIntent());
        ActorSceneState saved = session.State.ActorScene(TinyFarmIds.Elias);
        byte[] bytes = session.CaptureWeekSave();

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(bytes, definitions);
        Assert.Equal(saved, loaded.State.ActorScene(TinyFarmIds.Elias));
        Assert.DoesNotContain("DotRecast", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        loaded.Step(new LookIntent());
        Assert.True(loaded.NavigationPlanCount > 0);
    }

    [Fact]
    public void SaveLoadInactiveNpc_ProducesSameLaterActivation()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(10, 5), ActorFacing.Right);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, At(10, 12), ActorFacing.Up);
        SetActorLocation(state, TinyFarmIds.Mara, TinyFarmIds.TownSquare);
        var original = new TinyFarmSession(state, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(original.CaptureWeekSave(), definitions);

        ActorSceneState originalPlacement = EnterTownAndAdvance(original);
        ActorSceneState loadedPlacement = EnterTownAndAdvance(loaded);

        Assert.Equal(originalPlacement, loadedPlacement);
        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
    }

    [Fact]
    public void MissingAndUnreachableAnchors_AreTypedFailuresWithoutTeleport()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        var missingSession = new TinyFarmSession(state, definitions);
        ActorSceneState before = missingSession.State.ActorScene(TinyFarmIds.Player);
        IntentResult missing = Human(missingSession.Step(
            new NavigateToAnchorIntent(new SceneAnchorId("missing.anchor"))));
        Assert.Equal(IntentReason.MissingAnchor, missing.Reason);
        Assert.Equal(before, missingSession.State.ActorScene(TinyFarmIds.Player));

        var failedSession = new TinyFarmSession(state, definitions, new FailingPlanner());
        IntentResult unreachable = Human(failedSession.Step(
            new NavigateToAnchorIntent(TinyFarmAnchorIds.FarmWorkArea)));
        Assert.Equal(IntentReason.AnchorUnreachable, unreachable.Reason);
        Assert.Equal(before, failedSession.State.ActorScene(TinyFarmIds.Player));
    }

    [Fact]
    public void SemanticControllerNavigation_UsesAnchorAndOrdinaryMovementIntents()
    {
        NavigateToAnchorIntent parsed = Assert.IsType<NavigateToAnchorIntent>(
            TinyFarmCommandParser.Parse("go to store counter"));
        Assert.Equal(TinyFarmAnchorIds.StoreCounter, parsed.Anchor);

        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        var session = new TinyFarmSession(state, definitions);
        TinyFarmStepResult step = session.Step(new NavigateToAnchorIntent(TinyFarmAnchorIds.FarmWorkArea));
        IntentResult human = Human(step);
        Assert.IsType<SpatialMoveIntent>(human.Envelope.Intent);
        Assert.Equal(IntentResultStatus.Accepted, human.Status);
    }

    [Fact]
    public void CrossSceneCoarseProgression_BecomesVisibleAnchorLocomotion()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 13 * 60;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(6, 6), ActorFacing.Down);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(16, 6), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);

        session.Step(new LookIntent());
        session.Step(new LookIntent());
        Assert.Equal(TinyFarmIds.Riverside, session.State.Actor(TinyFarmIds.Elias).Location);
        ScenePosition entry = session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;

        SetPlacement(session.State, TinyFarmIds.Player, TinyFarmSceneIds.Riverside, At(2, 5), ActorFacing.Right);
        TinyFarmStepResult active = session.Step(new LookIntent());

        Assert.NotEqual(entry, session.State.ActorScene(TinyFarmIds.Elias).WorldPosition);
        Assert.Contains(active.Results, result =>
            result.Envelope.Actor == TinyFarmIds.Elias
            && result.Envelope.Intent is SpatialMoveIntent);
    }

    [Fact]
    public void CoreHasNoRendererOrDotRecastReferences()
    {
        string[] references = typeof(SceneAnchorId).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(references, name => name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("DotRecast", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveValidation_RejectsCoarseAndSpatialAuthorityDisagreement()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Riverside);
        var session = new TinyFarmSession(state, definitions);

        Assert.Throws<InvalidDataException>(() => session.CaptureWeekSave());
    }

    [Fact]
    public void CanonicalM6Scenario_ProvesOutcomeAAndExactLegacyHashes()
    {
        TinyFarmM6Proof proof = TinyFarmAnchorHandoffScenario.Prove().Proof;
        Assert.Equal("A", proof.Outcome);
        Assert.Equal("dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333", proof.M1Hash);
        Assert.Equal("4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3", proof.M2Hash);
        Assert.True(proof.ActiveSaveLoadExact);
        Assert.True(proof.InactiveSaveLoadExact);
        Assert.True(proof.InactiveNpcUsedNoNavigation);
    }

    [Fact]
    public void CanonicalM6Scenario_RepeatsAllSemanticHashes()
    {
        TinyFarmM6Proof first = TinyFarmAnchorHandoffScenario.Prove().Proof;
        TinyFarmM6Proof second = TinyFarmAnchorHandoffScenario.Prove().Proof;
        Assert.Equal(first.StateHash, second.StateHash);
        Assert.Equal(first.ResultsHash, second.ResultsHash);
        Assert.Equal(first.EventsHash, second.EventsHash);
        Assert.Equal(first.AnchorsHash, second.AnchorsHash);
        Assert.Equal(first.HandoffHash, second.HandoffHash);
        Assert.Equal(first.NavigationHash, second.NavigationHash);
        Assert.Equal(first.ProjectionHash, second.ProjectionHash);
    }

    private (ActorSceneState Placement, string Hash, int Plans) ActivateTownMara()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(10, 5), ActorFacing.Right);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, At(10, 12), ActorFacing.Up);
        SetActorLocation(state, TinyFarmIds.Mara, TinyFarmIds.TownSquare);
        var session = new TinyFarmSession(state, definitions);

        EnterTownAndAdvance(session);
        return (
            session.State.ActorScene(TinyFarmIds.Mara),
            TinyFarmSemanticHash.Compute(session.State),
            session.NavigationPlanCount);
    }

    private static ActorSceneState EnterTownAndAdvance(TinyFarmSession session)
    {
        session.Step(new InteractIntent(new SceneObjectId("town-entrance")));
        session.Step(new LookIntent());
        return session.State.ActorScene(TinyFarmIds.Mara);
    }

    private static IntentResult Human(TinyFarmStepResult step)
    {
        return step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
    }

    private static SceneDefinition EmptyScene(SceneId id, IReadOnlyList<SceneAnchorDefinition> anchors)
    {
        return new SceneDefinition(id, id.Value, 4, 4, [], [], anchors, []);
    }

    private static ScenePosition At(int x, int y)
    {
        return ScenePosition.FromGrid(new GridPosition(x, y));
    }

    private static void SetPlacement(
        TinyFarmState state,
        ActorId actor,
        SceneId scene,
        ScenePosition position,
        ActorFacing facing)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[index] = new ActorSceneState(actor, scene, position, facing);
        SetActorLocation(state, actor, TinyFarmScenes.LocationForScene(scene));
    }

    private static void SetActorLocation(TinyFarmState state, ActorId actor, LocationId location)
    {
        int index = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[index] = state.MutableActors[index] with { Location = location };
    }

    private sealed class CountingPlanner : INavigationPlanner
    {
        private readonly DotRecastNavigationPlanner inner = new();

        public int QueryCount { get; private set; }

        public NavigationPath FindPath(SceneDefinition scene, ScenePosition start, ScenePosition goal)
        {
            QueryCount++;
            return inner.FindPath(scene, start, goal);
        }
    }

    private sealed class FailingPlanner : INavigationPlanner
    {
        public NavigationPath FindPath(SceneDefinition scene, ScenePosition start, ScenePosition goal)
        {
            return new NavigationPath(scene.Id, [], NavigationFailure.NoPath, 0, 0, "test-unreachable");
        }
    }
}
