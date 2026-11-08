using TinyFarm.Core;

namespace TinyFarm.InputMan;

public enum TinyFarmMenuCommand
{
    Close, NewGame, Resume, Inventory, Stats, Save, Load, Quit, Confirm,
    Search, Clear, Bag, Equipment, Primary, Previous, Next, AgentPrevious, AgentNext,
    Experiment, FirstLearnedRecipe, Craft, Eat,
}

public enum TinyFarmContainerSide
{
    Bag,
    Chest,
}

public enum TinyFarmConfirmation
{
    Load,
    Quit,
}

public abstract record TinyFarmMenuMessage
{
    public sealed record Command(TinyFarmMenuCommand Kind) : TinyFarmMenuMessage;
    public sealed record SelectRow(TinyFarmInventoryKey Key) : TinyFarmMenuMessage;
    public sealed record Category(InventoryCategory Value) : TinyFarmMenuMessage;
    public sealed record Sort(InventorySort Value) : TinyFarmMenuMessage;
    public sealed record StatsGroup(string Value) : TinyFarmMenuMessage;
    public sealed record Recipe(CookingRecipeId Id) : TinyFarmMenuMessage;
    public sealed record Ingredient(ProductId Product, int Delta) : TinyFarmMenuMessage;
    public sealed record MoveStack(bool Value) : TinyFarmMenuMessage;
    public sealed record ContainerPage(TinyFarmContainerSide Side, int Direction) : TinyFarmMenuMessage;
    public sealed record Transfer(TinyFarmContainerSide Side, TinyFarmInventoryKey Key) : TinyFarmMenuMessage;
}
