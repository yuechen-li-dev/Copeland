using Machina.Runtime.State;

namespace TinyFarm.InputMan;

internal static class TinyFarmUiStateKeys
{
    public static readonly UiStateKey<TinyFarmTitleViewState> Title = new("widget.title");
    public static readonly UiStateKey<TinyFarmInventoryViewState> Inventory = new("widget.inventory");
    public static readonly UiStateKey<TinyFarmStatsViewState> Stats = new("widget.stats");
    public static readonly UiStateKey<TinyFarmSearchViewState> Search = new("widget.search");
    public static readonly UiStateKey<UiState<bool>> SearchFocus = new("widget.search-focus", UiStateLifetime.Activation);
    public static readonly UiStateKey<TinyFarmPauseViewState> Pause = new("widget.pause");
    public static readonly UiStateKey<TinyFarmCraftingViewState> Crafting = new("widget.crafting");
    public static readonly UiStateKey<TinyFarmContainerViewState> Container = new("widget.container");
}
