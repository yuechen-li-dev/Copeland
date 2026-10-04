using Deliverance.Core.Storage;
using InputMan.Core;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmCraftingA2Tests
{
    private static readonly SceneObjectId Stove = TinyFarmIds.HearthHouseKitchen;
    private static CookingRecipeInput[] Inputs(bool salt = false) => salt
        ? [new(TinyFarmIds.Turnip, 1), new(TinyFarmCraftingContent.Water, 1), new(TinyFarmCraftingContent.Salt, 1)]
        : [new(TinyFarmIds.Turnip, 1), new(TinyFarmCraftingContent.Water, 1)];

    private static TinyFarmState Ready(TinyFarmDefinitions definitions, int spirit = 20, bool known = false, int rank = 0)
    {
        TinyFarmState state = TinyFarmCraftingContent.Start(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 10));
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmCraftingContent.Salt, 2));
        int index = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[index];
        state.MutableActors[index] = player with
        {
            Location = TinyFarmIds.Farmhouse,
            Rpg = player.Rpg! with
            {
                SpiritCurrent = spirit, SpiritMaximum = Math.Max(20, spirit),
                Skills = [new TinyFarmSkillProgress(TinyFarmSkill.Cooking, rank)],
                Crafting = new TinyFarmCraftingProgress(known ? [new(TinyFarmCraftingContent.Recipe, "Test recipe card")] : [])
            }
        };
        int placement = state.MutableActorScenes.FindIndex(row => row.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placement] = state.MutableActorScenes[placement] with
        {
            Scene = TinyFarmSceneIds.Residence, WorldPosition = new ScenePosition(6656, 5632), Facing = ActorFacing.Up
        };
        return state;
    }

    private static ResolutionBatchResult Run(TinyFarmState state, TinyFarmDefinitions definitions, GameIntent intent) =>
        new TinyFarmResolver(definitions).Resolve(state,
            [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, 0, IntentSourceKind.Human)]);

    [Fact]
    public void IngredientEconomyAllowsPositiveSpiritRecoveryWithoutPurchasableProduce()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState start = TinyFarmCraftingContent.Start(definitions);
        Assert.DoesNotContain(start.ShopStock, stock => stock.Product == TinyFarmIds.Turnip || stock.Product == TinyFarmCraftingContent.Soup);
        Assert.Contains(definitions.ForageNodes, node => node.Id == TinyFarmCraftingContent.SaltOutcrop && node.Product == TinyFarmCraftingContent.Salt);
        TinyFarmState state = Ready(definitions, spirit: 8, known: true);
        state = Run(state, definitions, new CraftIntent(Stove, Inputs(), TinyFarmCraftingContent.Recipe)).State;
        Assert.Equal(4, state.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Equal(9, state.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip));
        state = Run(state, definitions, new EatIntent(TinyFarmCraftingContent.Soup)).State;
        Assert.Equal(16, state.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Equal(12, state.Slice!.Health);
        Assert.Equal(0, state.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Soup));
    }

    [Fact]
    public void ExperimentDiscoversRecipeAndFutureCraftsAreCheaper()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions);
        Assert.Equal(6, TinyFarmCrafting.Quote(state, definitions, TinyFarmIds.Player, Inputs()).SpiritCost);
        ResolutionBatchResult first = Run(state, definitions, new CraftIntent(Stove, Inputs()));
        Assert.Equal(IntentReason.None, first.Results.Single().Reason);
        Assert.Contains(first.Results.Single().Events, item => item.Kind == GameEventKind.RecipeLearned);
        Assert.True(first.State.Actor(TinyFarmIds.Player).Rpg!.Crafting!.Knows(TinyFarmCraftingContent.Recipe));
        Assert.Equal(4, TinyFarmCrafting.Quote(first.State, definitions, TinyFarmIds.Player, Inputs()).SpiritCost);
        Assert.Equal(20, first.State.Actor(TinyFarmIds.Player).Rpg!.Skills.Single(skill => skill.Skill == TinyFarmSkill.Cooking).Experience);
        Assert.Equal(5, first.State.Actor(TinyFarmIds.Player).Rpg!.Skills.Single(skill => skill.Skill == TinyFarmSkill.Fire).Experience);
        Assert.Equal(IntentReason.RecipeNotKnown, Run(state, definitions, new CookIntent(Stove, TinyFarmCraftingContent.Recipe)).Results.Single().Reason);
    }

    [Theory]
    [InlineData(false, 29)]
    [InlineData(true, 19)]
    public void SaltChangesDifficultyAndCreatesPersistentFoodVariant(bool known, int cost)
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions, spirit: 100, known: known);
        TinyFarmCraftQuote quote = TinyFarmCrafting.Quote(state, definitions, TinyFarmIds.Player, Inputs(true));
        Assert.Equal(cost, quote.SpiritCost);
        Assert.Equal(40, quote.Experience);
        state = Run(state, definitions, new CraftIntent(Stove, Inputs(true))).State;
        Assert.Equal(1, state.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.SaltedSoup));
        Assert.Equal(1, state.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Salt));
        Assert.Equal(20, definitions.Item(TinyFarmCraftingContent.SaltedSoup).Food!.SpiritRestore);
        var session = new TinyFarmSession(state, definitions);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, definitions), definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(restored.State));
        Assert.Equal(1, restored.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.SaltedSoup));
    }

    [Fact]
    public void InsufficientSpiritCommitsExhaustionButRetainsIngredientsAndAwardsNothing()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions, known: false);
        ResolutionBatchResult failed = Run(state, definitions, new CraftIntent(Stove, Inputs(true)));
        Assert.Equal(IntentResultStatus.Accepted, failed.Results.Single().Status);
        Assert.Equal(IntentReason.InsufficientSpirit, failed.Results.Single().Reason);
        TinyFarmRpgProfile profile = failed.State.Actor(TinyFarmIds.Player).Rpg!;
        Assert.Equal(0, profile.SpiritCurrent);
        Assert.Single(profile.ActiveConditions!);
        Assert.Empty(profile.Crafting!.KnownRecipes);
        Assert.Equal(0, profile.Skills.Single().Experience);
        Assert.Equal(state.InventoryStacks, failed.State.InventoryStacks);
        profile = TinyFarmCrafting.Recover(profile, 3);
        Assert.Single(profile.ActiveConditions!);
        profile = TinyFarmCrafting.Recover(profile, 1);
        Assert.Empty(profile.ActiveConditions!);
        var session = new TinyFarmSession(failed.State, definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(failed.State), TinyFarmSemanticHash.Compute(
            TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, definitions), definitions).State));
    }

    [Fact]
    public void UnmatchedExperimentCostsSmallFeeWithoutConsumingIngredients()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions);
        ResolutionBatchResult result = Run(state, definitions, new CraftIntent(Stove, [new(TinyFarmIds.Turnip, 2)]));
        Assert.Equal(IntentReason.ExperimentFailed, result.Results.Single().Reason);
        Assert.Equal(18, result.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Equal(state.InventoryStacks, result.State.InventoryStacks);
        Assert.Empty(result.State.Actor(TinyFarmIds.Player).Rpg!.Crafting!.KnownRecipes);
    }

    [Theory]
    [InlineData(1, 3, 10)]
    [InlineData(5, 1, 1)]
    [InlineData(20, 1, 1)]
    public void LowLevelRecipesBecomeTrivial(int rank, int cost, int experience)
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmCraftQuote quote = TinyFarmCrafting.Quote(Ready(definitions, known: true, rank: rank), definitions, TinyFarmIds.Player, Inputs());
        Assert.Equal(cost, quote.SpiritCost);
        Assert.Equal(experience, quote.Experience);
        if (experience == 1)
        {
            Assert.Equal(0, quote.FireExperience);
        }
    }

    [Fact]
    public void CraftRanksUseBoundedCurveAndCarryRemainderWithoutChangingBaseStats()
    {
        TinyFarmRpgProfile profile = TinyFarmRpgProfile.Starter() with { Crafting = new TinyFarmCraftingProgress([]) };
        TinyFarmRpgProfile advanced = TinyFarmCrafting.Award(profile, TinyFarmSkill.Cooking, 250);
        Assert.Equal(new TinyFarmSkillProgress(TinyFarmSkill.Cooking, 2, 50), advanced.Skills.Single());
        Assert.Equal(profile.BaseAbilities, advanced.BaseAbilities);
        Assert.Equal(profile, TinyFarmCrafting.Award(profile with { ProgressionEnabled = false }, TinyFarmSkill.Cooking, 250) with { ProgressionEnabled = true });
    }

    [Fact]
    public void MalformedMissingAndFullBagAttemptsRejectWithoutMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions);
        GameIntent[] invalid = [new CraftIntent(Stove, []), new CraftIntent(Stove, [new(TinyFarmIds.Turnip, 1), new(TinyFarmIds.Turnip, 1)]),
            new CraftIntent(Stove, [new(TinyFarmIds.Wood, 1)]), new CraftIntent(new SceneObjectId("no-stove"), Inputs())];
        foreach (GameIntent intent in invalid)
        {
            ResolutionBatchResult rejected = Run(state, definitions, intent);
            Assert.Equal(IntentResultStatus.Rejected, rejected.Results.Single().Status);
            Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(rejected.State));
        }
        state.MutableInventoryStacks.Add(new(TinyFarmIds.Player, TinyFarmCraftingContent.Soup, 99));
        ResolutionBatchResult full = Run(state, definitions, new CraftIntent(Stove, Inputs()));
        Assert.Equal(IntentReason.InventoryFull, full.Results.Single().Reason);
        Assert.Equal(TinyFarmSemanticHash.Compute(state), TinyFarmSemanticHash.Compute(full.State));
    }

    [Fact]
    public void OpeningStoveAndStagingInputsPauseGameplayWithoutChangingWorld()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        var game = new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-a2-tests", Guid.NewGuid().ToString("N"))),
            slice: true, authored: new TinyFarmAuthoredWorld(definitions, Ready(definitions)));
        game.Start();
        string hash = TinyFarmSemanticHash.Compute(game.State);
        game.Execute(new InteractIntent());
        Assert.Equal(TinyFarmScreen.Crafting, game.Screen);
        Assert.DoesNotContain(GameControls.Gameplay, game.Contexts);
        game.DispatchMenu("add:turnip");
        game.DispatchMenu("add:water");
        var engine = new InputManEngine(GameControls.CreateProfile(true));
        engine.SetMaps(game.Contexts);
        engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool> { [Controls.Key(KeyboardKey.W)] = true }, new Dictionary<ControlKey, float>()), .016f, 0);
        game.Handle(engine.CurrentFrame);
        game.Advance(TimeSpan.FromSeconds(2), engine.CurrentFrame, true);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
        game.DispatchMenu("craft");
        Assert.Equal(1, game.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Soup));
        game.BackFromMenu();
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
    }

    [Fact]
    public void ExplorationCardRequiresOwnershipAndTeachesWithoutSpiritOrExperience()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions);
        Assert.Equal(IntentReason.ItemNotOwned, Run(state, definitions, new ReadRecipeIntent(TinyFarmCraftingContent.RecipeCard)).Results.Single().Reason);
        int placement = state.MutableActorScenes.FindIndex(row => row.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placement] = state.MutableActorScenes[placement] with
        {
            WorldPosition = new ScenePosition(4608, 6656), Facing = ActorFacing.Up
        };
        ResolutionBatchResult taken = Run(state, definitions, new TakeIntent(TinyFarmCraftingContent.RecipeCard));
        Assert.Equal(IntentResultStatus.Accepted, taken.Results.Single().Status);
        ResolutionBatchResult read = Run(taken.State, definitions, new ReadRecipeIntent(TinyFarmCraftingContent.RecipeCard));
        Assert.True(read.State.Actor(TinyFarmIds.Player).Rpg!.Crafting!.Knows(TinyFarmCraftingContent.Recipe));
        Assert.Equal(20, read.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Equal(0, read.State.Actor(TinyFarmIds.Player).Rpg!.Skills.Single().Experience);
        Assert.Equal(IntentResultStatus.NoOp, Run(read.State, definitions, new ReadRecipeIntent(TinyFarmCraftingContent.RecipeCard)).Results.Single().Status);
    }

    [Fact]
    public void ExhaustionRestAndFullPoolFoodUseActualReducers()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState state = Ready(definitions);
        state = Run(state, definitions, new CraftIntent(Stove, Inputs(true))).State;
        int placement = state.MutableActorScenes.FindIndex(row => row.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[placement] = state.MutableActorScenes[placement] with
        {
            WorldPosition = new ScenePosition(3584, 6656), Facing = ActorFacing.Up
        };
        state = Run(state, definitions, new SleepIntent()).State;
        Assert.Equal(20, state.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Empty(state.Actor(TinyFarmIds.Player).Rpg!.ActiveConditions!);
        state.MutableInventoryStacks.Add(new(TinyFarmIds.Player, TinyFarmCraftingContent.Soup, 1));
        ResolutionBatchResult full = Run(state, definitions, new EatIntent(TinyFarmCraftingContent.Soup));
        Assert.Equal(IntentResultStatus.NoOp, full.Results.Single().Status);
        Assert.Equal(1, full.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Soup));
    }

    [Fact]
    public void GeneratedReplayCarriesCraftFailureDiscoveryFoodAndVariantIntents()
    {
        TinyFarmDefinitions definitions = TinyFarmCraftingContent.Load();
        TinyFarmState initial = Ready(definitions, spirit: 100);
        TinyFarmState state = initial;
        GameIntent[] intents = [new CraftIntent(Stove, [new(TinyFarmIds.Turnip, 2)]),
            new CraftIntent(Stove, Inputs()), new CraftIntent(Stove, Inputs(true)),
            new EatIntent(TinyFarmCraftingContent.SaltedSoup)];
        var records = new List<TinyFarmReplayRecord>();
        for (int index = 0; index < intents.Length; index++)
        {
            var envelope = new IntentEnvelope(TinyFarmIds.Player, intents[index], state.Minute, index, IntentSourceKind.Human);
            state = new TinyFarmResolver(definitions).Resolve(state, [envelope]).State;
            records.Add(new TinyFarmReplayRecord(index, envelope, TinyFarmSemanticHash.Compute(state)));
        }
        TinyFarmReplayEnvelope replay = TinyFarmSemanticReplay.Create(initial, definitions.Identity, "crafting-a2", records);
        TinyFarmReplayResult result = TinyFarmSemanticReplay.Replay(
            TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(replay)), definitions, "crafting-a2");
        Assert.Equal(TinyFarmSemanticHash.Compute(state), result.FinalHash);
    }

    [Theory]
    [InlineData(3, 19, 40)]
    [InlineData(6, 52, 80)]
    [InlineData(9, 103, 160)]
    public void DifficultRecipesPermitDeterministicCatchUpWithLargerSpiritPools(int level, int cost, int experience)
    {
        TinyFarmDefinitions source = TinyFarmCraftingContent.Load();
        CookingRecipeDefinition recipe = source.CookingRecipes.Single();
        var definitions = new TinyFarmDefinitions(source.Identity, source.Items, source.Crops, source.Scenes,
            source.SceneContent, source.Schedules, source.ScheduleContent, source.ForageNodes,
            [recipe with { Crafting = recipe.Crafting! with { Level = level } }], source.Trees, source.Enemies);
        TinyFarmState state = Ready(definitions, spirit: 200, known: true);
        TinyFarmCraftQuote quote = TinyFarmCrafting.Quote(state, definitions, TinyFarmIds.Player, Inputs());
        Assert.Equal(cost, quote.SpiritCost);
        Assert.Equal(experience, quote.Experience);
        state = Run(state, definitions, new CraftIntent(Stove, Inputs(), recipe.Id)).State;
        Assert.Equal(200 - cost, state.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent);
        Assert.Equal(experience / 100, TinyFarmCrafting.Rank(state.Actor(TinyFarmIds.Player).Rpg!, TinyFarmSkill.Cooking));
    }
}
