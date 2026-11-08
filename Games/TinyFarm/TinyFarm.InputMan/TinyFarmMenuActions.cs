using InputMan.Core;
using Machina.Core.Actions;
using static TinyFarm.InputMan.TinyFarmMenuMessage;
using Kind = TinyFarm.InputMan.TinyFarmMenuCommand;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

public sealed partial class TinyFarmGame
{
    public static readonly InventoryCategory[] MenuCategories =
    [
        InventoryCategory.All, InventoryCategory.Weapons, InventoryCategory.Tools, InventoryCategory.Seeds,
        InventoryCategory.Ingredients, InventoryCategory.Food, InventoryCategory.Materials, InventoryCategory.Keepsakes
    ];

    public static readonly UiAction<TinyFarmMenuMessage>[] PauseActions =
    [
        TinyFarmUiActions.Resume, TinyFarmUiActions.Inventory, TinyFarmUiActions.Stats,
        TinyFarmUiActions.Save, TinyFarmUiActions.Load, TinyFarmUiActions.Quit,
    ];
    public static readonly string[] StatsGroups = ["All", "Identity", "Resources", "Stats", "Skills", "Traits", "Conditions", "Equipment", "Inventory", "Presentation"];
    public int TitleSelection
    {
        get => TitleState.Selection;
        private set => TitleState.Selection = value;
    }
    private TinyFarmTitleViewState TitleState => UiScope.GetOrCreate(TinyFarmUiStateKeys.Title, () => new TinyFarmTitleViewState());
    public static readonly UiAction<TinyFarmMenuMessage>[] TitleActions =
        [TinyFarmUiActions.NewGame, TinyFarmUiActions.Load, TinyFarmUiActions.Quit];
    public bool MenuSaveAvailable { get; private set; }

    public void OpenPause()
    {
        uiFlow.ReturnTo(TinyFarmScreen.Playing);
        uiFlow.Push(TinyFarmScreen.Paused);
        MenuSaveAvailable = HasSave;
    }

    public void OpenInventory(bool fromPause)
    {
        uiFlow.ReturnTo(fromPause ? TinyFarmScreen.Paused : TinyFarmScreen.Playing);
        uiFlow.Push(TinyFarmScreen.Inventory);
        Menus.Rows(State, Definitions);
    }

    public void OpenStats(bool fromPause)
    {
        uiFlow.ReturnTo(fromPause ? TinyFarmScreen.Paused : TinyFarmScreen.Playing);
        uiFlow.Push(TinyFarmScreen.Stats);
    }

    private void HandleMenuInput(InputFrame input)
    {
        if (input.WasPressed(GameControls.UiCancel))
        {
            BackFromMenu();
            return;
        }
        if (Screen == TinyFarmScreen.Title)
        {
            if (input.WasPressed(GameControls.UiUp))
            {
                TitleSelection = Math.Max(0, TitleSelection - 1);
            }
            if (input.WasPressed(GameControls.UiDown))
            {
                TitleSelection = Math.Min(TitleActions.Length - 1, TitleSelection + 1);
            }
            if (input.WasPressed(GameControls.UiConfirm))
            {
                DispatchMenu(TitleActions[TitleSelection]);
            }
            return;
        }
        if (Screen == TinyFarmScreen.Container)
        {
            HandleContainerInput(input);
            return;
        }
        if (Screen == TinyFarmScreen.Crafting)
        {
            if (input.WasPressed(GameControls.UiConfirm))
            {
                DispatchMenu(TinyFarmUiActions.Craft);
            }
            return;
        }
        if (Screen == TinyFarmScreen.Stats)
        {
            int count = Menus.PropertyRows(State, Definitions).Count;
            if (input.WasPressed(GameControls.UiUp))
            {
                Menus.ScrollStats(-1, count);
            }
            if (input.WasPressed(GameControls.UiDown))
            {
                Menus.ScrollStats(1, count);
            }
            if (input.WasPressed(GameControls.UiLeft))
            {
                Menus.ChangeStatsAgent(-1, State);
            }
            if (input.WasPressed(GameControls.UiRight))
            {
                Menus.ChangeStatsAgent(1, State);
            }
            return;
        }
        if (Screen == TinyFarmScreen.Inventory)
        {
            IReadOnlyList<TinyFarmInventoryRow> rows = Menus.Rows(State, Definitions);
            if (input.WasPressed(GameControls.UiUp))
            {
                Menus.MoveSelection(-1, rows);
            }
            if (input.WasPressed(GameControls.UiDown))
            {
                Menus.MoveSelection(1, rows);
            }
            int category = Array.IndexOf(MenuCategories, Menus.Category);
            if (input.WasPressed(GameControls.UiLeft))
            {
                Menus.SetCategory(MenuCategories[Math.Max(0, category - 1)]);
            }
            if (input.WasPressed(GameControls.UiRight))
            {
                Menus.SetCategory(MenuCategories[Math.Min(MenuCategories.Length - 1, category + 1)]);
            }
            if (input.WasPressed(GameControls.UiConfirm))
            {
                DispatchMenu(TinyFarmUiActions.Primary);
            }
            return;
        }
        if (Screen == TinyFarmScreen.Paused)
        {
            if (input.WasPressed(GameControls.UiUp))
            {
                Menus.MovePause(-1);
            }
            if (input.WasPressed(GameControls.UiDown))
            {
                Menus.MovePause(1);
            }
            if (input.WasPressed(GameControls.UiConfirm))
            {
                DispatchMenu(Menus.Confirmation is null ? PauseActions[Menus.PauseSelection] : TinyFarmUiActions.Confirm);
            }
            return;
        }
        if (input.WasPressed(GameControls.UiConfirm))
        {
            Start();
        }
    }

