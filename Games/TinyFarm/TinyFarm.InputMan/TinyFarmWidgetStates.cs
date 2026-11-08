using TinyFarm.Core;

namespace TinyFarm.InputMan;

internal sealed class TinyFarmTitleViewState
{
    public int Selection { get; set; }
}

internal sealed class TinyFarmSearchViewState
{
    public string Text { get; set; } = "";
}

internal sealed class TinyFarmInventoryViewState
{
    public InventoryCategory Category { get; set; }
    public InventorySort Sort { get; set; }
    public bool Descending { get; set; }
    public bool EquipmentOnly { get; set; }
    public string? SelectedKey { get; set; }
    public int Offset { get; set; }
}

internal sealed class TinyFarmStatsViewState
{
    public ActorId Agent { get; set; } = TinyFarmIds.Player;
    public string Group { get; set; } = "Stats";
    public int Offset { get; set; }
}

internal sealed class TinyFarmPauseViewState
{
    public int Selection { get; set; }
    public string? Confirmation { get; set; }
}

internal sealed class TinyFarmCraftingViewState
{
    public Dictionary<ProductId, int> Inputs { get; } = new();
    public SceneObjectId? Station { get; set; }
    public CookingRecipeId? SelectedRecipe { get; set; }
}

internal sealed class TinyFarmContainerViewState
{
    public ActorId? Container { get; set; }
    public bool MoveStack { get; set; }
    public bool DepositSide { get; set; } = true;
    public int PlayerOffset { get; set; }
    public int ChestOffset { get; set; }
    public int Selection { get; set; }
}
