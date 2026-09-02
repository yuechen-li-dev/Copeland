using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM4Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void SceneDefinitions_AreFlatValidatedTablesWithStableReferences()
    {
        TinyFarmScenes.Validate(TinyFarmScenes.All);
        Assert.Equal(5, TinyFarmScenes.All.Count);
        Assert.All(TinyFarmScenes.All, scene => Assert.Equal(scene.Objects.Count, scene.Layout.Count));
        Assert.All(TinyFarmScenes.All.SelectMany(scene => scene.Routes), route =>
        {
            Assert.Contains(TinyFarmScenes.All, scene => scene.Id == route.TargetScene);
            Assert.Contains(
                TinyFarmScenes.Get(route.TargetScene).Spawns,
                spawn => spawn.Id == route.TargetSpawn);
        });
    }

    [Fact]
    public void SceneValidation_RejectsDuplicateObjectsAndInvalidRouteTargets()
    {
        SceneDefinition duplicateObjects = Scene(
            TinyFarmSceneIds.Farm,
            [Object("same"), Object("same")],
            [Layout("same", 1, 1), Layout("same", 2, 1)],
            [new SceneSpawnDefinition(new SceneSpawnId("start"), new GridPosition(0, 0))],
            []);
        Assert.Throws<InvalidDataException>(() => TinyFarmScenes.Validate([duplicateObjects]));

        SceneDefinition invalidRoute = Scene(
            TinyFarmSceneIds.Farm,
            [new SceneObjectDefinition(new SceneObjectId("door"), SceneObjectKind.Portal, "Door", false)],
            [Layout("door", 1, 1)],
            [new SceneSpawnDefinition(new SceneSpawnId("start"), new GridPosition(0, 0))],
            [new SceneRoute(
                new SceneRouteId("missing"),
                TinyFarmSceneIds.Farm,
                new SceneObjectId("door"),
                new SceneId("missing"),
                new SceneSpawnId("missing"),
                "ENTER")]);
        Assert.Throws<InvalidDataException>(() => TinyFarmScenes.Validate([invalidRoute]));
    }

    [Fact]
    public void SpatialMovement_IsCardinalBoundedAndBlockedByTabularCollision()
    {
        TinyFarmState state = TinyFarmContent.CreateSceneState(definitions);
        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(11, 6));
        IntentResult blocked = Resolve(state, new SpatialMoveIntent(1, 0)).Results.Single();
        Assert.Equal(IntentReason.MovementBlocked, blocked.Reason);
        Assert.Equal(new GridPosition(11, 6), state.ActorScene(TinyFarmIds.Player).Position);

        ResolutionBatchResult moved = Resolve(state, new SpatialMoveIntent(0, 1));
        Assert.Equal(IntentResultStatus.Accepted, moved.Results.Single().Status);
        Assert.Equal(new GridPosition(11, 7), moved.State.ActorScene(TinyFarmIds.Player).Position);
        Assert.Equal(IntentReason.InvalidMovement, Resolve(state, new SpatialMoveIntent(1, 1)).Results.Single().Reason);
    }

    [Fact]
    public void SemanticCliMove_AcceptsDirectionAndDistanceAtomically()
    {
        SpatialMoveIntent parsed = Assert.IsType<SpatialMoveIntent>(TinyFarmCommandParser.Parse("move down 4 units"));
        Assert.Equal(new SpatialMoveIntent(0, 1, 4), parsed);

        TinyFarmState state = TinyFarmContent.CreateSceneState(definitions);
        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(6, 6));
        ResolutionBatchResult moved = Resolve(state, TinyFarmCommandParser.Parse("move down 4"));
        Assert.Equal(new GridPosition(6, 10), moved.State.ActorScene(TinyFarmIds.Player).Position);

        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(12, 7));
        string before = TinyFarmSemanticHash.Compute(state);
        IntentResult blocked = Resolve(state, TinyFarmCommandParser.Parse("move up 5 units")).Results.Single();
        Assert.Equal(IntentReason.MovementBlocked, blocked.Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(state));
    }

    [Fact]
    public void PortalRequiresInteractAndReducerSelectsAuthoredRouteAndSpawn()
    {
        TinyFarmState state = TinyFarmContent.CreateSceneState(definitions);
        Assert.Equal(IntentReason.NoInteraction, Resolve(state, new InteractIntent()).Results.Single().Reason);
        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(17, 6));

        ResolutionBatchResult result = Resolve(state, new InteractIntent());
        ActorSceneState player = result.State.ActorScene(TinyFarmIds.Player);
        Assert.Equal(TinyFarmSceneIds.Overworld, player.Scene);
        Assert.Equal(new GridPosition(3, 7), player.Position);
        Assert.Collection(
            result.Results.Single().Events,
            item => Assert.Equal(GameEventKind.SceneExited, item.Kind),
            item =>
            {
                Assert.Equal(GameEventKind.SceneEntered, item.Kind);
                Assert.Equal(new SceneRouteId("farm-overworld"), item.Route);
            });
    }

    [Fact]
    public void SceneSaveLoad_RestoresExactScenePositionAndCanonicalHash()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateSceneState(definitions), definitions);
        SetPlayerPlacement(session.State, TinyFarmSceneIds.Town, new GridPosition(14, 9));
        byte[] save = session.CaptureWeekSave();
        string expected = TinyFarmSemanticHash.Compute(session.State);
        session.Step(new SpatialMoveIntent(1, 0));

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        Assert.Equal(new ActorSceneState(TinyFarmIds.Player, TinyFarmSceneIds.Town, new GridPosition(14, 9)),
            loaded.State.ActorScene(TinyFarmIds.Player));
        Assert.Equal(expected, TinyFarmSemanticHash.Compute(loaded.State));
    }

    [Fact]
    public void FarmingRequiresPhysicalAdjacencyAndStillUsesResolver()
    {
        TinyFarmState state = TinyFarmContent.CreateSceneState(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 1));
        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(1, 8));
        Assert.Equal(
            IntentReason.NotAdjacent,
            Resolve(state, new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop)).Results.Single().Reason);

        SetPlayerPlacement(state, TinyFarmSceneIds.Farm, new GridPosition(7, 6));
        ResolutionBatchResult planted = Resolve(
            state,
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop));
        Assert.Equal(IntentResultStatus.Accepted, planted.Results.Single().Status);
        Assert.Equal(TinyFarmIds.TurnipCrop, planted.State.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop);
    }

    [Fact]
    public void DominatusGoalMovement_UpdatesAuthoritativeNpcSceneBeforeProjection()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateSceneState(definitions), definitions);
        SceneId before = session.State.ActorScene(TinyFarmIds.Elias).Scene;
        session.Step(new WaitIntent(240));
        SceneId after = session.State.ActorScene(TinyFarmIds.Elias).Scene;
        Assert.NotEqual(before, after);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions);
        Assert.DoesNotContain(frame.Actors, actor => actor.Id == TinyFarmIds.Elias);
    }

    [Fact]
    public void Projection_IsActiveSceneOnlyAndDeterministic()
    {
        TinyFarmState state = TinyFarmContent.CreateSceneState(definitions);
        TinyFarmFrame first = TinyFarmFrameProjector.Project(state, definitions);
        TinyFarmFrame second = TinyFarmFrameProjector.Project(state.DeepCopy(), definitions);
        Assert.Equal(TinyFarmSceneIds.Farm, first.ActiveScene);
        Assert.Equal(18, first.SceneWidth);
        Assert.Equal(12, first.SceneHeight);
        Assert.Equal(5, first.SceneObjects!.Count);
        Assert.Equal(
            TinyFarmFrameProjector.ComputeHash(first),
            TinyFarmFrameProjector.ComputeHash(second));
    }

    [Fact]
    public void CanonicalM4Journey_TraversesRequiredRoutesAndPreservesM1M2()
    {
        TinyFarmM4Proof proof = TinyFarmSceneScenario.Prove().Proof;
        Assert.Equal("A", proof.Outcome);
        Assert.True(proof.M1HashPreserved);
        Assert.True(proof.M2HashPreserved);
        Assert.True(proof.SaveLoadRestoredExactSceneAndPosition);
        Assert.True(proof.NpcCrossedScene);
        Assert.True(proof.SeedPurchased);
        Assert.True(proof.SeedPlanted);
        Assert.Contains(proof.RouteReductions, item => item.Route == new SceneRouteId("overworld-town"));
        Assert.Contains(proof.RouteReductions, item => item.Route == new SceneRouteId("town-store"));
        Assert.Contains(proof.RouteReductions, item => item.Route == new SceneRouteId("store-town"));
        Assert.Contains(proof.RouteReductions, item => item.Route == new SceneRouteId("town-overworld"));
    }

    [Fact]
    public void SameM4InputSequence_ProducesSameHashes()
    {
        TinyFarmM4Proof first = TinyFarmSceneScenario.Prove().Proof;
        TinyFarmM4Proof second = TinyFarmSceneScenario.Prove().Proof;
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.IntentResultHash, second.IntentResultHash);
        Assert.Equal(first.EventHash, second.EventHash);
        Assert.Equal(first.SceneRouteHash, second.SceneRouteHash);
        Assert.Equal(first.ProjectionHash, second.ProjectionHash);
    }

    private ResolutionBatchResult Resolve(TinyFarmState state, GameIntent intent)
    {
        return new TinyFarmResolver(definitions).Resolve(
            state,
            [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, 0, IntentSourceKind.Human)]);
    }

    private static void SetPlayerPlacement(TinyFarmState state, SceneId scene, GridPosition position)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[index] = new ActorSceneState(TinyFarmIds.Player, scene, position);
        ActorState player = state.Actor(TinyFarmIds.Player);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = player with { Location = TinyFarmScenes.LocationForScene(scene) };
    }

    private static SceneDefinition Scene(
        SceneId id,
        IReadOnlyList<SceneObjectDefinition> objects,
        IReadOnlyList<SceneLayoutRow> layout,
        IReadOnlyList<SceneSpawnDefinition> spawns,
        IReadOnlyList<SceneRoute> routes)
    {
        return new SceneDefinition(id, id.Value, 4, 4, objects, layout, spawns, routes);
    }

    private static SceneObjectDefinition Object(string id)
    {
        return new SceneObjectDefinition(new SceneObjectId(id), SceneObjectKind.Prop, id, false);
    }

    private static SceneLayoutRow Layout(string id, int x, int y)
    {
        return new SceneLayoutRow(new SceneObjectId(id), x, y, 1, 1, 0);
    }
}
