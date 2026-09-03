using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM20Tests
{
    [Fact]
    public void AxeWoodAndAuthoredTree_AreExactConcreteDefinitions()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TreeDefinition tree = Assert.Single(definitions.Trees);
        ItemDefinition wood = definitions.Item(TinyFarmIds.Wood);
        TinyFarmState state = TinyFarmM20ControlStates.Create(definitions);

        Assert.Equal(new ItemId("axe"), TinyFarmIds.Axe);
        Assert.Equal("Axe", state.Item(TinyFarmIds.Axe).Name);
        Assert.Equal(TinyFarmIds.Player, state.Item(TinyFarmIds.Axe).Owner);
        Assert.Contains(TinyFarmIds.Axe, state.Actor(TinyFarmIds.Player).Inventory);
        Assert.Equal(new ProductId("wood"), wood.Id);
        Assert.Equal("Wood", wood.Name);
        Assert.Equal(0, wood.BuyPrice);
        Assert.Equal(2, wood.SellPrice);
        Assert.Equal(TinyFarmIds.FarmTree, tree.Id);
        Assert.Equal(TinyFarmSceneIds.Farm, tree.Scene);
        Assert.Equal(TinyFarmIds.Wood, tree.YieldProduct);
        Assert.Equal(1, tree.YieldCount);
        Assert.Equal(SceneObjectKind.Tree, definitions.Scenes.Get(tree.Scene).Object(new SceneObjectId(tree.Id.Value)).Kind);
    }

    [Fact]
    public void HotbarUsesClosedItemBindingAndPreservesProductBindings()
    {
        Assert.Equal(
            new ProductHotbarBinding(TinyFarmIds.TurnipSeed),
            TinyFarmHotbar.DefaultSlots[0].Binding);
        Assert.Equal(
            new ProductHotbarBinding(TinyFarmIds.Turnip),
            TinyFarmHotbar.DefaultSlots[1].Binding);
        Assert.Equal(
            new ItemHotbarBinding(TinyFarmIds.Axe),
            TinyFarmHotbar.DefaultSlots[2].Binding);

        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmPlayerUiView ui = TinyFarmPlayerUiProjector.Project(
            TinyFarmM20ControlStates.Create(definitions),
            definitions);
        TinyFarmHotbarSlotView axe = ui.Hotbar[2];
        Assert.Equal("Item", axe.BindingKind);
        Assert.Equal("axe", axe.SemanticId);
        Assert.Equal("Axe", axe.Label);
        Assert.Equal(1, axe.Count);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Available, axe.VisualState);
    }

    [Fact]
    public void TreeTargetUsesSharedRangeAndPriorityAfterForageBeforePlot()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState state = TinyFarmM20ControlStates.Create(definitions);
        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.Tree, target.Kind);
        Assert.Equal(TinyFarmIds.FarmTree, target.Tree);
        Assert.Equal("object:farm-tree", target.StableId);
        Assert.Equal(1024L * 1024L, target.SquaredDistance);

        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        SceneLayoutRow treePlacement = farm.Placement(new SceneObjectId(TinyFarmIds.FarmTree.Value));
        var overlappingPlot = new SceneObjectId("tree-priority-plot");
        var replacedFarm = new SceneDefinition(
            farm.Id,
            farm.Name,
            farm.Width,
            farm.Height,
            farm.Objects.Append(new SceneObjectDefinition(
                overlappingPlot,
                SceneObjectKind.Plot,
                "Priority Plot",
                BlocksMovement: false,
                SemanticReference: TinyFarmIds.PlotOne.Value)),
            farm.Layout.Append(treePlacement with { ObjectId = overlappingPlot }),
            farm.Anchors,
            farm.Routes);
        var scenes = new TinyFarmSceneCatalog(definitions.Scenes.All.Select(scene =>
            scene.Id == farm.Id ? replacedFarm : scene));
        var overlapDefinitions = new TinyFarmDefinitions(
            definitions.Identity,
            definitions.Items,
            definitions.Crops,
            scenes,
            definitions.SceneContent,
            definitions.Schedules,
            definitions.ScheduleContent,
            definitions.ForageNodes,
            definitions.CookingRecipes,
            definitions.Trees);

        InteractionTarget priorityTarget = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, overlapDefinitions.Scenes));
        Assert.Equal(InteractionTargetKind.Tree, priorityTarget.Kind);
    }

    [Fact]
    public void WrongEmptyAndUnavailableSelectedBindingsRejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState initial = TinyFarmM20ControlStates.Create(definitions);
        AssertRejected(initial, definitions, new UseSelectedIntent(), IntentReason.WrongTool);

        TinyFarmState empty = initial.DeepCopy();
        empty.SelectedHotbarSlot = 8;
        AssertRejected(empty, definitions, new UseSelectedIntent(), IntentReason.NoSelectedBinding);

        TinyFarmState unavailable = initial.DeepCopy();
        unavailable.SelectedHotbarSlot = 3;
        RemoveAxeFromPlayer(unavailable);
        AssertRejected(unavailable, definitions, new UseSelectedIntent(), IntentReason.SelectedBindingUnavailable);
    }

    [Fact]
    public void UseSelectedAxeAndDirectChopHaveExactSemanticParity()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState state = TinyFarmM20ControlStates.Create(definitions);
        state.SelectedHotbarSlot = 3;
        var selected = new TinyFarmSession(state, definitions);
        var direct = new TinyFarmSession(state, definitions);

        IntentResult selectedResult = selected.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        IntentResult directResult = direct.Step(new ChopIntent(TinyFarmIds.FarmTree), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(directResult.Status, selectedResult.Status);
        Assert.Equal(directResult.Reason, selectedResult.Reason);
        Assert.Equal(directResult.Events, selectedResult.Events);
        Assert.Equal(TinyFarmSemanticHash.Compute(direct.State), TinyFarmSemanticHash.Compute(selected.State));
    }

    [Fact]
    public void ChopAtomicallyAddsWoodDepletesTreeAndDoesNotConsumeAxe()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        var session = new TinyFarmSession(TinyFarmM20ControlStates.Create(definitions), definitions);

        IntentResult result = session.Step(
            new ChopIntent(TinyFarmIds.FarmTree),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        GameEvent chopped = Assert.Single(result.Events);
        Assert.Equal(GameEventKind.TreeChopped, chopped.Kind);
        Assert.Equal(TinyFarmIds.Player, chopped.Actor);
        Assert.Equal(TinyFarmIds.FarmTree, chopped.Tree);
        Assert.Equal(TinyFarmSceneIds.Farm, chopped.Scene);
        Assert.Equal(TinyFarmIds.Wood, chopped.Product);
        Assert.Equal(1, chopped.Amount);
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Wood));
        Assert.Equal(TreeAvailability.Depleted, session.State.Tree(TinyFarmIds.FarmTree).Availability);
        Assert.Contains(TinyFarmIds.Axe, session.State.Actor(TinyFarmIds.Player).Inventory);
        Assert.Equal(TinyFarmIds.Player, session.State.Item(TinyFarmIds.Axe).Owner);
    }

    [Fact]
    public void SecondChopRejectsAndDepletedTreeIsNotTargetable()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        var session = new TinyFarmSession(TinyFarmM20ControlStates.Create(definitions), definitions);
        session.Step(new ChopIntent(TinyFarmIds.FarmTree), evaluateNpcDecisions: false);
        string afterFirst = TinyFarmSemanticHash.Compute(session.State);

        IntentResult second = session.Step(
            new ChopIntent(TinyFarmIds.FarmTree),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.AlreadyDepleted, second.Reason);
        Assert.Empty(second.Events);
        Assert.Equal(afterFirst, TinyFarmSemanticHash.Compute(session.State));
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Wood));
        Assert.Null(TinyFarmSpatialQueries.SelectInteractionTarget(
            session.State,
            TinyFarmIds.Player,
            definitions.Scenes));
    }

    [Fact]
    public void InvalidChopVariantsRejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState initial = TinyFarmM20ControlStates.Create(definitions);
        AssertRejected(initial, definitions, new ChopIntent(new TreeId("unknown")), IntentReason.UnknownTree);

        TinyFarmState wrongScene = initial.DeepCopy();
        PlacePlayer(wrongScene, TinyFarmSceneIds.Riverside, new ScenePosition(6656, 6656), ActorFacing.Right);
        AssertRejected(wrongScene, definitions, new ChopIntent(TinyFarmIds.FarmTree), IntentReason.TreeWrongScene);

        TinyFarmState outOfRange = initial.DeepCopy();
        PlacePlayer(outOfRange, TinyFarmSceneIds.Farm, ScenePosition.FromGrid(new GridPosition(6, 6)), ActorFacing.Right);
        AssertRejected(outOfRange, definitions, new ChopIntent(TinyFarmIds.FarmTree), IntentReason.TreeOutOfRange);

        TinyFarmState missingAxe = initial.DeepCopy();
        RemoveAxeFromPlayer(missingAxe);
        AssertRejected(missingAxe, definitions, new ChopIntent(TinyFarmIds.FarmTree), IntentReason.MissingAxe);

        string before = TinyFarmSemanticHash.Compute(initial);
        ResolutionBatchResult invalidActor = new TinyFarmResolver(definitions).Resolve(initial,
        [
            new IntentEnvelope(
                new ActorId("unknown"),
                new ChopIntent(TinyFarmIds.FarmTree),
                initial.Minute,
                0,
                IntentSourceKind.Human)
        ]);
        Assert.Equal(IntentReason.UnknownActor, invalidActor.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(invalidActor.State));

        ResolutionBatchResult nonPlayer = new TinyFarmResolver(definitions).Resolve(initial,
        [
            new IntentEnvelope(
                TinyFarmIds.Elias,
                new ChopIntent(TinyFarmIds.FarmTree),
                initial.Minute,
                0,
                IntentSourceKind.Dominatus)
        ]);
        Assert.Equal(IntentReason.WrongTargetKind, nonPlayer.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(nonPlayer.State));
    }

    [Fact]
    public void SaveLoadReplayCliDtoAndM17PlantingRemainExact()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState initial = TinyFarmM20ControlStates.Create(definitions);
        initial.SelectedHotbarSlot = 3;
        var session = new TinyFarmSession(initial, definitions);
        session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        TinyFarmSimulationSnapshot snapshot = new TinyFarmSimulationHost(
            loaded,
            definitions,
            TinyFarmSimulationMode.Playing).Snapshot();

        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal("tiny-farm-simulation@5", snapshot.Version);
        Assert.Contains("farm-tree:farm:wood:Depleted:", Assert.Single(snapshot.Trees!));
        Assert.Contains("treeSummary", TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot));
        Assert.Equal(TinyFarmIds.FarmTree, Assert.IsType<ChopIntent>(TinyFarmCommandParser.Parse("chop")).Tree);
        Assert.IsType<UseSelectedIntent>(TinyFarmCommandParser.Parse("use-selected"));

        TinyFarmState plantState = TinyFarmM20ControlStates.Create(definitions);
        plantState.SelectedHotbarSlot = 1;
        PlacePlayer(plantState, TinyFarmSceneIds.Farm, ScenePosition.FromGrid(new GridPosition(8, 5)), ActorFacing.Left);
        var selectedPlant = new TinyFarmSession(plantState, definitions);
        var directPlant = new TinyFarmSession(plantState, definitions);
        IntentResult selectedResult = selectedPlant.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        IntentResult directResult = directPlant.Step(
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            evaluateNpcDecisions: false).Results.Single();
        Assert.Equal(directResult.Status, selectedResult.Status);
        Assert.Equal(directResult.Reason, selectedResult.Reason);
        Assert.Equal(directResult.Events, selectedResult.Events);
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void GraphicalProjectionShowsAxeTreeStumpAndWoodWithoutClipping(int width, int height)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        var session = new TinyFarmSession(TinyFarmM20ControlStates.Create(definitions), definitions);
        TinyFarmFrame before = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView beforeUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        Assert.Contains(before.SceneObjects!, item => item.Kind == SceneObjectKind.Tree && !item.Depleted);
        Assert.Equal("Requires Axe", beforeUi.InteractionHint);
        Assert.Equal("Axe", beforeUi.Hotbar[2].Label);

        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(3)), evaluateNpcDecisions: false);
        Assert.Equal("Chop Tree [Use]", TinyFarmPlayerUiProjector.Project(session.State, definitions).InteractionHint);
        session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false);
        TinyFarmFrame after = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView afterUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        TinyFarmPlayerUiLayout layout = TinyFarmPlayerUiLayoutEngine.Compute(width, height, afterUi.Inventory.Count);

        Assert.Contains(after.SceneObjects!, item => item.Kind == SceneObjectKind.Tree && item.Depleted);
        Assert.Contains(afterUi.Inventory, item => item.SemanticId == "wood" && item.Count == 1);
        Assert.Equal(3, afterUi.SelectedSlot.Value);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Available, afterUi.Hotbar[2].VisualState);
        Assert.All(layout.HotbarSlots, rectangle =>
        {
            Assert.InRange(rectangle.X, 0, width - rectangle.Width);
            Assert.InRange(rectangle.Y, 0, height - rectangle.Height);
        });
        Assert.InRange(layout.InventoryPanel.X, 0, width - layout.InventoryPanel.Width);
        Assert.InRange(layout.InventoryPanel.Y, 0, height - layout.InventoryPanel.Height);
    }

    [Fact]
    public void WoodcuttingSemanticTypesRemainRendererIndependent()
    {
        Type[] semanticTypes =
        [
            typeof(ItemHotbarBinding),
            typeof(TreeDefinition),
            typeof(TreeState),
            typeof(ChopIntent)
        ];
        string[] references = semanticTypes.SelectMany(type => type.Assembly.GetReferencedAssemblies())
            .Select(name => name.Name ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain(references, name =>
            name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xna", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GraphicalHostKeepsClockAndNpcLocomotionRunningWithTreePresent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM20();
        TinyFarmState state = TinyFarmM20ControlStates.Create(definitions);
        int eliasIndex = state.MutableActorScenes.FindIndex(placement => placement.Actor == TinyFarmIds.Elias);
        state.MutableActorScenes[eliasIndex] = state.MutableActorScenes[eliasIndex] with
        {
            WorldPosition = ScenePosition.FromGrid(new GridPosition(6, 7))
        };
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        int initialMinute = host.Session.State.Minute;
        ScenePosition initialElias = host.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;

        for (int second = 0; second < 10; second++)
        {
            host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(initialMinute + 2, host.Session.State.Minute);
        Assert.NotEqual(initialElias, host.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition);
        Assert.Contains(
            TinyFarmFrameProjector.Project(host.Session.State, definitions).SceneObjects!,
            item => item.Kind == SceneObjectKind.Tree);
    }

    [Fact]
    public void CanonicalScenarioProducesOutcomeAAndTwelveRequiredHashes()
    {
        TinyFarmM20Evidence evidence = TinyFarmM20Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM20Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
        Assert.True(proof.RootElement.GetProperty("saveLoadExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("replayExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("useSelectedParity").GetBoolean());
        Assert.Equal(12, proof.RootElement.GetProperty("hashes").EnumerateObject().Count());
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

    private static void RemoveAxeFromPlayer(TinyFarmState state)
    {
        int actorIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[actorIndex];
        state.MutableActors[actorIndex] = player with
        {
            Inventory = player.Inventory.Where(item => item != TinyFarmIds.Axe).ToList()
        };
    }

    private static void PlacePlayer(
        TinyFarmState state,
        SceneId scene,
        ScenePosition position,
        ActorFacing facing)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[index] = state.MutableActorScenes[index] with
        {
            Scene = scene,
            WorldPosition = position,
            Facing = facing
        };
    }
}
