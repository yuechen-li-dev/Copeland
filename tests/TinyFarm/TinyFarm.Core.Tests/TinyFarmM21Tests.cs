using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM21Tests
{
    [Fact]
    public void DungeonSwordAndSlime_AreExactBoundedDefinitions()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        SceneDefinition dungeon = definitions.Scenes.Get(TinyFarmSceneIds.DungeonEntrance);
        EnemyDefinition slime = Assert.Single(definitions.Enemies);
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);

        Assert.Equal("Old Burrow", dungeon.Name);
        Assert.Equal(16, dungeon.Width);
        Assert.Equal(12, dungeon.Height);
        Assert.Equal(TinyFarmIds.DungeonSlime, slime.Id);
        Assert.Equal(EnemyKind.Slime, slime.Kind);
        Assert.Equal(TinyFarmSceneIds.DungeonEntrance, slime.Scene);
        Assert.Equal(1, slime.MaxHealth);
        Assert.Equal(1, state.Enemy(slime.Id).CurrentHealth);
        Assert.Equal(EnemyLifecycle.Alive, state.Enemy(slime.Id).Lifecycle);
        Assert.Equal("Sword", state.Item(TinyFarmIds.Sword).Name);
        Assert.Equal(TinyFarmIds.Player, state.Item(TinyFarmIds.Sword).Owner);
        Assert.Contains(TinyFarmIds.Sword, state.Actor(TinyFarmIds.Player).Inventory);
        Assert.Equal(SceneObjectKind.Enemy, dungeon.Object(new SceneObjectId(slime.Id.Value)).Kind);
        Assert.False(dungeon.Object(new SceneObjectId(slime.Id.Value)).BlocksMovement);
    }

    [Fact]
    public void DungeonUsesAuthoredRouteGraphAndValidatedGeometry()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        SceneDefinition overworld = definitions.Scenes.Get(TinyFarmSceneIds.Overworld);
        SceneDefinition dungeon = definitions.Scenes.Get(TinyFarmSceneIds.DungeonEntrance);
        SceneRoute enter = Assert.Single(overworld.Routes, route => route.Id == new SceneRouteId("overworld-dungeon"));
        SceneRoute leave = Assert.Single(dungeon.Routes, route => route.Id == new SceneRouteId("dungeon-overworld"));

        Assert.Equal(TinyFarmSceneIds.DungeonEntrance, enter.TargetScene);
        Assert.Equal(TinyFarmAnchorIds.DungeonEntrance, enter.TargetAnchor);
        Assert.Equal(TinyFarmSceneIds.Overworld, leave.TargetScene);
        Assert.Equal(new SceneAnchorId("overworld.from-dungeon"), leave.TargetAnchor);
        Assert.All(dungeon.Layout, row =>
        {
            Assert.InRange(row.X, 0, dungeon.Width - 1);
            Assert.InRange(row.Y, 0, dungeon.Height - 1);
            Assert.True(row.X + row.Width <= dungeon.Width);
            Assert.True(row.Y + row.Height <= dungeon.Height);
        });
        Assert.False(TinyFarmScenes.IsBlocked(dungeon, definitions.Enemy(TinyFarmIds.DungeonSlime).SpawnPosition));
    }

    [Fact]
    public void SwordUsesSlotFourAndOlderM20StateStillProjectsItEmpty()
    {
        Assert.Equal(
            new ItemHotbarBinding(TinyFarmIds.Sword),
            TinyFarmHotbar.DefaultSlots[3].Binding);
        TinyFarmDefinitions m21 = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmHotbarSlotView sword = TinyFarmPlayerUiProjector.Project(
            TinyFarmM21ControlStates.Create(m21),
            m21).Hotbar[3];
        Assert.Equal("Sword", sword.Label);
        Assert.Equal(1, sword.Count);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Available, sword.VisualState);

        TinyFarmDefinitions m20 = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmHotbarSlotView legacySlot = TinyFarmPlayerUiProjector.Project(
            TinyFarmM20ControlStates.Create(m20),
            m20).Hotbar[3];
        Assert.Null(legacySlot.BindingKind);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Empty, legacySlot.VisualState);
    }

    [Fact]
    public void AliveEnemyUsesSharedFacingRangeAndPriorityAfterFriendlyActor()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.Enemy, target.Kind);
        Assert.Equal(TinyFarmIds.DungeonSlime, target.Enemy);
        Assert.Equal("object:dungeon.slime-1", target.StableId);
        Assert.Equal(1024L * 1024L, target.SquaredDistance);

        int maraPlacement = state.MutableActorScenes.FindIndex(placement => placement.Actor == TinyFarmIds.Mara);
        state.MutableActorScenes[maraPlacement] = state.MutableActorScenes[maraPlacement] with
        {
            Scene = TinyFarmSceneIds.DungeonEntrance,
            WorldPosition = definitions.Enemy(TinyFarmIds.DungeonSlime).SpawnPosition
        };
        InteractionTarget friendlyFirst = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));
        Assert.Equal(InteractionTargetKind.Actor, friendlyFirst.Kind);
        Assert.Equal(TinyFarmIds.Mara, friendlyFirst.Actor);
    }

    [Fact]
    public void WrongEmptyAndUnavailableSelectedBindingsRejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        AssertRejected(initial, definitions, new UseSelectedIntent(), IntentReason.WrongWeapon);

        TinyFarmState wrongProduct = initial.DeepCopy();
        wrongProduct.SelectedHotbarSlot = 1;
        AssertRejected(wrongProduct, definitions, new UseSelectedIntent(), IntentReason.WrongTool);

        TinyFarmState empty = initial.DeepCopy();
        empty.SelectedHotbarSlot = 8;
        AssertRejected(empty, definitions, new UseSelectedIntent(), IntentReason.NoSelectedBinding);

        TinyFarmState unavailable = initial.DeepCopy();
        unavailable.SelectedHotbarSlot = 4;
        RemoveSwordFromPlayer(unavailable);
        AssertRejected(unavailable, definitions, new UseSelectedIntent(), IntentReason.SelectedBindingUnavailable);
    }

    [Fact]
    public void UseSelectedSwordAndDirectAttackHaveExactSemanticParity()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        state.SelectedHotbarSlot = 4;
        var selected = new TinyFarmSession(state, definitions);
        var direct = new TinyFarmSession(state, definitions);

        IntentResult selectedResult = selected.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        IntentResult directResult = direct.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(directResult.Status, selectedResult.Status);
        Assert.Equal(directResult.Reason, selectedResult.Reason);
        Assert.Equal(directResult.Events, selectedResult.Events);
        Assert.Equal(TinyFarmSemanticHash.Compute(direct.State), TinyFarmSemanticHash.Compute(selected.State));
    }

    [Fact]
    public void AttackAtomicallyDefeatsSlimeAndEmitsOneExactEvent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);

        IntentResult result = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        GameEvent defeated = Assert.Single(result.Events);
        Assert.Equal(GameEventKind.EnemyDefeated, defeated.Kind);
        Assert.Equal(TinyFarmIds.Player, defeated.Actor);
        Assert.Equal(TinyFarmIds.DungeonSlime, defeated.Enemy);
        Assert.Equal(EnemyKind.Slime, defeated.EnemyKind);
        Assert.Equal(TinyFarmSceneIds.DungeonEntrance, defeated.Scene);
        Assert.Equal(1, defeated.Amount);
        Assert.Equal(0, session.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        Assert.Equal(EnemyLifecycle.Defeated, session.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle);
        Assert.Contains(TinyFarmIds.Sword, session.State.Actor(TinyFarmIds.Player).Inventory);
    }

    [Fact]
    public void SecondAttackRejectsAndDefeatedEnemyIsNotTargetable()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        session.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);
        string afterFirst = TinyFarmSemanticHash.Compute(session.State);

        IntentResult second = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.AlreadyDefeated, second.Reason);
        Assert.Empty(second.Events);
        Assert.Equal(afterFirst, TinyFarmSemanticHash.Compute(session.State));
        Assert.Null(TinyFarmSpatialQueries.SelectInteractionTarget(
            session.State,
            TinyFarmIds.Player,
            definitions.Scenes));
    }

    [Fact]
    public void DirectAttackValidatesSwordSceneRangeEnemyAndPlayerAuthority()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        AssertRejected(initial, definitions, new AttackIntent(new EnemyId("unknown")), IntentReason.UnknownEnemy);

        TinyFarmState wrongScene = initial.DeepCopy();
        PlacePlayer(wrongScene, TinyFarmSceneIds.Farm, new GridPosition(6, 6), ActorFacing.Right);
        AssertRejected(wrongScene, definitions, new AttackIntent(TinyFarmIds.DungeonSlime), IntentReason.EnemyWrongScene);

        TinyFarmState outOfRange = initial.DeepCopy();
        PlacePlayer(outOfRange, TinyFarmSceneIds.DungeonEntrance, new GridPosition(2, 2), ActorFacing.Right);
        AssertRejected(outOfRange, definitions, new AttackIntent(TinyFarmIds.DungeonSlime), IntentReason.EnemyOutOfRange);

        TinyFarmState missingSword = initial.DeepCopy();
        RemoveSwordFromPlayer(missingSword);
        AssertRejected(missingSword, definitions, new AttackIntent(TinyFarmIds.DungeonSlime), IntentReason.MissingSword);

        string before = TinyFarmSemanticHash.Compute(initial);
        ResolutionBatchResult friendlyActor = new TinyFarmResolver(definitions).Resolve(initial,
        [
            new IntentEnvelope(
                TinyFarmIds.Mara,
                new AttackIntent(TinyFarmIds.DungeonSlime),
                initial.Minute,
                0,
                IntentSourceKind.Dominatus)
        ]);
        Assert.Equal(IntentReason.WrongTargetKind, friendlyActor.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(friendlyActor.State));

        Assert.Equal(typeof(EnemyId), typeof(AttackIntent).GetProperty(nameof(AttackIntent.Enemy))!.PropertyType);
    }

    [Fact]
    public void SaveLoadPreservesAliveAndDefeatedEnemyAndDungeonPlacement()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var alive = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        TinyFarmSession loadedAlive = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(alive, definitions),
            definitions);
        Assert.Equal(1, loadedAlive.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        Assert.Equal(TinyFarmSceneIds.DungeonEntrance, loadedAlive.State.CurrentScene);
        Assert.Contains(TinyFarmIds.Sword, loadedAlive.State.Actor(TinyFarmIds.Player).Inventory);

        alive.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);
        TinyFarmSession loadedDefeated = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(alive, definitions),
            definitions);
        Assert.Equal(0, loadedDefeated.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth);
        Assert.Equal(EnemyLifecycle.Defeated, loadedDefeated.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle);
        Assert.Equal(TinyFarmSemanticHash.Compute(alive.State), TinyFarmSemanticHash.Compute(loadedDefeated.State));
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void DungeonSlimeSwordAndUiProjectWithoutClipping(int width, int height)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        TinyFarmFrame before = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView beforeUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        Assert.Contains(before.SceneObjects!, item => item.Kind == SceneObjectKind.Enemy);
        TinyFarmEnemyView slime = Assert.Single(before.Enemies!);
        Assert.Equal(EnemyLifecycle.Alive, slime.Lifecycle);
        Assert.True(slime.IsInteractionTarget);
        Assert.Equal("Requires Sword", beforeUi.InteractionHint);
        Assert.Equal("Sword", beforeUi.Hotbar[3].Label);

        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(4)), evaluateNpcDecisions: false);
        Assert.Equal("Attack Slime [Use]", TinyFarmPlayerUiProjector.Project(session.State, definitions).InteractionHint);
        session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false);
        TinyFarmFrame after = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView afterUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        TinyFarmPlayerUiLayout layout = TinyFarmPlayerUiLayoutEngine.Compute(width, height, afterUi.Inventory.Count);

        Assert.DoesNotContain(after.SceneObjects!, item => item.Kind == SceneObjectKind.Enemy);
        Assert.Equal(EnemyLifecycle.Defeated, Assert.Single(after.Enemies!).Lifecycle);
        Assert.Equal(4, afterUi.SelectedSlot.Value);
        Assert.All(layout.HotbarSlots, rectangle =>
        {
            Assert.InRange(rectangle.X, 0, width - rectangle.Width);
            Assert.InRange(rectangle.Y, 0, height - rectangle.Height);
        });
        Assert.InRange(layout.InventoryPanel.X, 0, width - layout.InventoryPanel.Width);
        Assert.InRange(layout.InventoryPanel.Y, 0, height - layout.InventoryPanel.Height);
    }

    [Fact]
    public void CliLlmDtoReplayAndRendererBoundariesRemainSemantic()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        TinyFarmSimulationSnapshot snapshot = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing).Snapshot();

        Assert.Equal(TinyFarmIds.DungeonSlime, Assert.IsType<AttackIntent>(TinyFarmCommandParser.Parse("attack")).Enemy);
        Assert.Equal(new EnemyId("dungeon.slime-1"), Assert.IsType<AttackIntent>(
            TinyFarmCommandParser.Parse("attack dungeon.slime-1")).Enemy);
        Assert.Equal(4, Assert.IsType<SelectHotbarSlotIntent>(TinyFarmCommandParser.Parse("select Sword")).Slot.Value);
        Assert.Equal(TinyFarmAnchorIds.DungeonEntrance, Assert.IsType<NavigateToAnchorIntent>(
            TinyFarmCommandParser.Parse("go to dungeon")).Anchor);
        Assert.Equal(TinyFarmAnchorIds.DungeonSlimeApproach, Assert.IsType<NavigateToAnchorIntent>(
            TinyFarmCommandParser.Parse("approach Slime")).Anchor);
        Assert.Equal("tiny-farm-simulation@6", snapshot.Version);
        Assert.Contains("dungeon.slime-1:Slime:dungeon-entrance", Assert.Single(snapshot.Enemies!));
        Assert.Contains("enemySummary", TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot));

        Type[] semanticTypes =
        [
            typeof(EnemyId),
            typeof(EnemyDefinition),
            typeof(EnemyState),
            typeof(AttackIntent)
        ];
        string[] references = semanticTypes.SelectMany(type => type.Assembly.GetReferencedAssemblies())
            .Select(name => name.Name ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain(references, name =>
            name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xna", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dominatus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalScenarioProducesOutcomeAAndThirteenRequiredHashes()
    {
        TinyFarmM21Evidence evidence = TinyFarmM21Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM21Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
        Assert.True(proof.RootElement.GetProperty("saveLoadExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("replayExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("useSelectedParity").GetBoolean());
        Assert.Equal(13, proof.RootElement.GetProperty("hashes").EnumerateObject().Count());
    }

    private static void AssertRejected(
        TinyFarmState initial,
        TinyFarmDefinitions definitions,
        GameIntent intent,
        IntentReason reason)
    {
        string before = TinyFarmSemanticHash.Compute(initial);
        ResolutionBatchResult batch = new TinyFarmResolver(definitions).Resolve(initial,
        [
            new IntentEnvelope(TinyFarmIds.Player, intent, initial.Minute, 0, IntentSourceKind.Human)
        ]);
        Assert.Equal(reason, batch.Results.Single().Reason);
        Assert.Empty(batch.Results.Single().Events);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(batch.State));
    }

    private static void RemoveSwordFromPlayer(TinyFarmState state)
    {
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[actorIndex];
        state.MutableActors[actorIndex] = player with
        {
            Inventory = player.Inventory.Where(item => item != TinyFarmIds.Sword).ToList()
        };
    }

    private static void PlacePlayer(
        TinyFarmState state,
        SceneId scene,
        GridPosition position,
        ActorFacing facing)
    {
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with
        {
            Location = TinyFarmScenes.LocationForScene(scene)
        };
        int placementIndex = state.MutableActorScenes.FindIndex(placement => placement.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = state.MutableActorScenes[placementIndex] with
        {
            Scene = scene,
            WorldPosition = ScenePosition.FromGrid(position),
            Facing = facing
        };
    }
}
