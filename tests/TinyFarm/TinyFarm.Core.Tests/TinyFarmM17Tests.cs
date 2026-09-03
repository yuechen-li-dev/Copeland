using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM17Tests
{
    [Fact]
    public void GroundItemTargeting_IsFacingRangedAndUsesDocumentedPriority()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.GroundItem, target.Kind);
        Assert.Equal(TinyFarmIds.WildMint, target.Item);
        Assert.Equal("item:wild-mint", target.StableId);
    }

    [Fact]
    public void InteractPickup_MutatesAuthoritativeItemAndInventoryAndReprojectsWorld()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        Assert.Single(TinyFarmFrameProjector.Project(session.State, definitions).GroundItems);

        IntentResult result = session.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        Assert.Equal(GameEventKind.ItemTaken, Assert.Single(result.Events).Kind);
        ItemState item = session.State.Item(TinyFarmIds.WildMint);
        Assert.Equal(TinyFarmIds.Player, item.Owner);
        Assert.Null(item.GroundLocation);
        Assert.Null(item.GroundScene);
        Assert.Null(item.GroundPosition);
        Assert.Contains(TinyFarmIds.WildMint, session.State.Actor(TinyFarmIds.Player).Inventory);
        Assert.Empty(TinyFarmFrameProjector.Project(session.State, definitions).GroundItems);
        Assert.Contains(
            TinyFarmPlayerUiProjector.Project(session.State, definitions).Inventory,
            row => row.SemanticId == TinyFarmIds.WildMint.Value);
    }

    [Fact]
    public void DirectPickup_OutOfRangeRejectsWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);
        ReplaceWildMint(state, new ScenePosition(15 * ScenePosition.UnitsPerTile, 5 * ScenePosition.UnitsPerTile));
        var session = new TinyFarmSession(state, definitions);
        string before = TinyFarmSemanticHash.Compute(session.State);

        IntentResult result = session.Step(
            new TakeIntent(TinyFarmIds.WildMint),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.ItemOutOfRange, result.Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(session.State));
    }

    [Fact]
    public void DuplicatePickup_FirstWinsAndSecondRejectsWithoutFurtherMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        Assert.Equal(
            IntentResultStatus.Accepted,
            session.Step(new TakeIntent(TinyFarmIds.WildMint), evaluateNpcDecisions: false).Results.Single().Status);
        string afterFirst = TinyFarmSemanticHash.Compute(session.State);

        IntentResult second = session.Step(
            new TakeIntent(TinyFarmIds.WildMint),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.ItemNotGround, second.Reason);
        Assert.Equal(afterFirst, TinyFarmSemanticHash.Compute(session.State));
    }

    [Theory]
    [InlineData(8, IntentReason.NoSelectedBinding)]
    [InlineData(2, IntentReason.UnsupportedSelectedUse)]
    public void UseSelected_RejectsEmptyAndUnsupportedBindings(int slot, IntentReason reason)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);
        state.SelectedHotbarSlot = slot;
        var session = new TinyFarmSession(state, definitions);

        IntentResult result = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void UseSelected_RejectsUnavailableBindingWithoutClearingSelection()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);
        state.MutableInventoryStacks.RemoveAll(stack => stack.Product == TinyFarmIds.TurnipSeed);
        var session = new TinyFarmSession(state, definitions);

        IntentResult result = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.SelectedBindingUnavailable, result.Reason);
        Assert.Equal(1, session.State.SelectedHotbarSlot);
        Assert.Equal(0, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed));
    }

    [Fact]
    public void UseSelectedTurnipSeed_IsSemanticallyEquivalentToDirectPlantIntent()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = StateAfterPickup(definitions);
        var selected = new TinyFarmSession(state, definitions);
        var direct = new TinyFarmSession(state, definitions);

        IntentResult selectedResult = selected.Step(
            new UseSelectedIntent(),
            evaluateNpcDecisions: false).Results.Single();
        IntentResult directResult = direct.Step(
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(directResult.Status, selectedResult.Status);
        Assert.Equal(directResult.Reason, selectedResult.Reason);
        Assert.Equal(directResult.Events, selectedResult.Events);
        Assert.Equal(TinyFarmSemanticHash.Compute(direct.State), TinyFarmSemanticHash.Compute(selected.State));
        Assert.Equal(2, selected.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed));
        Assert.Equal(TinyFarmIds.TurnipCrop, selected.State.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop);
    }

    [Fact]
    public void InteractDoesNotPlantAnEmptyPlotInM17()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var session = new TinyFarmSession(StateAfterPickup(definitions), definitions);

        IntentResult result = session.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.NoInteraction, result.Reason);
        Assert.Null(session.State.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop);
        Assert.Equal(3, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed));
    }

    [Fact]
    public void OccupiedPlotFailure_PreservesDirectPlantReasonAndSeedCount()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = StateAfterPickup(definitions);
        state.MutableFarmPlots[0] = state.MutableFarmPlots[0] with { Crop = TinyFarmIds.TurnipCrop, PlantedDay = state.Day };
        var selected = new TinyFarmSession(state, definitions);
        var direct = new TinyFarmSession(state, definitions);

        IntentResult selectedResult = selected.Step(new UseSelectedIntent(), evaluateNpcDecisions: false).Results.Single();
        IntentResult directResult = direct.Step(
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.PlotOccupied, selectedResult.Reason);
        Assert.Equal(directResult.Reason, selectedResult.Reason);
        Assert.Equal(3, selected.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed));
    }

    [Fact]
    public void InventoryFocusSuppressesUseSelected()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = StateAfterPickup(definitions);
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        var controller = new TinyFarmPlayerUiController(host);
        controller.HandleKey(TinyFarmUiKey.Inventory);
        string before = TinyFarmSemanticHash.Compute(host.Session.State);

        controller.HandleKey(TinyFarmUiKey.UseSelected);

        Assert.Equal(before, TinyFarmSemanticHash.Compute(host.Session.State));
        Assert.Null(host.Session.State.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop);
    }

    [Fact]
    public void CliAndLlmCommandsAreSemanticIntents()
    {
        Assert.IsType<InteractIntent>(TinyFarmCommandParser.Parse("pickup"));
        Assert.IsType<InteractIntent>(TinyFarmCommandParser.Parse("take"));
        Assert.IsType<UseSelectedIntent>(TinyFarmCommandParser.Parse("use-selected"));
    }

    [Fact]
    public void PickupSelectionAndPlantingPersistExactlyAndSnapshotExposesLoopState()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(1)), evaluateNpcDecisions: false);
        session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false);

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        var host = new TinyFarmSimulationHost(loaded, definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        string tson = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot);

        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal("tiny-farm-simulation@3", snapshot.Version);
        Assert.Empty(snapshot.GroundItems!);
        Assert.Contains("plot-1:turnip:0", snapshot.Plots!);
        Assert.Contains("groundItemSummary", tson, StringComparison.Ordinal);
        Assert.Contains("plotSummary", tson, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayOrigin_UsesTheSamePickupSelectionAndUseResolverPath()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState initial = TinyFarmM17ControlStates.Create(definitions);
        var resolver = new TinyFarmResolver(definitions);
        GameIntent[] intents =
        [
            new InteractIntent(),
            new SelectHotbarSlotIntent(new HotbarSlotId(1)),
            new UseSelectedIntent()
        ];

        ResolutionBatchResult human = resolver.Resolve(
            initial,
            CreateEnvelopes(initial, intents, IntentSourceKind.Human));
        ResolutionBatchResult replay = resolver.Resolve(
            initial,
            CreateEnvelopes(initial, intents, IntentSourceKind.Replay));

        Assert.Equal(TinyFarmSemanticHash.Compute(human.State), TinyFarmSemanticHash.Compute(replay.State));
        Assert.Equal(
            human.Results.SelectMany(result => result.Events),
            replay.Results.SelectMany(result => result.Events));
        Assert.All(replay.Results, result => Assert.Equal(IntentResultStatus.Accepted, result.Status));
    }

    [Fact]
    public void CanonicalScenario_ProducesOutcomeAAndFiveCompactArtifacts()
    {
        TinyFarmM17Evidence evidence = TinyFarmM17Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM17Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
        Assert.True(proof.RootElement.GetProperty("plantParity").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("headlessRepeatExact").GetBoolean());
    }

    private static TinyFarmState StateAfterPickup(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        return session.State.DeepCopy();
    }

    private static void ReplaceWildMint(TinyFarmState state, ScenePosition position)
    {
        int index = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[index] = state.MutableItems[index] with { GroundPosition = position };
    }

    private static IReadOnlyList<IntentEnvelope> CreateEnvelopes(
        TinyFarmState state,
        IReadOnlyList<GameIntent> intents,
        IntentSourceKind source)
    {
        return intents
            .Select((intent, index) => new IntentEnvelope(
                TinyFarmIds.Player,
                intent,
                state.Minute,
                index,
                source))
            .ToArray();
    }
}