    public void BackFromMenu()
    {
        if (Menus.Confirmation is not null)
        {
            Menus.Confirmation = null;
        }
        else if (IsAgentMenu && Menus.SearchFocused)
        {
            Menus.SearchFocused = false;
        }
        else if (Screen == TinyFarmScreen.Container && OpenContainer is ActorId chest)
        {
            Execute(new CloseContainerIntent(chest));
        }
        else if (Screen is TinyFarmScreen.Stats or TinyFarmScreen.Inventory)
        {
            uiFlow.Back();
        }
        else if (Screen != TinyFarmScreen.Title && !LoadInProgress)
        {
            Start();
        }
    }

    public void DispatchMenu(UiAction<TinyFarmMenuMessage> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        DispatchMenuMessage(action.Message);
    }

    public bool TryDispatchMenu(UiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.TryGetMessage<TinyFarmMenuMessage>(out var message))
        {
            return false;
        }
        DispatchMenuMessage(message);
        return true;
    }

    private void DispatchMenuMessage(TinyFarmMenuMessage message)
    {
        if ((!IsModalMenu && Screen != TinyFarmScreen.Paused) || LoadInProgress)
        {
            return;
        }
        if (Screen == TinyFarmScreen.Paused && SaveInProgress && message is not Command(Kind.Close))
        {
            return;
        }
        Menus.SearchFocused = IsAgentMenu && message is Command(Kind.Search);
        if (message is Command(Kind.Close))
        {
            BackFromMenu();
            return;
        }
        if (Screen == TinyFarmScreen.Title)
        {
            switch (message)
            {
                case Command(Kind.NewGame):
                    Start();
                    break;
                case Command(Kind.Load) when MenuSaveAvailable:
                    BeginLoad();
                    break;
                case Command(Kind.Quit):
                    ShouldQuit = true;
                    break;
            }
            return;
        }
        if (Screen == TinyFarmScreen.Container)
        {
            HandleContainerAction(message);
            return;
        }
        if (Screen == TinyFarmScreen.Crafting)
        {
            HandleCraftAction(message);
            return;
        }
        if (Screen == TinyFarmScreen.Inventory)
        {
            HandleInventoryAction(message);
            return;
        }
        if (Screen == TinyFarmScreen.Stats)
        {
            HandleStatsAction(message);
            return;
        }
        switch (message)
        {
            case Command(Kind.Resume):
                Start();
                break;
            case Command(Kind.Inventory):
                OpenInventory(true);
                break;
            case Command(Kind.Stats):
                OpenStats(true);
                break;
            case Command(Kind.Save):
                BeginSave();
                break;
            case Command(Kind.Load) when MenuSaveAvailable && !SaveInProgress:
                Menus.Confirmation = TinyFarmConfirmation.Load;
                break;
            case Command(Kind.Quit):
                Menus.Confirmation = TinyFarmConfirmation.Quit;
                break;
            case Command(Kind.Confirm) when Menus.Confirmation == TinyFarmConfirmation.Load:
                if (BeginLoad())
                {
                    Menus.Confirmation = null;
                }
                break;
            case Command(Kind.Confirm) when Menus.Confirmation == TinyFarmConfirmation.Quit:
                ShouldQuit = true;
                break;
        }
    }

    private void HandleStatsAction(TinyFarmMenuMessage action)
    {
        if (action is StatsGroup(var group))
        {
            if (StatsGroups.Contains(group))
            {
                Menus.SetStatsGroup(group);
            }
            return;
        }
        int count = Menus.PropertyRows(State, Definitions).Count;
        switch (action)
        {
            case Command(Kind.AgentPrevious):
                Menus.ChangeStatsAgent(-1, State);
                break;
            case Command(Kind.AgentNext):
                Menus.ChangeStatsAgent(1, State);
                break;
            case Command(Kind.Previous):
                Menus.ScrollStats(-TinyFarmMenus.PageSize, count);
                break;
            case Command(Kind.Next):
                Menus.ScrollStats(TinyFarmMenus.PageSize, count);
                break;
            case Command(Kind.Clear):
                Menus.SetSearch("");
                break;
        }
    }

    private void HandleInventoryAction(TinyFarmMenuMessage action)
    {
        IReadOnlyList<TinyFarmInventoryRow> rows = Menus.Rows(State, Definitions);
        switch (action)
        {
            case SelectRow(var key):
                Menus.Select(key, rows);
                break;
            case Category(var category) when MenuCategories.Contains(category):
                Menus.SetCategory(category);
                break;
            case Sort(var sort) when Enum.IsDefined(sort):
                Menus.SetSort(sort);
                break;
            case Command(Kind.Bag):
                Menus.SetEquipmentOnly(false);
                break;
            case Command(Kind.Equipment):
                Menus.SetEquipmentOnly(true);
                break;
            case Command(Kind.Clear):
                Menus.SetSearch("");
                break;
            case Command(Kind.Previous):
                Menus.Scroll(-TinyFarmMenus.PageSize, rows.Count);
                break;
            case Command(Kind.Next):
                Menus.Scroll(TinyFarmMenus.PageSize, rows.Count);
                break;
            case Command(Kind.Primary):
                UseSelectedInventoryRow(rows);
                break;
        }
    }

    private void UseSelectedInventoryRow(IReadOnlyList<TinyFarmInventoryRow> rows)
    {
        TinyFarmInventoryRow? selected = rows.SingleOrDefault(row => row.Key == Menus.SelectedKey);
        if (selected?.Slot is EquipmentSlot slot)
        {
            TinyFarmStepResult result = Execute(new SetEquipmentIntent(slot, selected.Equipped ? null : selected.Item));
            IntentResult equipment = result.Results.First(item => item.Envelope.Actor == TinyFarmIds.Player
                && item.Envelope.Intent is SetEquipmentIntent);
            bool accepted = equipment.Status != IntentResultStatus.Rejected;
            Status = accepted ? selected.Name + (selected.Equipped ? " unequipped." : " equipped.")
                : "Finish your swing or dodge before changing equipment.";
        }
        else if (selected?.Item is ItemId recipeCard && State.Item(recipeCard).TeachesRecipe is not null)
        {
            Execute(new ReadRecipeIntent(recipeCard));
        }
        else if (selected?.Product is ProductId food && Definitions.Items.Any(item => item.Id == food && item.Food is not null))
        {
            Execute(new EatIntent(food));
        }
        else if (selected?.Product?.Value == "turnip-broth" && State.Slice is not null)
        {
            Execute(new EatIntent());
        }
    }

    public void EnterMenuText(string text)
    {
        if (IsAgentMenu && Menus.SearchFocused)
        {
            string printable = new(text.Where(character => !char.IsControl(character)).ToArray());
            Menus.SetSearch(Menus.Search + printable);
        }
    }

    public void EditMenuSearch(bool clear)
    {
        if (IsAgentMenu && Menus.SearchFocused)
        {
            string next = "";
            if (!clear && Menus.Search.Length > 0)
            {
                next = Menus.Search[..^1];
            }
            Menus.SetSearch(next);
        }
    }
}
