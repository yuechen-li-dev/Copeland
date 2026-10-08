using TinyFarm.Core;

namespace TinyFarm.InputMan;

/// <summary>Staging is a transient preview, never a second inventory.</summary>
public sealed partial class TinyFarmGame
{
    private readonly Dictionary<ProductId, int> craftInputs = new();
    public SceneObjectId? CraftStation { get; private set; }
    public CookingRecipeId? SelectedCraftRecipe { get; private set; }
    public IReadOnlyList<CookingRecipeInput> CraftInputs => craftInputs.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
        .Select(pair => new CookingRecipeInput(pair.Key, pair.Value)).ToArray();
    public string CraftCacheKey => string.Join(";", CraftInputs.Select(input => input.Product.Value + "=" + input.Count))
        + "|" + SelectedCraftRecipe;
    public TinyFarmCraftQuote CraftQuote => TinyFarmCrafting.Quote(State, Definitions, TinyFarmIds.Player, CraftInputs);

    public void HandleCraftAction(string action)
    {
        if (action == "experiment")
        {
            SelectedCraftRecipe = null;
            craftInputs.Clear();
        }
        else if (action == "recipe")
        {
            CookingRecipeDefinition? recipe = Definitions.CookingRecipes.FirstOrDefault(candidate =>
                State.Actor(TinyFarmIds.Player).Rpg?.Crafting?.Knows(candidate.Id) == true);
            if (recipe is not null)
            {
                SelectedCraftRecipe = recipe.Id;
                craftInputs.Clear();
                foreach (CookingRecipeInput input in recipe.Inputs)
                {
                    craftInputs[input.Product] = input.Count;
                }
            }
        }
        else if (action.StartsWith("add:", StringComparison.Ordinal) || action.StartsWith("remove:", StringComparison.Ordinal))
        {
            bool add = action.StartsWith("add:", StringComparison.Ordinal);
            var product = new ProductId(action[(add ? 4 : 7)..]);
            if (!Definitions.Items.Any(item => item.Id == product))
            {
                return;
            }
            int available = TinyFarmCrafting.IsSupplied(Definitions, product) ? 99 : State.ProductCount(TinyFarmIds.Player, product);
            int count = Math.Clamp(craftInputs.GetValueOrDefault(product) + (add ? 1 : -1), 0, Math.Min(99, available));
            if (count == 0)
            {
                craftInputs.Remove(product);
            }
            else
            {
                craftInputs[product] = count;
            }
        }
        else if (action == "craft" && CraftStation is SceneObjectId station)
        {
            Execute(new CraftIntent(station, CraftInputs, SelectedCraftRecipe));
        }
        else if (action == "eat")
        {
            Execute(new EatIntent(Product: CraftQuote.Output));
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
            CraftStation = station;
            SelectedCraftRecipe = null;
            craftInputs.Clear();
            Menus.SearchFocused = false;
            Screen = TinyFarmScreen.Crafting;
            Status = "Choose a learned recipe or experiment with ingredients. Water is supplied by the stove.";
        }
        else if (result.Events.Any(item => item.Kind == GameEventKind.ItemTaken))
        {
            GameEvent taken = result.Events.First(item => item.Kind == GameEventKind.ItemTaken);
            Status = taken.Item is ItemId item ? "Picked up " + State.Item(item).Name + ". Open I to read or use it." : "Item collected.";
        }
        else if (result.Events.Any(item => item.Kind == GameEventKind.ForageGathered))
        {
            Status = "Ingredients gathered. Bring them to the stove.";
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
