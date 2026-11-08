using TinyFarm.Core;
using static TinyFarm.InputMan.TinyFarmMenuMessage;
using Kind = TinyFarm.InputMan.TinyFarmMenuCommand;

namespace TinyFarm.InputMan;

/// <summary>Staging is a transient preview, never a second inventory.</summary>
public sealed partial class TinyFarmGame
{
    private TinyFarmCraftingViewState CraftingState => UiScope.GetOrCreate(TinyFarmUiStateKeys.Crafting, () => new TinyFarmCraftingViewState());
    private Dictionary<ProductId, int> CraftInputCounts => CraftingState.Inputs;
    public SceneObjectId? CraftStation
    {
        get => CraftingState.Station;
        private set => CraftingState.Station = value;
    }
    public CookingRecipeId? SelectedCraftRecipe
    {
        get => CraftingState.SelectedRecipe;
        private set => CraftingState.SelectedRecipe = value;
    }
    public IReadOnlyList<CookingRecipeInput> CraftInputs => CraftInputCounts.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
        .Select(pair => new CookingRecipeInput(pair.Key, pair.Value)).ToArray();
    public TinyFarmCraftQuote CraftQuote => TinyFarmCrafting.Quote(State, Definitions, TinyFarmIds.Player, CraftInputs);

    private void HandleCraftAction(TinyFarmMenuMessage action)
    {
        switch (action)
        {
            case Command(Kind.Experiment):
                SelectedCraftRecipe = null;
                CraftInputCounts.Clear();
                break;
            case Command(Kind.FirstLearnedRecipe):
                StageCraftRecipe(null);
                break;
            case Recipe(var recipe):
                StageCraftRecipe(recipe);
                break;
            case Ingredient(var product, var delta) when delta is -1 or 1:
                if (!Definitions.Items.Any(item => item.Id == product))
                {
                    return;
                }
                int available = TinyFarmCrafting.IsSupplied(Definitions, product) ? 99 : State.ProductCount(TinyFarmIds.Player, product);
                int count = Math.Clamp(CraftInputCounts.GetValueOrDefault(product) + delta, 0, Math.Min(99, available));
                if (count == 0)
                {
                    CraftInputCounts.Remove(product);
                }
                else
                {
                    CraftInputCounts[product] = count;
                }
                break;
            case Command(Kind.Craft) when CraftStation is SceneObjectId station:
                Execute(new CraftIntent(station, CraftInputs, SelectedCraftRecipe));
                break;
            case Command(Kind.Eat):
                Execute(new EatIntent(Product: CraftQuote.Output));
                break;
        }
    }

    private void StageCraftRecipe(CookingRecipeId? wanted)
    {
        CookingRecipeDefinition? recipe = Definitions.CookingRecipes.FirstOrDefault(candidate =>
            (wanted is null || candidate.Id == wanted)
            && State.Actor(TinyFarmIds.Player).Rpg?.Crafting?.Knows(candidate.Id) == true);
        if (recipe is null)
        {
            return;
        }
        SelectedCraftRecipe = recipe.Id;
        CraftInputCounts.Clear();
        foreach (CookingRecipeInput input in recipe.Inputs)
        {
            CraftInputCounts[input.Product] = input.Count;
        }
    }

    private void ApplyCraftingFeedback(GameIntent intent, IntentResult result)
    {
        if (State.Version < TinyFarmState.CraftingSaveVersion)
        {
            return;
        }
        GameEvent? opened = result.Events.FirstOrDefault(item => item.Kind == GameEventKind.CraftingStationOpened);
        if (opened?.SceneObject is SceneObjectId station)
        {
            uiFlow.ReturnTo(TinyFarmScreen.Playing);
            uiFlow.Push(TinyFarmScreen.Crafting);
            CraftStation = station;
            Status = "Choose a learned recipe or experiment with ingredients. Water is supplied by the stove.";
        }
        else if (result.Events.Any(item => item.Kind == GameEventKind.ItemTaken))
        {
            GameEvent taken = result.Events.First(item => item.Kind == GameEventKind.ItemTaken);
            if (taken.Item == TinyFarmIds.WildMint)
            {
                Status = "Wild mint, tucked away for Mara's supper.";
            }
            else
            {
                Status = taken.Item is ItemId item ? "Picked up " + State.Item(item).Name + ". Open I to read or use it." : "Item collected.";
            }
        }
        else if (result.Events.Any(item => item.Kind == GameEventKind.ForageGathered))
        {
            bool mushrooms = result.Events.Any(item => item.Kind == GameEventKind.ForageGathered && item.Product == TinyFarmIds.HenOfTheWoods);
            Status = mushrooms
                ? "Hen-of-the-woods gathered. Saute it at your stove for Mara's supper."
                : "Ingredients gathered. Bring them to the stove.";
        }
        else if (intent is CraftIntent)
        {
            Status = result.Reason switch
            {
                IntentReason.InsufficientSpirit => "Not enough SP. Exhausted; ingredients retained. Eat or rest before trying again.",
                IntentReason.ExperimentFailed => "No recipe matched. 2 SP spent; ingredients retained.",
                IntentReason.MissingIngredient => "Missing ingredients. Nothing spent.",
                _ when result.Status == IntentResultStatus.Rejected => "Craft rejected: " + result.Reason + ". Nothing spent.",
                _ => "Cooked! " + (result.Events.Any(item => item.Kind == GameEventKind.RecipeLearned) ? "Recipe discovered. " : "")
                    + "SP spent: " + result.Events.Where(item => item.Kind == GameEventKind.SpiritSpent).Sum(item => item.Amount)
                    + "; Cooking XP: " + result.Events.Where(item => item.Kind == GameEventKind.SkillPracticed && item.Skill == TinyFarmSkill.Cooking).Sum(item => item.Amount) + "."
            };
        }
        else if (intent is ReadRecipeIntent)
        {
            Status = result.Status switch
            {
                IntentResultStatus.Rejected => "Read failed: " + result.Reason,
                IntentResultStatus.NoOp => "Recipe already learned. Visit the stove to cook.",
                _ => "Recipe learned. Visit the stove to cook."
            };
        }
        else if (intent is EatIntent)
        {
            Status = result.Status == IntentResultStatus.Accepted
                ? "Food restored " + result.Events.Where(item => item.Kind == GameEventKind.SpiritRecovered).Sum(item => item.Amount) + " SP."
                : "No food needed or available.";
        }
    }
}
