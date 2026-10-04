using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmMenuPresentation
{
    private const uint Gold = 0xF5DDB5FF;
    private static readonly StandardColors TableColors = StandardTheme.Default.Colors with
    {
        Background = ColorToken.Hex(0x233C33FF),
        Muted = ColorToken.Hex(0x1B322BFF),
        Secondary = ColorToken.Hex(0x29473FFF),
        Primary = ColorToken.Hex(0x4A6655FF),
        Foreground = ColorToken.Hex(0xE7EBDDFF),
        Border = ColorToken.Hex(0x83978440)
    };

    public static UiNode Build(TinyFarmGame game)
    {
        var nodes = new List<UiNode>();
        TinyFarmNativeUi.Panel(nodes, "menu-shade", 0, 0, 1280, 720, 0x0C211B99);
        if (game.Screen == TinyFarmScreen.Inventory)
        {
            Inventory(nodes, game);
        }
        else
        {
            Pause(nodes, game);
        }
        return UI.Surface(id: "tinyfarm-menu", width: 1280, height: 720, children: nodes);
    }

    private static void Inventory(List<UiNode> nodes, TinyFarmGame game)
    {
        TinyFarmMenus menu = game.Menus;
        IReadOnlyList<TinyFarmInventoryRow> rows = menu.Rows(game.State, game.Definitions);
        TinyFarmInventoryRow? selected = rows.SingleOrDefault(row => row.Key == menu.SelectedKey);
        TinyFarmNativeUi.Panel(nodes, "bag-panel", 44, 56, 1192, 612, 0x19362BFC);
        Label(nodes, "bag-title", "POCKETS & EQUIPMENT", 66, 78, 650, TextSize.H1, Gold);
        Label(nodes, "bag-summary", $"{game.State.Actor(TinyFarmIds.Player).Money} coins  /  {rows.Count} matching items  /  world paused", 68, 120, 790);
        Button(nodes, "close", "Close / I or ESC", 1022, 80, 190);
        Button(nodes, "bag", "Inventory", 234, 162, 138, selected: !menu.EquipmentOnly);
        Button(nodes, "equipment", "Equipment", 380, 162, 140, selected: menu.EquipmentOnly);
        string search = menu.Search.Length == 0 ? "Click to search..." : menu.Search;
        if (menu.SearchFocused)
        {
            search += " |";
        }
        Button(nodes, "search", search, 536, 162, 590, selected: menu.SearchFocused);
        Button(nodes, "clear", "Clear", 1134, 162, 78);

        for (int index = 0; index < TinyFarmGame.MenuCategories.Length; index++)
        {
            InventoryCategory category = TinyFarmGame.MenuCategories[index];
            Button(nodes, "category:" + category, category.ToString(), 66, 218 + index * 39, 152,
                selected: menu.Category == category);
        }

        UiTableColumn[] columns =
        [
            new(Header("Name", InventorySort.Name, menu), 225, UiAction.Named("sort:name")),
            new(Header("Category", InventorySort.Category, menu), 130, UiAction.Named("sort:category")),
            new(Header("Qty", InventorySort.Quantity, menu), 65, UiAction.Named("sort:quantity")),
            new(Header("Value", InventorySort.Value, menu), 90, UiAction.Named("sort:value")),
            new(Header("Equipped", InventorySort.Equipped, menu), 110, UiAction.Named("sort:equipped"))
        ];
        UiTableRow[] visible = rows.Skip(menu.Offset).Take(TinyFarmMenus.PageSize).Select(row => new UiTableRow(
            row.Key, [row.Name, row.Category.ToString(), row.Quantity.ToString(), row.Value.ToString(), row.Equipped ? "Yes" : "-"],
            UiAction.Named("row:" + row.Key), row.Key == menu.SelectedKey)).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build("inventory-table", columns, visible, colors: TableColors),
            left: 234, top: 218, width: 620, height: 376));
        if (rows.Count == 0)
        {
            Label(nodes, "empty", "No items match. Try All or Clear.", 250, 279, 590, color: Gold);
        }
        Button(nodes, "previous", "Previous", 234, 600, 128, disabled: menu.Offset == 0);
        Button(nodes, "next", "Next", 726, 600, 128, disabled: menu.Offset + TinyFarmMenus.PageSize >= rows.Count);
        int first = rows.Count == 0 ? 0 : menu.Offset + 1;
        Label(nodes, "range", $"{first}-{Math.Min(rows.Count, menu.Offset + TinyFarmMenus.PageSize)} / {rows.Count}", 400, 609, 300);

        TinyFarmNativeUi.Panel(nodes, "item-details", 878, 218, 334, 376, 0x233D31FF);
        if (selected is not null)
        {
            Wrap(nodes, "item-name", selected.Name, 898, 236, 30, Gold);
            Label(nodes, "item-category", selected.Category.ToString().ToUpperInvariant(), 898, 301, 294, color: Gold);
            Label(nodes, "item-quantity", $"Quantity: {selected.Quantity}  /  Value: {selected.Value}", 898, 337, 294);
            Wrap(nodes, "description", selected.Description, 898, 377, 30);
            if (selected.Slot is EquipmentSlot slot)
            {
                Label(nodes, "item-slot", "Slot: " + slot, 898, 491, 294);
                Button(nodes, "primary", selected.Equipped ? "Unequip / ENTER" : "Equip / ENTER", 898, 544, 294,
                    disabled: game.State.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 });
            }
            else if (selected.Product?.Value == "turnip-broth" && game.State.Slice is not null)
            {
                Button(nodes, "primary", "Eat broth / ENTER", 898, 544, 294, disabled: game.State.Slice.Health >= 12);
            }
            else
            {
                Label(nodes, "item-use", "Use it in the world with E / K.", 898, 548, 294);
            }
        }
        else
        {
            Label(nodes, "no-selection", "Select an item to inspect it.", 898, 246, 294);
        }
        Label(nodes, "bag-footer", "UP/DOWN select  LEFT/RIGHT category  ENTER use  /  Value is per item; stacks are not unique gear.", 66, 638, 1140, TextSize.Sm);
        Label(nodes, "bag-status", Short(game.Status, 100), 66, 686, 1150, color: Gold);
    }

    private static void Pause(List<UiNode> nodes, TinyFarmGame game)
    {
        TinyFarmNativeUi.Panel(nodes, "pause-panel", 300, 100, 680, 520, 0x19362BFC);
        Label(nodes, "pause-title", "A MOMENT AT HOME", 336, 129, 590, TextSize.H1, Gold);
        Label(nodes, "pause-subtitle", "The world waits while you are here.", 338, 175, 590);
        bool busy = game.SaveInProgress || game.LoadInProgress;
        if (game.Menus.Confirmation is string confirmation)
        {
            string text = confirmation == "load"
                ? "Load your checkpoint? Unsaved progress will be replaced."
                : "Quit TinyFarm? Save first to keep your latest progress.";
            Wrap(nodes, "confirmation", text, 338, 250, 58, Gold);
            Button(nodes, "confirm", confirmation == "load" ? "Load checkpoint / ENTER" : "Quit / ENTER", 338, 356, 604, disabled: busy);
            Button(nodes, "close", "Cancel / ESC", 338, 407, 604);
        }
        else
        {
            string[] labels = ["Resume", "Inventory & Equipment", "Save checkpoint", "Load checkpoint", "Quit"];
            for (int index = 0; index < labels.Length; index++)
            {
                bool disabled = busy || index == 3 && !game.MenuSaveAvailable;
                Button(nodes, TinyFarmGame.PauseActions[index], labels[index], 338, 228 + index * 49, 604,
                    selected: game.Menus.PauseSelection == index, disabled: disabled);
            }
            string checkpoint = game.MenuSaveAvailable ? "Checkpoint available" : "No checkpoint yet / Save to create one";
            Label(nodes, "checkpoint", checkpoint + "  /  one local save slot", 338, 496, 604);
        }
        Wrap(nodes, "pause-status", game.Status, 338, 535, 60, Gold);
        Label(nodes, "pause-hint", "UP/DOWN choose  ENTER confirm  ESC return", 338, 594, 604, TextSize.Sm);
    }

    private static string Header(string label, InventorySort sort, TinyFarmMenus menu)
    {
        if (menu.Sort != sort)
        {
            return label;
        }
        return label + (menu.Descending ? " v" : " ^");
    }

    private static void Button(List<UiNode> nodes, string action, string label, int x, int y, int width,
        bool selected = false, bool disabled = false)
    {
        uint color = selected ? 0x4A6655FFu : 0x29473FFFu;
        uint foreground = disabled ? 0x8A9B8EFFu : Gold;
        nodes.Add(UI.Anchor(StandardUI.Button(label, id: "menu." + action, action: UiAction.Named(action), disabled: disabled,
            style: new StandardButtonStyle(ColorToken.Hex(color), ColorToken.Hex(foreground),
                ColorToken.Hex(0x83978480), 1,
                new TextStyle(ColorToken.Hex(foreground), TextSize.Md, TextAlignX.Center, TextAlignY.Center), width, 34)),
            left: x, top: y, width: width, height: 34));
    }

    private static void Label(List<UiNode> nodes, string id, string text, int x, int y, int width,
        TextSize size = TextSize.Md, uint color = 0xE7EBDDFF)
    {
        TinyFarmNativeUi.Text(nodes, id, text, x, y, width, size, color);
    }

    private static void Wrap(List<UiNode> nodes, string id, string text, int x, int y, int characters,
        uint color = 0xE7EBDDFF)
    {
        string line = "";
        int row = 0;
        foreach (string word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > characters && line.Length > 0)
            {
                Label(nodes, id + row, line, x, y + row * 23, characters * 10, color: color);
                line = "";
                row++;
            }
            line += (line.Length == 0 ? "" : " ") + word;
        }
        Label(nodes, id + row, line, x, y + row * 23, characters * 10, color: color);
    }

    private static string Short(string text, int limit)
    {
        return text.Length <= limit ? text : text[..(limit - 3)] + "...";
    }
}
