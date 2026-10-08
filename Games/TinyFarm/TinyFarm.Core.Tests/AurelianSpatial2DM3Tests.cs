using Aurelian.Spatial2D;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class AurelianSpatial2DM3Tests
{
    [Fact]
    public void CanonicalBlockedAndUnblockedMovesHaveExactLegacyParity()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);

        AssertParity(definitions, farm, new ScenePosition(12164, 6656), 1, 0, 256);
        AssertParity(definitions, farm, ScenePosition.FromGrid(new GridPosition(9, 7)), 1, 0, 128);
    }

    [Fact]
    public void DotRecastProposalIsValidatedBySpatialSweepBeforeAuthorityChanges()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        ScenePosition start = ScenePosition.FromGrid(new GridPosition(11, 4));
        ScenePosition goal = ScenePosition.FromGrid(new GridPosition(13, 4));
        NavigationPath path = new DotRecastNavigationPlanner().FindPath(farm, start, goal);
        Assert.True(path.Succeeded, path.FailureDetail);

        ScenePosition proposed = path.Waypoints[1];
        SpatialVector2D displacement = new(
            proposed.XUnits - start.XUnits,
            proposed.YUnits - start.YUnits);
        SpatialMoveResult validated = TinyFarmSpatialWorldAdapter
            .BuildStaticWorld(farm)
            .SweepAndSlide(TinyFarmSpatialWorldAdapter.ActorPoint(start), displacement);

        Assert.Equal(displacement, validated.AcceptedDisplacement);
        Assert.Empty(validated.Contacts);
    }

    [Fact]
    public void SpatialAttackCandidateFeedsExistingAuthoritativeAttackIntent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        ActorSceneState player = state.ActorScene(TinyFarmIds.Player);
        ScenePosition enemy = definitions.Enemy(TinyFarmIds.DungeonSlime).SpawnPosition;
        var candidate = new SpatialCollider2D(
            new SpatialColliderId("enemy:dungeon.slime-1"),
            new Circle2(new SpatialPoint2D(enemy.XUnits, enemy.YUnits), 256),
            SemanticOwnerId: TinyFarmIds.DungeonSlime.Value);
        var queryWorld = new SpatialWorld2D();
        var attackRegion = new Aabb2(
            new SpatialPoint2D(player.WorldPosition.XUnits + 640, player.WorldPosition.YUnits),
            new SpatialVector2D(640, 640));

        SpatialOverlap2D spatialCandidate = Assert.Single(
            queryWorld.Overlap(attackRegion, transientColliders: [candidate]));
        Assert.Equal(TinyFarmIds.DungeonSlime.Value, spatialCandidate.SemanticOwnerId);

        var session = new TinyFarmSession(state, definitions);
        IntentResult result = session.Step(
            new AttackIntent(new EnemyId(spatialCandidate.SemanticOwnerId!)),
            evaluateNpcDecisions: false).Results.Single();
        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        Assert.Equal(GameEventKind.EnemyDefeated, Assert.Single(result.Events).Kind);
    }

    [Fact]
    public void PickupTriggerCandidateFeedsExistingAuthoritativeTakeIntent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);
        ItemState groundItem = state.Item(TinyFarmIds.WildMint);
        ScenePosition position = groundItem.GroundPosition!.Value;
        int playerIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[playerIndex] = state.MutableActorScenes[playerIndex] with
        {
            Scene = groundItem.GroundScene!.Value,
            WorldPosition = position
        };
        var triggerWorld = new SpatialWorld2D(
            triggers:
            [
                new SpatialTrigger2D(
                    new SpatialTriggerId($"pickup:{groundItem.Id.Value}"),
                    new Circle2(new SpatialPoint2D(position.XUnits, position.YUnits), 256),
                    SemanticOwnerId: groundItem.Id.Value)
            ]);

        SpatialTriggerOverlap2D candidate = Assert.Single(triggerWorld.OverlapTriggers(
            new Circle2(new SpatialPoint2D(position.XUnits, position.YUnits), 128)));
        var session = new TinyFarmSession(state, definitions);
        IntentResult result = session.Step(
            new TakeIntent(new ItemId(candidate.SemanticOwnerId!)),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        Assert.Equal(TinyFarmIds.Player, session.State.Item(groundItem.Id).Owner);
    }

    private static void AssertParity(
        TinyFarmDefinitions definitions,
        SceneDefinition scene,
        ScenePosition start,
        int deltaX,
        int deltaY,
        int distance)
    {
        ScenePosition target = new(
            start.XUnits + (deltaX * distance),
            start.YUnits + (deltaY * distance));
        bool legacyAccepted = TinyFarmScenes.IsInBounds(scene, target)
            && !TinyFarmScenes.IsBlocked(scene, target);

        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = state.MutableActorScenes[placementIndex] with
        {
            Scene = scene.Id,
            WorldPosition = start
        };
        var resolver = new TinyFarmResolver(definitions);
        ResolutionBatchResult result = resolver.Resolve(
            state,
            [new IntentEnvelope(
                TinyFarmIds.Player,
                new SpatialMoveIntent(deltaX, deltaY, distance),
                state.Minute,
                1,
                IntentSourceKind.Human)]);

        bool spatialAccepted = result.Results.Single().Status == IntentResultStatus.Accepted;
        Assert.Equal(legacyAccepted, spatialAccepted);
        Assert.Equal(
            legacyAccepted ? target : start,
            result.State.ActorScene(TinyFarmIds.Player).WorldPosition);
    }
}
