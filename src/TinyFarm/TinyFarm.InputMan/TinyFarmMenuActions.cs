using InputMan.Core;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

public sealed partial class TinyFarmGame
{
    public static readonly InventoryCategory[] MenuCategories =
    [
        InventoryCategory.All, InventoryCategory.Weapons, InventoryCategory.Tools, InventoryCategory.Seeds,
        InventoryCategory.Ingredients, InventoryCategory.Food, InventoryCategory.Materials, InventoryCategory.Keepsakes
    ];

    public static readonly string[] PauseActions = ["resume", "inventory", "save", "load", "quit"];
    public bool MenuSaveAvailable { get; private set; }

    public void OpenInventory(bool fromPause)
    {
        Menus.InventoryFromPause = fromPause;
        Menus.SearchFocused = false;
        Menus.Confirmation = null;
        Screen = TinyFarmScreen.Inventory;
        Menus.Rows(State, Definitions);
    }

    private void HandleMenuInput(InputFrame input)
    {
        if (input.WasPressed(GameControls.UiCancel))
        {
            BackFromMenu();
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
                DispatchMenu("primary");
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
                DispatchMenu(Menus.Confirmation is null ? PauseActions[Menus.PauseSelection] : "confirm");
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
        else if (Screen == TinyFarmScreen.Inventory)
        {
            Menus.SearchFocused = false;
            Screen = Menus.InventoryFromPause ? TinyFarmScreen.Paused : TinyFarmScreen.Playing;
        }
        else if (Screen != TinyFarmScreen.Title && !LoadInProgress)
        {
            Start();
        }
    }

    public void DispatchMenu(string action)
    {
        if (Screen is not TinyFarmScreen.Inventory and not TinyFarmScreen.Paused)
        {
            return;
        }
        if (LoadInProgress)
        {
            return;
        }
        if (Screen == TinyFarmScreen.Paused && SaveInProgress && action != "close")
        {
            return;
        }
        Menus.SearchFocused = action == "search";
        if (action == "close")
        {
            BackFromMenu();
            return;
        }
        if (Screen == TinyFarmScreen.Inventory)
        {
            HandleInventoryAction(action);
            return;
        }
        switch (action)
        {
            case "resume":
                Start();
                break;
            case "inventory":
                OpenInventory(true);
                break;
            case "save":
                BeginSave();
                break;
            case "load" when MenuSaveAvailable && !SaveInProgress:
                Menus.Confirmation = "load";
                break;
            case "quit":
                Menus.Confirmation = "quit";
                break;
            case "confirm" when Menus.Confirmation == "load":
                if (BeginLoad())
                {
                    Menus.Confirmation = null;
                }
                break;
            case "confirm" when Menus.Confirmation == "quit":
                ShouldQuit = true;
                break;
        }
    }

    private void HandleInventoryAction(string action)
    {
        IReadOnlyList<TinyFarmInventoryRow> rows = Menus.Rows(State, Definitions);
        if (action.StartsWith("row:", StringComparison.Ordinal))
        {
            Menus.Select(action[4..], rows);
            return;
        }
        if (action.StartsWith("category:", StringComparison.Ordinal))
        {
            foreach (InventoryCategory category in MenuCategories)
            {
                if (action == "category:" + category)
                {
                    Menus.SetCategory(category);
                    break;
                }
            }
            return;
        }
        switch (action)
        {
            case "bag":
                Menus.SetEquipmentOnly(false);
                return;
            case "equipment":
                Menus.SetEquipmentOnly(true);
                return;
            case "clear":
                Menus.SetSearch("");
                return;
            case "sort:name":
                Menus.SetSort(InventorySort.Name);
                return;
            case "sort:category":
                Menus.SetSort(InventorySort.Category);
                return;
            case "sort:quantity":
                Menus.SetSort(InventorySort.Quantity);
                return;
            case "sort:value":
                Menus.SetSort(InventorySort.Value);
                return;
            case "sort:equipped":
                Menus.SetSort(InventorySort.Equipped);
                return;
            case "previous":
                Menus.Scroll(-TinyFarmMenus.PageSize, rows.Count);
                return;
            case "next":
                Menus.Scroll(TinyFarmMenus.PageSize, rows.Count);
                return;
            case "primary":
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
                else if (selected?.Product?.Value == "turnip-broth" && State.Slice is not null)
                {
                    Execute(new EatIntent());
                }
                break;
        }
    }

    public void EnterMenuText(string text)
    {
        if (Screen == TinyFarmScreen.Inventory && Menus.SearchFocused)
        {
            string printable = new(text.Where(character => !char.IsControl(character)).ToArray());
            Menus.SetSearch(Menus.Search + printable);
        }
    }

    public void EditMenuSearch(bool clear)
    {
        if (Screen == TinyFarmScreen.Inventory && Menus.SearchFocused)
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
