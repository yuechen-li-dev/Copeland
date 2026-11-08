using Machina.Core.Actions;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

/// <summary>Game messages and stable diagnostic aliases. Dispatch never interprets the aliases.</summary>
public static class TinyFarmUiActions
{
    public static readonly UiAction<TinyFarmMenuMessage> Close = Command("close", TinyFarmMenuCommand.Close);
    public static readonly UiAction<TinyFarmMenuMessage> NewGame = Command("new-game", TinyFarmMenuCommand.NewGame);
    public static readonly UiAction<TinyFarmMenuMessage> Resume = Command("resume", TinyFarmMenuCommand.Resume);
    public static readonly UiAction<TinyFarmMenuMessage> Inventory = Command("inventory", TinyFarmMenuCommand.Inventory);
    public static readonly UiAction<TinyFarmMenuMessage> Stats = Command("stats", TinyFarmMenuCommand.Stats);
    public static readonly UiAction<TinyFarmMenuMessage> Save = Command("save", TinyFarmMenuCommand.Save);
    public static readonly UiAction<TinyFarmMenuMessage> Load = Command("load", TinyFarmMenuCommand.Load);
    public static readonly UiAction<TinyFarmMenuMessage> Quit = Command("quit", TinyFarmMenuCommand.Quit);
    public static readonly UiAction<TinyFarmMenuMessage> Confirm = Command("confirm", TinyFarmMenuCommand.Confirm);
    public static readonly UiAction<TinyFarmMenuMessage> Search = Command("search", TinyFarmMenuCommand.Search);
    public static readonly UiAction<TinyFarmMenuMessage> Clear = Command("clear", TinyFarmMenuCommand.Clear);
    public static readonly UiAction<TinyFarmMenuMessage> Bag = Command("bag", TinyFarmMenuCommand.Bag);
    public static readonly UiAction<TinyFarmMenuMessage> Equipment = Command("equipment", TinyFarmMenuCommand.Equipment);
    public static readonly UiAction<TinyFarmMenuMessage> Primary = Command("primary", TinyFarmMenuCommand.Primary);
    public static readonly UiAction<TinyFarmMenuMessage> Previous = Command("previous", TinyFarmMenuCommand.Previous);
    public static readonly UiAction<TinyFarmMenuMessage> Next = Command("next", TinyFarmMenuCommand.Next);
    public static readonly UiAction<TinyFarmMenuMessage> AgentPrevious = Command("agent:previous", TinyFarmMenuCommand.AgentPrevious);
    public static readonly UiAction<TinyFarmMenuMessage> AgentNext = Command("agent:next", TinyFarmMenuCommand.AgentNext);
    public static readonly UiAction<TinyFarmMenuMessage> Experiment = Command("experiment", TinyFarmMenuCommand.Experiment);
    public static readonly UiAction<TinyFarmMenuMessage> FirstLearnedRecipe = Command("recipe", TinyFarmMenuCommand.FirstLearnedRecipe);
    public static readonly UiAction<TinyFarmMenuMessage> Craft = Command("craft", TinyFarmMenuCommand.Craft);
    public static readonly UiAction<TinyFarmMenuMessage> Eat = Command("eat", TinyFarmMenuCommand.Eat);

    public static readonly UiActionFamily<InventoryCategory, TinyFarmMenuMessage> Categories = new(
        new UiActionId("category"), value => value.ToString(), value => new TinyFarmMenuMessage.Category(value));
    public static readonly UiActionFamily<InventorySort, TinyFarmMenuMessage> Sorts = new(
        new UiActionId("sort"), value => value.ToString().ToLowerInvariant(), value => new TinyFarmMenuMessage.Sort(value));
    public static readonly UiActionFamily<string, TinyFarmMenuMessage> StatsGroups = new(
        new UiActionId("group"), value => value, value => new TinyFarmMenuMessage.StatsGroup(value));
    public static readonly UiActionFamily<CookingRecipeId, TinyFarmMenuMessage> Recipes = new(
        new UiActionId("recipe"), value => value.Value, value => new TinyFarmMenuMessage.Recipe(value));
    public static readonly UiActionFamily<ProductId, TinyFarmMenuMessage> AddIngredients = new(
        new UiActionId("add"), value => value.Value, value => new TinyFarmMenuMessage.Ingredient(value, 1));
    public static readonly UiActionFamily<ProductId, TinyFarmMenuMessage> RemoveIngredients = new(
        new UiActionId("remove"), value => value.Value, value => new TinyFarmMenuMessage.Ingredient(value, -1));

    public static UiAction<TinyFarmMenuMessage> SelectRow(TinyFarmInventoryKey key)
    {
        return UiAction.Typed<TinyFarmMenuMessage>(InventoryId(new UiActionId("row"), key), new TinyFarmMenuMessage.SelectRow(key));
    }

    public static UiAction<TinyFarmMenuMessage> MoveStack(bool value)
    {
        return UiAction.Typed<TinyFarmMenuMessage>(new UiActionId("quantity").Child(value ? "stack" : "one"),
            new TinyFarmMenuMessage.MoveStack(value));
    }

    public static UiAction<TinyFarmMenuMessage> ContainerPage(TinyFarmContainerSide side, int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        UiActionId id = new UiActionId("page").Child(side.ToString().ToLowerInvariant())
            .Child(direction > 0 ? "next" : "previous");
        return UiAction.Typed<TinyFarmMenuMessage>(id, new TinyFarmMenuMessage.ContainerPage(side, direction));
    }

    public static UiAction<TinyFarmMenuMessage> Transfer(TinyFarmContainerSide side, TinyFarmInventoryKey key)
    {
        UiActionId root = new(side == TinyFarmContainerSide.Bag ? "deposit" : "withdraw");
        return UiAction.Typed<TinyFarmMenuMessage>(InventoryId(root, key), new TinyFarmMenuMessage.Transfer(side, key));
    }

    private static UiActionId InventoryId(UiActionId root, TinyFarmInventoryKey key)
    {
        return key switch
        {
            TinyFarmInventoryKey.Item item => root.Child("item").Child(item.Id.Value),
            TinyFarmInventoryKey.Product product => root.Child("product").Child(product.Id.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    private static UiAction<TinyFarmMenuMessage> Command(string name, TinyFarmMenuCommand command)
    {
        return UiAction.Typed<TinyFarmMenuMessage>(new UiActionId(name), new TinyFarmMenuMessage.Command(command));
    }
}
