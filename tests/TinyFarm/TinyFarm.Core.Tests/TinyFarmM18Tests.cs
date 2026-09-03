using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM18Tests
{
    [Fact]
    public void ProductAndAuthoredForageDefinition_AreExact()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();

        ItemDefinition product = definitions.Item(TinyFarmIds.HenOfTheWoods);
        ForageNodeDefinition node = definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods);

        Assert.Equal("hen-of-the-woods", product.Id.Value);
        Assert.Equal("Hen-of-the-Woods", product.Name);
        Assert.Equal(3, product.SellPrice);
        Assert.Equal(TinyFarmSceneIds.Riverside, node.Scene);
        Assert.Equal(TinyFarmIds.HenOfTheWoods, node.Product);
        Assert.Equal(1, node.YieldCount);
        Assert.Equal(new ScenePosition(6656, 6656), node.Position);
    }

    [Fact]
    public void AvailableNode_IsTargetedWithExistingFacingRangeAndStableIdentityLaw()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        TinyFarmState state = TinyFarmM18ControlStates.Create(definitions);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.ForageNode, target.Kind);
        Assert.Equal(TinyFarmIds.RiversideHenOfTheWoods, target.ForageNode);
        Assert.Equal("object:riverside-hen-of-the-woods", target.StableId);
    }

    [Fact]
    public void GroundItem_HasPriorityOverForageNode()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        TinyFarmState state = TinyFarmM18ControlStates.Create(definitions);
        ForageNodeDefinition node = definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods);
        int itemIndex = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[itemIndex] = state.MutableItems[itemIndex] with
        {
            GroundLocation = TinyFarmIds.Riverside,
            GroundScene = TinyFarmSceneIds.Riverside,
            GroundPosition = node.Position,
            Owner = null
        };

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.GroundItem, target.Kind);
        Assert.Equal(TinyFarmIds.WildMint, target.Item);
    }

    [Fact]
    public void Interact_GathersStackableProductAndDepletesNodeAtomically()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        var session = new TinyFarmSession(TinyFarmM18ControlStates.Create(definitions), definitions);
        int selectedSlot = session.State.SelectedHotbarSlot;

        IntentResult result = session.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        GameEvent gathered = Assert.Single(result.Events);
        Assert.Equal(GameEventKind.ForageGathered, gathered.Kind);
        Assert.Equal(TinyFarmIds.RiversideHenOfTheWoods, gathered.ForageNode);
        Assert.Equal(TinyFarmIds.HenOfTheWoods, gathered.Product);
        Assert.Equal(1, gathered.Amount);
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods));
        Assert.Equal(
            ForageNodeAvailability.Depleted,
            session.State.ForageNode(TinyFarmIds.RiversideHenOfTheWoods).Availability);
        Assert.Equal(selectedSlot, session.State.SelectedHotbarSlot);
    }

    [Fact]
    public void SecondGather_IsRejectedWithoutCreatingAnotherProduct()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        var session = new TinyFarmSession(TinyFarmM18ControlStates.Create(definitions), definitions);
        session.Step(new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods), evaluateNpcDecisions: false);
        string afterFirst = TinyFarmSemanticHash.Compute(session.State);

        IntentResult second = session.Step(
            new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.AlreadyDepleted, second.Reason);
        Assert.Equal(afterFirst, TinyFarmSemanticHash.Compute(session.State));
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods));
    }

    [Fact]
    public void InvalidGatherVariants_RejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        TinyFarmState initial = TinyFarmM18ControlStates.Create(definitions);

        AssertRejectedWithoutMutation(
            initial,
            definitions,
            new IntentEnvelope(
                TinyFarmIds.Player,
                new GatherIntent(new ForageNodeId("unknown")),
                initial.Minute,
                0,
                IntentSourceKind.Human),
            IntentReason.UnknownForageNode);

        TinyFarmState wrongScene = initial.DeepCopy();
        PlacePlayer(wrongScene, TinyFarmSceneIds.Farm, new ScenePosition(1664, 1664), ActorFacing.Right);
        AssertRejectedWithoutMutation(
            wrongScene,
            definitions,
            Envelope(wrongScene, new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods)),
            IntentReason.ForageWrongScene);

        TinyFarmState outOfRange = initial.DeepCopy();
        PlacePlayer(outOfRange, TinyFarmSceneIds.Riverside, new ScenePosition(15 * 256, 9 * 256), ActorFacing.Left);
        AssertRejectedWithoutMutation(
            outOfRange,
            definitions,
            Envelope(outOfRange, new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods)),
            IntentReason.ForageOutOfRange);

        AssertRejectedWithoutMutation(
            initial,
            definitions,
            new IntentEnvelope(
                new ActorId("unknown"),
                new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods),
                initial.Minute,
                0,
                IntentSourceKind.Human),
            IntentReason.UnknownActor);
    }

    [Fact]
    public void DepletedNode_DisappearsFromTargetFrameAndHintWhileInventoryReprojects()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        var session = new TinyFarmSession(TinyFarmM18ControlStates.Create(definitions), definitions);
        TinyFarmPlayerUiView before = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        Assert.Equal("Gather Hen-of-the-Woods [Interact]", before.InteractionHint);

        session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView after = TinyFarmPlayerUiProjector.Project(session.State, definitions);

        Assert.DoesNotContain(frame.SceneObjects!, sceneObject => sceneObject.Kind == SceneObjectKind.Forage);
        Assert.DoesNotContain("Gather", after.InteractionHint, StringComparison.Ordinal);
        TinyFarmPlayerInventoryView inventory = Assert.Single(
            after.Inventory,
            item => item.SemanticId == TinyFarmIds.HenOfTheWoods.Value);
        Assert.Equal(1, inventory.Count);
    }

    [Fact]
    public void SaveLoadAndSimulationDto_PreserveAndExposeForageTruth()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        var session = new TinyFarmSession(TinyFarmM18ControlStates.Create(definitions), definitions);
        session.Step(new InteractIntent(), evaluateNpcDecisions: false);

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        var host = new TinyFarmSimulationHost(loaded, definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        string tson = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot);

        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal("tiny-farm-simulation@4", snapshot.Version);
        Assert.Contains(
            snapshot.ForageNodes!,
            node => node.StartsWith(
                "riverside-hen-of-the-woods:riverside:hen-of-the-woods:Depleted:",
                StringComparison.Ordinal));
        Assert.Contains("forageSummary", tson, StringComparison.Ordinal);
        Assert.Equal(1, loaded.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods));
    }

    [Fact]
    public void CliAndLlmSurface_UseSemanticGatherIntent()
    {
        GatherIntent gather = Assert.IsType<GatherIntent>(
            TinyFarmCommandParser.Parse("gather riverside-hen-of-the-woods"));

        Assert.Equal(TinyFarmIds.RiversideHenOfTheWoods, gather.Node);
        Assert.IsType<InteractIntent>(TinyFarmCommandParser.Parse("interact"));
    }

    [Fact]
    public void ForageSemanticTypes_RemainRendererIndependent()
    {
        Type[] semanticTypes =
        [
            typeof(ForageNodeDefinition),
            typeof(ForageNodeState),
            typeof(GatherIntent)
        ];

        string[] assemblyNames = semanticTypes
            .SelectMany(type => type.Assembly.GetReferencedAssemblies())
            .Select(name => name.Name ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(assemblyNames, name =>
            name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xna", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalScenario_ProducesOutcomeAAndRequiredHashes()
    {
        TinyFarmM18Evidence evidence = TinyFarmM18Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM18Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
        Assert.True(proof.RootElement.GetProperty("saveLoadExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("replayExact").GetBoolean());
        Assert.Equal(9, proof.RootElement.GetProperty("hashes").EnumerateObject().Count());
    }

    private static IntentEnvelope Envelope(TinyFarmState state, GameIntent intent)
    {
        return new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, 0, IntentSourceKind.Human);
    }

    private static void AssertRejectedWithoutMutation(
        TinyFarmState initial,
        TinyFarmDefinitions definitions,
        IntentEnvelope envelope,
        IntentReason expectedReason)
    {
        string before = TinyFarmSemanticHash.Compute(initial);
        ResolutionBatchResult batch = new TinyFarmResolver(definitions).Resolve(initial, [envelope]);

        Assert.Equal(expectedReason, batch.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(batch.State));
    }

    private static void PlacePlayer(
        TinyFarmState state,
        SceneId scene,
        ScenePosition position,
        ActorFacing facing)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placementIndex] = state.MutableActorScenes[placementIndex] with
        {
            Scene = scene,
            WorldPosition = position,
            Facing = facing
        };
    }
}
