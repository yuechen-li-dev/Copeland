using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM19Tests
{
    [Fact]
    public void AuthoredRecipeStationAndProduct_AreExact()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();

        CookingRecipeDefinition recipe = Assert.Single(definitions.CookingRecipes);
        Assert.Equal(TinyFarmIds.SauteedHenOfTheWoodsRecipe, recipe.Id);
        Assert.Equal(CookingStationKind.Cooking, recipe.StationKind);
        Assert.Equal(new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1), Assert.Single(recipe.Inputs));
        Assert.Equal(TinyFarmIds.SauteedHenOfTheWoods, recipe.OutputProduct);
        Assert.Equal(1, recipe.OutputCount);

        ItemDefinition product = definitions.Item(TinyFarmIds.SauteedHenOfTheWoods);
        Assert.Equal("Sautéed Hen-of-the-Woods", product.Name);
        Assert.Equal(0, product.BuyPrice);
        Assert.Equal(6, product.SellPrice);
        Assert.DoesNotContain(definitions.Items.Where(item => item.BuyPrice > 0), item => item.Id == product.Id);

        SceneDefinition residence = definitions.Scenes.Get(TinyFarmSceneIds.Residence);
        SceneObjectDefinition station = residence.Object(TinyFarmIds.HearthHouseKitchen);
        Assert.Equal(SceneObjectKind.CookingStation, station.Kind);
        Assert.Equal("Cooking", station.SemanticReference);
    }

    [Fact]
    public void RecipeValidation_RejectsDuplicateUnknownEmptyAndNonPositiveDefinitions()
    {
        TinyFarmDefinitions baseline = TinyFarmDefinitionLoader.LoadM19();
        CookingRecipeDefinition valid = Assert.Single(baseline.CookingRecipes);

        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline, [valid, valid]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with { Inputs = [new CookingRecipeInput(new ProductId("unknown"), 1)] }
        ]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with { OutputProduct = new ProductId("unknown") }
        ]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with { Inputs = [] }
        ]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with { Inputs = [new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 0)] }
        ]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with { OutputCount = 0 }
        ]));
        Assert.Throws<InvalidDataException>(() => WithRecipes(baseline,
        [
            valid with
            {
                Inputs =
                [
                    new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1),
                    new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1)
                ]
            }
        ]));
    }

    [Fact]
    public void RecipeInputSemanticsAreIndependentOfSourceOrder()
    {
        TinyFarmDefinitions baseline = TinyFarmDefinitionLoader.LoadM19();
        CookingRecipeDefinition recipe = Assert.Single(baseline.CookingRecipes) with
        {
            Inputs =
            [
                new CookingRecipeInput(TinyFarmIds.Turnip, 2),
                new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1)
            ]
        };

        TinyFarmDefinitions first = WithRecipes(baseline, [recipe]);
        TinyFarmDefinitions second = WithRecipes(baseline,
        [
            recipe with { Inputs = recipe.Inputs.Reverse().ToArray() }
        ]);

        Assert.Equal(first.CookingRecipes.Single().Inputs, second.CookingRecipes.Single().Inputs);
        Assert.Equal(
            [TinyFarmIds.HenOfTheWoods, TinyFarmIds.Turnip],
            first.CookingRecipes.Single().Inputs.Select(input => input.Product));
    }

    [Fact]
    public void StationUsesSharedFacingRangeAndStableTargetLaw()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        TinyFarmState state = TinyFarmM19ControlStates.Create(definitions);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.CookingStation, target.Kind);
        Assert.Equal(TinyFarmIds.HearthHouseKitchen, target.SceneObject);
        Assert.Equal("object:hearth-house-kitchen", target.StableId);
        Assert.Equal(1024L * 1024L, target.SquaredDistance);
    }

    [Fact]
    public void CookingStationHasDeterministicPriorityOverShop()
    {
        TinyFarmDefinitions baseline = TinyFarmDefinitionLoader.LoadM19();
        SceneDefinition residence = baseline.Scenes.Get(TinyFarmSceneIds.Residence);
        var shopId = new SceneObjectId("kitchen-shop-priority-test");
        SceneLayoutRow stationRow = residence.Placement(TinyFarmIds.HearthHouseKitchen);
        var replacedResidence = new SceneDefinition(
            residence.Id,
            residence.Name,
            residence.Width,
            residence.Height,
            residence.Objects.Append(new SceneObjectDefinition(
                shopId,
                SceneObjectKind.Shop,
                "Test Shop",
                BlocksMovement: false,
                SemanticReference: TinyFarmIds.GeneralStore.Value)),
            residence.Layout.Append(stationRow with { ObjectId = shopId }),
            residence.Anchors,
            residence.Routes);
        var scenes = new TinyFarmSceneCatalog(baseline.Scenes.All.Select(scene =>
            scene.Id == residence.Id ? replacedResidence : scene));
        var definitions = new TinyFarmDefinitions(
            baseline.Identity,
            baseline.Items,
            baseline.Crops,
            scenes,
            baseline.SceneContent,
            baseline.Schedules,
            baseline.ScheduleContent,
            baseline.ForageNodes,
            baseline.CookingRecipes);
        TinyFarmState state = TinyFarmM19ControlStates.Create(definitions);

        InteractionTarget target = Assert.IsType<InteractionTarget>(
            TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes));

        Assert.Equal(InteractionTargetKind.CookingStation, target.Kind);
        Assert.Equal(TinyFarmIds.HearthHouseKitchen, target.SceneObject);
    }

    [Fact]
    public void InteractCooksAtomicallyAndProjectionRereadsInventory()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        var session = new TinyFarmSession(TinyFarmM19ControlStates.Create(definitions), definitions);
        int selectedSlot = session.State.SelectedHotbarSlot;
        Assert.Equal(
            "Cook Sautéed Hen-of-the-Woods [Interact]",
            TinyFarmPlayerUiProjector.Project(session.State, definitions).InteractionHint);

        IntentResult result = session.Step(new InteractIntent(), evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentResultStatus.Accepted, result.Status);
        GameEvent cooked = Assert.Single(result.Events);
        Assert.Equal(GameEventKind.RecipeCooked, cooked.Kind);
        Assert.Equal(TinyFarmIds.HearthHouseKitchen, cooked.SceneObject);
        Assert.Equal(TinyFarmIds.SauteedHenOfTheWoodsRecipe, cooked.Recipe);
        Assert.Equal(TinyFarmIds.SauteedHenOfTheWoods, cooked.Product);
        Assert.Equal(1, cooked.Amount);
        Assert.Equal(0, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods));
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods));
        Assert.Equal(selectedSlot, session.State.SelectedHotbarSlot);
        TinyFarmPlayerUiView ui = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        Assert.DoesNotContain(ui.Inventory, item => item.SemanticId == TinyFarmIds.HenOfTheWoods.Value);
        Assert.Contains(ui.Inventory, item =>
            item.SemanticId == TinyFarmIds.SauteedHenOfTheWoods.Value && item.Count == 1);
        Assert.Equal("Need Hen-of-the-Woods x1", ui.InteractionHint);
    }

    [Fact]
    public void SecondCookRejectsWithoutConsumptionOrDuplication()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        var session = new TinyFarmSession(TinyFarmM19ControlStates.Create(definitions), definitions);
        session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        string afterFirst = TinyFarmSemanticHash.Compute(session.State);

        IntentResult second = session.Step(
            new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            evaluateNpcDecisions: false).Results.Single();

        Assert.Equal(IntentReason.MissingIngredient, second.Reason);
        Assert.Empty(second.Events);
        Assert.Equal(afterFirst, TinyFarmSemanticHash.Compute(session.State));
        Assert.Equal(0, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods));
        Assert.Equal(1, session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods));
    }

    [Fact]
    public void InvalidCookVariantsRejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        TinyFarmState initial = TinyFarmM19ControlStates.Create(definitions);
        AssertRejected(initial, definitions,
            new CookIntent(TinyFarmIds.HearthHouseKitchen, new CookingRecipeId("unknown")),
            IntentReason.UnknownRecipe);
        AssertRejected(initial, definitions,
            new CookIntent(new SceneObjectId("elias-bed"), TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            IntentReason.WrongStation);

        TinyFarmState wrongScene = initial.DeepCopy();
        PlacePlayer(wrongScene, TinyFarmSceneIds.Farm, ScenePosition.FromGrid(new GridPosition(5, 4)), ActorFacing.Right);
        AssertRejected(wrongScene, definitions,
            new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            IntentReason.StationWrongScene);

        TinyFarmState outOfRange = initial.DeepCopy();
        PlacePlayer(outOfRange, TinyFarmSceneIds.Residence, ScenePosition.FromGrid(new GridPosition(2, 4)), ActorFacing.Right);
        AssertRejected(outOfRange, definitions,
            new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
            IntentReason.StationOutOfRange);

        string before = TinyFarmSemanticHash.Compute(initial);
        ResolutionBatchResult invalidActor = new TinyFarmResolver(definitions).Resolve(initial,
        [
            new IntentEnvelope(
                new ActorId("unknown"),
                new CookIntent(TinyFarmIds.HearthHouseKitchen, TinyFarmIds.SauteedHenOfTheWoodsRecipe),
                initial.Minute,
                0,
                IntentSourceKind.Human)
        ]);
        Assert.Equal(IntentReason.UnknownActor, invalidActor.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(invalidActor.State));
    }

    [Fact]
    public void SaveLoadReplayCliAndRendererBoundaryRemainExact()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        var session = new TinyFarmSession(TinyFarmM19ControlStates.Create(definitions), definitions);
        session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(loaded.State));

        CookIntent cook = Assert.IsType<CookIntent>(TinyFarmCommandParser.Parse("cook sauteed-hen-of-the-woods"));
        Assert.Equal(TinyFarmIds.SauteedHenOfTheWoodsRecipe, cook.Recipe);
        Assert.Equal(TinyFarmIds.HearthHouseKitchen, cook.Station);

        Type[] semanticTypes = [typeof(CookingRecipeDefinition), typeof(CookIntent)];
        string[] references = semanticTypes.SelectMany(type => type.Assembly.GetReferencedAssemblies())
            .Select(name => name.Name ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain(references, name =>
            name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xna", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void GraphicalProjectionShowsKitchenWithoutUiClipping(int width, int height)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM19();
        TinyFarmState state = TinyFarmM19ControlStates.Create(definitions);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);
        TinyFarmPlayerUiView ui = TinyFarmPlayerUiProjector.Project(state, definitions);
        TinyFarmPlayerUiLayout layout = TinyFarmPlayerUiLayoutEngine.Compute(width, height, ui.Inventory.Count);

        Assert.Equal(TinyFarmSceneIds.Residence, frame.ActiveScene);
        Assert.Contains(frame.SceneObjects!, sceneObject =>
            sceneObject.Id == TinyFarmIds.HearthHouseKitchen
            && sceneObject.Kind == SceneObjectKind.CookingStation);
        Assert.Equal("Cook Sautéed Hen-of-the-Woods [Interact]", ui.InteractionHint);
        Assert.All(layout.HotbarSlots, rectangle =>
        {
            Assert.InRange(rectangle.X, 0, width - rectangle.Width);
            Assert.InRange(rectangle.Y, 0, height - rectangle.Height);
        });
        Assert.InRange(layout.InventoryPanel.X, 0, width - layout.InventoryPanel.Width);
        Assert.InRange(layout.InventoryPanel.Y, 0, height - layout.InventoryPanel.Height);
    }

    [Fact]
    public void CanonicalScenarioProducesOutcomeAAndTenRequiredHashes()
    {
        TinyFarmM19Evidence evidence = TinyFarmM19Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM19Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
        Assert.True(proof.RootElement.GetProperty("saveLoadExact").GetBoolean());
        Assert.True(proof.RootElement.GetProperty("replayExact").GetBoolean());
        Assert.Equal(10, proof.RootElement.GetProperty("hashes").EnumerateObject().Count());
    }

    private static TinyFarmDefinitions WithRecipes(
        TinyFarmDefinitions baseline,
        IReadOnlyList<CookingRecipeDefinition> recipes)
    {
        return new TinyFarmDefinitions(
            baseline.Identity,
            baseline.Items,
            baseline.Crops,
            baseline.Scenes,
            baseline.SceneContent,
            baseline.Schedules,
            baseline.ScheduleContent,
            baseline.ForageNodes,
            recipes);
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
        Assert.Equal(before, TinyFarmSemanticHash.Compute(batch.State));
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
