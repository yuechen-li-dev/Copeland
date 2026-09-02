using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM5Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void ContinuousMovement_UsesSubTileWorldUnitsFacingAndSemanticCollision()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        ActorSceneState before = state.ActorScene(TinyFarmIds.Player);
        ResolutionBatchResult moved = Resolve(state, new SpatialMoveIntent(1, 0, 128));

        ActorSceneState after = moved.State.ActorScene(TinyFarmIds.Player);
        Assert.Equal(before.WorldPosition.XUnits + 128, after.WorldPosition.XUnits);
        Assert.Equal(before.WorldPosition.YUnits, after.WorldPosition.YUnits);
        Assert.Equal(ActorFacing.Right, after.Facing);
        Assert.Equal(before.Position, after.Position);

        SetPlacement(
            state,
            TinyFarmIds.Player,
            TinyFarmSceneIds.Farm,
            new ScenePosition((11 * 1024) + 900, (6 * 1024) + 512),
            ActorFacing.Right);
        string hash = TinyFarmSemanticHash.Compute(state);
        IntentResult blocked = Resolve(state, new SpatialMoveIntent(1, 0, 256)).Results.Single();
        Assert.Equal(IntentReason.MovementBlocked, blocked.Reason);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(state));
    }

    [Fact]
    public void InteractionTargeting_IsForwardRangedSemanticAndStable()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Town, At(10, 7), ActorFacing.Right);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, At(11, 7), ActorFacing.Left);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Town, At(11, 7), ActorFacing.Left);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));
        Assert.Equal(TinyFarmIds.Elias, target.Actor);

        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(4, 7), ActorFacing.Down);
        IntentResult talked = Resolve(state, new InteractIntent()).Results.Single();
        Assert.Equal(IntentResultStatus.Accepted, talked.Status);
        Assert.Contains(talked.Events, item => item.Kind == GameEventKind.Conversation && item.Target == TinyFarmIds.Mara);

        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, At(9, 7), ActorFacing.Right);
        Assert.Equal(
            IntentReason.NoInteractionTarget,
            Resolve(state, new InteractIntent()).Results.Single().Reason);
    }

    [Fact]
    public void PlotInteraction_UsesFacingTargetAndExistingFarmReducer()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 1));
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Farm, At(6, 5), ActorFacing.Right);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));
        Assert.Equal(TinyFarmIds.PlotOne, target.Plot);
        ResolutionBatchResult result = Resolve(state, new InteractIntent());
        Assert.Equal(IntentResultStatus.Accepted, result.Results.Single().Status);
        Assert.Equal(TinyFarmIds.TurnipCrop, result.State.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop);
    }

    [Fact]
    public void DotRecast_PathGoesAroundObstacleAndIsCanonical()
    {
        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        var planner = new DotRecastNavigationPlanner();
        ScenePosition start = At(11, 4);
        ScenePosition goal = At(13, 4);

        NavigationPath first = planner.FindPath(farm, start, goal);
        NavigationPath second = planner.FindPath(farm, start, goal);
        Assert.True(first.Succeeded, $"{first.Failure}: {first.FailureDetail}");
        Assert.Equal(first.Waypoints, second.Waypoints);
        Assert.True(first.Waypoints.Count >= 3);
        Assert.All(first.Waypoints, waypoint =>
        {
            Assert.True(TinyFarmScenes.IsInBounds(farm, waypoint));
            Assert.False(TinyFarmScenes.IsBlocked(farm, waypoint));
        });

        NavigationPath blocked = planner.FindPath(farm, start, At(12, 4));
        Assert.Equal(NavigationFailure.GoalBlocked, blocked.Failure);
        Assert.Empty(blocked.Waypoints);
    }

    [Fact]
    public void DotRecast_UnreachableWalkableGoalReturnsTypedFailure()
    {
        var wall = new SceneObjectDefinition(new SceneObjectId("wall"), SceneObjectKind.Prop, "Wall", true);
        var scene = new SceneDefinition(
            new SceneId("split"),
            "Split",
            6,
            4,
            [wall],
            [new SceneLayoutRow(wall.Id, 3, 0, 1, 4, 0)],
            [new SceneAnchorDefinition(
                new SceneAnchorId("split.left"),
                new SceneId("split"),
                At(1, 1),
                SceneAnchorKind.Spawn)],
            []);

        NavigationPath path = new DotRecastNavigationPlanner().FindPath(scene, At(1, 1), At(4, 1));
        Assert.Equal(NavigationFailure.NoPath, path.Failure);
        Assert.Empty(path.Waypoints);
    }

    [Fact]
    public void MidTileSaveLoad_RestoresPositionFacingSceneAndHash()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateContinuousSceneState(definitions), definitions);
        session.Step(new SpatialMoveIntent(1, 0, 333));
        ActorSceneState expected = session.State.ActorScene(TinyFarmIds.Player);
        string hash = TinyFarmSemanticHash.Compute(session.State);

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(session.CaptureWeekSave(), definitions);
        Assert.Equal(expected, loaded.State.ActorScene(TinyFarmIds.Player));
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(loaded.State));
    }

    [Fact]
    public void VisibleNpc_UsesSharedSubTileLocomotionWithoutTeleporting()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);

        ScenePosition before = session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        TinyFarmStepResult step = session.Step(new LookIntent());
        ScenePosition after = step.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        Assert.NotEqual(before, after);
        Assert.True(Math.Abs(after.XUnits - before.XUnits) <= ScenePosition.UnitsPerTile / 8);
        Assert.Contains(step.Results, result =>
            result.Envelope.Actor == TinyFarmIds.Elias
            && result.Envelope.Intent is SpatialMoveIntent);
    }

    [Fact]
    public void RepeatedSubTileInput_HasIdenticalResultsEventsAndHash()
    {
        (string State, string Results, string Events) first = RunMovementSequence();
        (string State, string Results, string Events) second = RunMovementSequence();
        Assert.Equal(first, second);
    }

    [Fact]
    public void FixedMovementSampling_IsFrameRateIndependent()
    {
        string sixtyHertz = RunCadence(60);
        string oneFortyFourHertz = RunCadence(144);
        Assert.Equal(sixtyHertz, oneFortyFourHertz);
    }

    [Fact]
    public void PortalTargeting_UsesSceneRouteWhileHighLevelGoalRemainsSeparate()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Farm, At(17, 6), ActorFacing.Right);
        SetActorLocation(state, TinyFarmIds.Mara, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);

        TinyFarmStepResult step = session.Step(new LookIntent());
        ActorSceneState mara = step.State.ActorScene(TinyFarmIds.Mara);
        Assert.Equal(TinyFarmSceneIds.Overworld, mara.Scene);
        Assert.NotEqual(TinyFarmSceneIds.Town, mara.Scene);
        Assert.Contains(step.Results, result =>
            result.Envelope.Actor == TinyFarmIds.Mara
            && result.Envelope.Intent is InteractIntent
            && result.Events.Any(gameEvent => gameEvent.Kind == GameEventKind.SceneEntered));
    }

    [Fact]
    public void NavigationPath_IsDerivedAndRecomputedAfterLoad()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);
        byte[] save = session.CaptureWeekSave();
        Assert.DoesNotContain("DotRecast", System.Text.Encoding.UTF8.GetString(save), StringComparison.Ordinal);

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        ScenePosition before = loaded.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        loaded.Step(new LookIntent());
        Assert.NotEqual(before, loaded.State.ActorScene(TinyFarmIds.Elias).WorldPosition);
    }

    private (string State, string Results, string Events) RunMovementSequence()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        var results = new List<string>();
        var events = new List<string>();
        foreach (GameIntent intent in new GameIntent[]
                 {
                     new SpatialMoveIntent(0, 1, 128),
                     new SpatialMoveIntent(1, 0, 128),
                     new SpatialMoveIntent(1, 0, 64),
                     new SpatialMoveIntent(0, -1, 32)
                 })
        {
            ResolutionBatchResult batch = Resolve(state, intent);
            state = batch.State;
            results.AddRange(batch.Results.Select(item => $"{item.Status}:{item.Reason}"));
            events.AddRange(batch.Results.SelectMany(item => item.Events).Select(item => item.Kind.ToString()));
        }
        return (TinyFarmSemanticHash.Compute(state), string.Join('|', results), string.Join('|', events));
    }

    private string RunCadence(int samples)
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        var stepper = new FixedMovementStepper();
        long baseTicks = TimeSpan.TicksPerSecond / samples;
        long remainder = TimeSpan.TicksPerSecond % samples;
        int emitted = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            long ticks = baseTicks + (sample < remainder ? 1 : 0);
            foreach (SpatialMoveIntent intent in stepper.Advance(TimeSpan.FromTicks(ticks), 0, 1))
            {
                state = Resolve(state, intent).State;
                emitted++;
            }
        }
        Assert.Equal(60, emitted);
        return TinyFarmSemanticHash.Compute(state);
    }

    private ResolutionBatchResult Resolve(TinyFarmState state, GameIntent intent)
    {
        return new TinyFarmResolver(definitions).Resolve(
            state,
            [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, 0, IntentSourceKind.Human)]);
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
}
