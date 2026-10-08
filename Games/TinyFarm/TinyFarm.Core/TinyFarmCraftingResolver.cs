namespace TinyFarm.Core;

public sealed partial class TinyFarmResolver
{
    private IntentReason CraftingStationReason(TinyFarmState state, ActorState actor, SceneObjectId station)
    {
        SceneDefinition? scene = Scenes.All.SingleOrDefault(candidate =>
            candidate.Objects.Any(item => item.Id == station));
        if (scene is null || scene.Object(station).Kind != SceneObjectKind.CookingStation)
        {
            return IntentReason.WrongStation;
        }
        if (state.ActorScene(actor.Id).Scene != scene.Id)
        {
            return IntentReason.StationWrongScene;
        }
        return TinyFarmSpatialQueries.SelectObjectTarget(state, actor.Id, station, Scenes)?.Kind
            == InteractionTargetKind.CookingStation ? IntentReason.None : IntentReason.StationOutOfRange;
    }

    private IntentResult ResolveCraft(TinyFarmState state, ActorState actor, IntentEnvelope envelope, CraftIntent intent)
    {
        IntentReason stationReason = CraftingStationReason(state, actor, intent.Station);
        if (stationReason != IntentReason.None)
        {
            return Rejected(envelope, stationReason);
        }
        if (definitions is null || state.Version < TinyFarmState.CraftingSaveVersion || actor.Rpg?.Crafting is null
            || actor.Agent?.Kind == TinyFarmAgentKind.Object)
        {
            return Rejected(envelope, IntentReason.RpgUnavailable);
        }
        if (intent.Inputs is null || intent.Inputs.Count is < 1 or > 16
            || intent.Inputs.Any(input => input is null || input.Count is < 1 or > 99
                || !definitions.Items.Any(item => item.Id == input.Product))
            || intent.Inputs.Select(input => input.Product).Distinct().Count() != intent.Inputs.Count)
        {
            return Rejected(envelope, IntentReason.InvalidCraftInputs);
        }
        TinyFarmRpgProfile profile = actor.Rpg;
        if (intent.Recipe is CookingRecipeId selected && !profile.Crafting.Knows(selected))
        {
            return Rejected(envelope, IntentReason.RecipeNotKnown);
        }
        TinyFarmCraftQuote quote = TinyFarmCrafting.Quote(state, definitions, actor.Id, intent.Inputs);
        if (intent.Recipe is not null && quote.Recipe?.Id != intent.Recipe)
        {
            return Rejected(envelope, IntentReason.InvalidCraftInputs);
        }
        if (!quote.IngredientsAvailable)
        {
            return Rejected(envelope, IntentReason.MissingIngredient);
        }
        if (quote.Recipe is not null && quote.Output is ProductId output && !CraftOutputFits(state, actor.Id, intent.Inputs, output, quote.Recipe.OutputCount))
        {
            return Rejected(envelope, IntentReason.InventoryFull);
        }
        if (profile.SpiritCurrent < quote.SpiritCost)
        {
            int spent = profile.SpiritCurrent;
            TinyFarmActiveCondition exhausted = new(TinyFarmConditionKind.Exhausted, intent.Station);
            ReplaceActor(state, actor with
            {
                Rpg = profile with
                {
                    SpiritCurrent = 0,
                    ActiveConditions = (profile.ActiveConditions ?? []).Where(condition => condition.Kind != TinyFarmConditionKind.Exhausted)
                        .Append(exhausted).ToArray()
                }
            });
            return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.InsufficientSpirit,
                [new GameEvent(GameEventKind.SpiritSpent, actor.Id, Amount: spent),
                    new GameEvent(GameEventKind.CraftingExhausted, actor.Id, SceneObject: intent.Station)]);
        }
        profile = profile with { SpiritCurrent = profile.SpiritCurrent - quote.SpiritCost };
        var events = new List<GameEvent> { new(GameEventKind.SpiritSpent, actor.Id, Amount: quote.SpiritCost) };
        if (quote.Recipe is null || quote.Output is null)
        {
            ReplaceActor(state, actor with { Rpg = profile });
            events.Add(new GameEvent(GameEventKind.CraftExperimentFailed, actor.Id, SceneObject: intent.Station));
            return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.ExperimentFailed, events);
        }
        foreach (CookingRecipeInput input in intent.Inputs)
        {
            if (!TinyFarmCrafting.IsSupplied(definitions, input.Product))
            {
                SetProductCount(state, actor.Id, input.Product, state.ProductCount(actor.Id, input.Product) - input.Count);
            }
        }
        SetProductCount(state, actor.Id, quote.Output.Value, state.ProductCount(actor.Id, quote.Output.Value) + quote.Recipe.OutputCount);
        if (!quote.Known)
        {
            profile = LearnRecipe(profile, quote.Recipe.Id, "Experiment at " + intent.Station.Value);
            events.Add(new GameEvent(GameEventKind.RecipeLearned, actor.Id, Recipe: quote.Recipe.Id, SceneObject: intent.Station));
        }
        foreach ((TinyFarmSkill skill, int experience) in new[]
        {
            (TinyFarmSkill.Cooking, quote.Experience), (TinyFarmSkill.Fire, quote.FireExperience)
        })
        {
            if (profile.ProgressionEnabled && TinyFarmCrafting.Rank(profile, skill) < 50 && experience > 0)
            {
                profile = TinyFarmCrafting.Award(profile, skill, experience);
                events.Add(new GameEvent(GameEventKind.SkillPracticed, actor.Id, Amount: experience, Skill: skill));
            }
        }
        ReplaceActor(state, actor with { Rpg = profile });
        if (actor.IsPlayer && state.Slice is not null)
        {
            state.Slice = state.Slice with { CookedBroth = true };
        }
        events.Add(new GameEvent(GameEventKind.RecipeCooked, actor.Id, Product: quote.Output,
            Amount: quote.Recipe.OutputCount, Recipe: quote.Recipe.Id, SceneObject: intent.Station));
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private bool CraftOutputFits(TinyFarmState state, ActorId actor, IReadOnlyList<CookingRecipeInput> inputs, ProductId output, int count)
    {
        var stacks = state.InventoryStacks.Where(stack => stack.Actor == actor)
            .ToDictionary(stack => stack.Product, stack => stack.Count);
        foreach (CookingRecipeInput input in inputs)
        {
            if (!TinyFarmCrafting.IsSupplied(definitions!, input.Product))
            {
                stacks[input.Product] -= input.Count;
            }
        }
        long quantity = (long)stacks.GetValueOrDefault(output) + count;
        if (quantity > TinyFarmCrafting.StackLimit)
        {
            return false;
        }
        stacks[output] = (int)quantity;
        return stacks.Count(pair => pair.Value > 0) <= TinyFarmCrafting.BagSlots;
    }

    private static TinyFarmRpgProfile LearnRecipe(TinyFarmRpgProfile profile, CookingRecipeId recipe, string source)
    {
        if (profile.Crafting is null || profile.Crafting.Knows(recipe))
        {
            return profile;
        }
        return profile with
        {
            Crafting = profile.Crafting with
            {
                KnownRecipes = profile.Crafting.KnownRecipes.Append(new TinyFarmRecipeKnowledge(recipe, source))
                    .OrderBy(known => known.Recipe.Value, StringComparer.Ordinal).ToArray()
            }
        };
    }

    private IntentResult ResolveReadRecipe(TinyFarmState state, ActorState actor, IntentEnvelope envelope, ReadRecipeIntent intent)
    {
        ItemState? item = FindItem(state, intent.Item);
        if (item?.Owner != actor.Id || !actor.Inventory.Contains(intent.Item))
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }
        if (state.Version < TinyFarmState.CraftingSaveVersion || actor.Rpg?.Crafting is null
            || item.TeachesRecipe is not CookingRecipeId recipe
            || definitions?.CookingRecipes.Any(candidate => candidate.Id == recipe && candidate.Crafting is not null) != true)
        {
            return Rejected(envelope, IntentReason.UnknownRecipe);
        }
        if (actor.Rpg.Crafting.Knows(recipe))
        {
            return NoOp(envelope, IntentReason.None);
        }
        ReplaceActor(state, actor with { Rpg = LearnRecipe(actor.Rpg, recipe, "Read " + item.Id.Value) });
        return Accepted(envelope, new GameEvent(GameEventKind.RecipeLearned, actor.Id, Item: item.Id, Recipe: recipe));
    }

    private IntentResult ResolveCraftingFood(TinyFarmState state, ActorState actor, IntentEnvelope envelope, EatIntent intent)
    {
        ProductId? product = intent.Product;
        if (product is null)
        {
            product = state.InventoryStacks.Where(stack => stack.Actor == actor.Id && stack.Count > 0)
                .OrderBy(stack => stack.Product.Value, StringComparer.Ordinal)
                .FirstOrDefault(stack => definitions!.Item(stack.Product).Food is not null)?.Product;
        }
        ItemDefinition? item = definitions?.Items.SingleOrDefault(candidate => candidate.Id == product);
        if (product is null || item?.Food is not TinyFarmFoodEffect food || state.ProductCount(actor.Id, product.Value) <= 0)
        {
            return Rejected(envelope, IntentReason.MissingIngredient);
        }
        TinyFarmRpgProfile profile = actor.Rpg!;
        int health = actor.IsPlayer && state.Slice is not null ? state.Slice.Health : actor.Agent?.Health?.Current ?? 0;
        int maximum = actor.IsPlayer && state.Slice is not null ? 12 : actor.Agent?.Health?.Maximum ?? 0;
        int healed = Math.Min(food.HealthRestore, maximum - health);
        int spirit = Math.Min(food.SpiritRestore, profile.SpiritMaximum - profile.SpiritCurrent);
        if (healed == 0 && spirit == 0)
        {
            return NoOp(envelope, IntentReason.None);
        }
        SetProductCount(state, actor.Id, product.Value, state.ProductCount(actor.Id, product.Value) - 1);
        TinyFarmAgentState? agent = actor.Agent;
        if (!actor.IsPlayer && agent?.Health is not null)
        {
            agent = agent with { Health = agent.Health with { Current = health + healed } };
        }
        ReplaceActor(state, actor with { Rpg = TinyFarmCrafting.Recover(profile, spirit), Agent = agent });
        if (actor.IsPlayer && state.Slice is not null)
        {
            state.Slice = state.Slice with { Health = health + healed };
        }
        return Accepted(envelope, new GameEvent(GameEventKind.PlayerHealed, actor.Id, Amount: healed, Product: product),
            new GameEvent(GameEventKind.SpiritRecovered, actor.Id, Amount: spirit, Product: product));
    }

    private static void RestoreCraftingSpiritForRest(TinyFarmState state, ActorId id)
    {
        ActorState actor = state.Actor(id);
        if (actor.Rpg?.Crafting is not null)
        {
            ReplaceActor(state, actor with { Rpg = TinyFarmCrafting.Recover(actor.Rpg, 0, rest: true) });
        }
    }
}
