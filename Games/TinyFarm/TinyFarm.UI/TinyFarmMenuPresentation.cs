using Machina.Core.Actions;
using Aurelian.GameMenus;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Machina.Standard.Theme;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    private const uint Gold = TinyFarmUiTheme.Gold;
    private static readonly StandardColors TableColors = StandardTheme.Default.Colors with
    {
        Background = ColorToken.Hex(0x233C33FF),
        Muted = ColorToken.Hex(0x1B322BFF),
        Secondary = ColorToken.Hex(0x29473FFF),
        Primary = ColorToken.Hex(0x4A6655FF),
        Foreground = ColorToken.Hex(0xE7EBDDFF),
        Border = ColorToken.Hex(0x83978440)
    };

    public static UiNode Build(TinyFarmGame game, IMachinaTextMeasurer? measurer = null)
    {
        var nodes = new List<UiNode>();
        if (game.Screen == TinyFarmScreen.Title)
        {
            Title(nodes, game, measurer);
        }
        else if (game.Screen == TinyFarmScreen.Inventory)
        {
            Inventory(nodes, game, measurer);
        }
        else if (game.Screen == TinyFarmScreen.Stats)
        {
            Stats(nodes, game);
        }
        else if (game.Screen == TinyFarmScreen.Container)
        {
            Container(nodes, game, measurer);
        }
        else if (game.Screen == TinyFarmScreen.Crafting)
        {
            Crafting(nodes, game);
        }
        else
        {
            Pause(nodes, game, measurer);
        }
        return UI.Surface(id: "tinyfarm-menu", width: 1280, height: 720, children: nodes);
    }

    private static void Stats(List<UiNode> nodes, TinyFarmGame game)
    {
        TinyFarmMenus menu = game.Menus;
        IReadOnlyList<TinyFarmAgentProperty> rows = menu.PropertyRows(game.State, game.Definitions);
        ActorState agent = game.State.Actor(menu.StatsAgent);
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: "stats-panel", style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        // The native portrait occupies the square at (908, 60), before the Close button.
        nodes.Add(StandardUI.Label("AGENT / " + agent.Name.ToUpperInvariant(), 66, 78, 820, id: "stats-title",
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("Live properties + authored RPG foundation  /  world paused", 68, 120, 820, id: "stats-summary",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / C or ESC", 1022, 80, 190, id: "menu.close",
            action: UiAction.Named("close"),
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("< Agent", 66, 162, 110, id: "menu.agent:previous",
            action: UiAction.Named("agent:previous"),
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("Agent >", 184, 162, 110, id: "menu.agent:next",
            action: UiAction.Named("agent:next"),
            style: TinyFarmUiTheme.Button()));
        string search = menu.Search.Length == 0 ? "Click to filter properties..." : menu.Search;
        if (menu.SearchFocused)
        {
            search += " |";
        }
        nodes.Add(StandardUI.Button(search, 308, 162, 818, id: "menu.search",
            action: UiAction.Named("search"),
            style: TinyFarmUiTheme.Button(selected: menu.SearchFocused)));
        nodes.Add(StandardUI.Button("Clear", 1134, 162, 78, id: "menu.clear",
            action: UiAction.Named("clear"),
            style: TinyFarmUiTheme.Button()));
        for (int index = 0; index < TinyFarmGame.StatsGroups.Length; index++)
        {
            string group = TinyFarmGame.StatsGroups[index];
            nodes.Add(StandardUI.Button(group, 66, 218 + index * 38, 152, id: "menu.group:" + group,
                action: UiAction.Named("group:" + group),
                style: TinyFarmUiTheme.Button(selected: group == menu.StatsGroup)));
        }
        UiTableColumn[] columns = [new("Property", 225), new("Value", 275), new("Source / status", 430)];
        UiTableRow[] visible = rows.Skip(menu.StatsOffset).Take(TinyFarmMenus.PageSize)
            .Select((row, index) => new UiTableRow("property." + (menu.StatsOffset + index),
                [row.Name, row.Value, row.Source], null)).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build("agent-properties", columns, visible, colors: TableColors),
            left: 234, top: 218, width: 930, height: 376));
        if (rows.Count == 0)
        {
            nodes.Add(StandardUI.Label("No properties match. Clear the filter or choose All.", 250, 275, 870, id: "stats-empty",
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        }
        var bar = TinyFarmStatsScrollbar.Geometry(rows.Count, menu.StatsOffset);
        nodes.Add(StandardUI.Panel(1180, 254, 24, 340, id: "stats-scroll-track", style: TinyFarmUiTheme.Panel(0x102A21FF)));
        if (bar.IsVisible)
        {
            nodes.Add(StandardUI.Panel(1180, (int)bar.ThumbRect.Y, 24, (int)bar.ThumbRect.Height, id: "stats-scroll-thumb", style: TinyFarmUiTheme.Panel(0x839784FF)));
        }
        nodes.Add(StandardUI.Button("Previous", 234, 600, 128, id: "menu.previous",
            action: UiAction.Named("previous"), disabled: menu.StatsOffset == 0,
            style: TinyFarmUiTheme.Button(disabled: menu.StatsOffset == 0)));
        nodes.Add(StandardUI.Button("Next", 1036, 600, 128, id: "menu.next",
            action: UiAction.Named("next"), disabled: menu.StatsOffset + TinyFarmMenus.PageSize >= rows.Count,
            style: TinyFarmUiTheme.Button(disabled: menu.StatsOffset + TinyFarmMenus.PageSize >= rows.Count)));
        int first = rows.Count == 0 ? 0 : menu.StatsOffset + 1;
        nodes.Add(StandardUI.Label($"{first}-{Math.Min(rows.Count, menu.StatsOffset + TinyFarmMenus.PageSize)} / {rows.Count}", 430, 609, 500, id: "stats-range",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label("Wheel / drag scroll  UP/DOWN scroll  LEFT/RIGHT agent  TAB filter  ENTER/ESC finish typing", 66, 642, 1140, id: "stats-footer",
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(game.State.Version >= TinyFarmState.CraftingSaveVersion
            ? "A2: Cooking / Fire practice is live; general ability and trait modifiers remain pending."
            : "A1: skill practice and trait/condition modifiers are pending; combat retains its existing rules.", 66, 686, 1140, id: "stats-boundary",
            size: TextSize.Sm, color: TinyFarmUiTheme.Accent));
    }

    private static void Inventory(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        TinyFarmMenus menu = game.Menus;
        IReadOnlyList<TinyFarmInventoryRow> rows = menu.Rows(game.State, game.Definitions);
        TinyFarmInventoryRow? selected = rows.SingleOrDefault(row => row.Key == menu.SelectedKey);
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: "bag-panel", style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label("POCKETS & EQUIPMENT", 66, 78, 650, id: "bag-title",
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label($"{game.State.Actor(TinyFarmIds.Player).Money} coins  /  {rows.Count} matching items  /  world paused", 68, 120, 790, id: "bag-summary",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / I or ESC", 1022, 80, 190, id: "menu.close",
            action: UiAction.Named("close"),
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("Inventory", 234, 162, 138, id: "menu.bag",
            action: UiAction.Named("bag"),
            style: TinyFarmUiTheme.Button(selected: !menu.EquipmentOnly)));
        nodes.Add(StandardUI.Button("Equipment", 380, 162, 140, id: "menu.equipment",
            action: UiAction.Named("equipment"),
            style: TinyFarmUiTheme.Button(selected: menu.EquipmentOnly)));
        string search = menu.Search.Length == 0 ? "Click to search..." : menu.Search;
        if (menu.SearchFocused)
        {
            search += " |";
        }
        nodes.Add(StandardUI.Button(search, 536, 162, 590, id: "menu.search",
            action: UiAction.Named("search"),
            style: TinyFarmUiTheme.Button(selected: menu.SearchFocused)));
        nodes.Add(StandardUI.Button("Clear", 1134, 162, 78, id: "menu.clear",
            action: UiAction.Named("clear"),
            style: TinyFarmUiTheme.Button()));

        for (int index = 0; index < TinyFarmGame.MenuCategories.Length; index++)
        {
            InventoryCategory category = TinyFarmGame.MenuCategories[index];
            nodes.Add(StandardUI.Button(category.ToString(), 66, 218 + index * 39, 152, id: "menu.category:" + category,
                action: UiAction.Named("category:" + category),
                style: TinyFarmUiTheme.Button(selected: menu.Category == category)));
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
            nodes.Add(StandardUI.Label("No items match. Try All or Clear.", 250, 279, 590, id: "empty",
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        }
        nodes.Add(StandardUI.Button("Previous", 234, 600, 128, id: "menu.previous",
            action: UiAction.Named("previous"), disabled: menu.Offset == 0,
            style: TinyFarmUiTheme.Button(disabled: menu.Offset == 0)));
        nodes.Add(StandardUI.Button("Next", 726, 600, 128, id: "menu.next",
            action: UiAction.Named("next"), disabled: menu.Offset + TinyFarmMenus.PageSize >= rows.Count,
            style: TinyFarmUiTheme.Button(disabled: menu.Offset + TinyFarmMenus.PageSize >= rows.Count)));
        int first = rows.Count == 0 ? 0 : menu.Offset + 1;
        nodes.Add(StandardUI.Label($"{first}-{Math.Min(rows.Count, menu.Offset + TinyFarmMenus.PageSize)} / {rows.Count}", 400, 609, 300, id: "range",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));

        nodes.Add(StandardUI.Panel(878, 218, 334, 376, id: "item-details", style: TinyFarmUiTheme.Panel(0x233D31FF)));
        if (selected is not null)
        {
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(selected.Name, 300, id: "item-name",
                foreground: TinyFarmUiTheme.Accent, measurer: measurer), 898, 236));
            nodes.Add(StandardUI.Label(selected.Category.ToString().ToUpperInvariant(), 898, 301, 294, id: "item-category",
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
            nodes.Add(StandardUI.Label($"Quantity: {selected.Quantity}  /  Value: {selected.Value}", 898, 337, 294, id: "item-quantity",
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(selected.Description, 300, id: "description",
                foreground: TinyFarmUiTheme.Text, measurer: measurer), 898, 377));
            if (selected.Slot is EquipmentSlot slot)
            {
                nodes.Add(StandardUI.Label("Slot: " + slot, 898, 491, 294, id: "item-slot",
                    size: TextSize.Md, color: TinyFarmUiTheme.Text));
                nodes.Add(StandardUI.Button(selected.Equipped ? "Unequip / ENTER" : "Equip / ENTER", 898, 544, 294, id: "menu.primary",
                    action: UiAction.Named("primary"), disabled: game.State.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 },
                    style: TinyFarmUiTheme.Button(disabled: game.State.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 })));
            }
            else if (selected.Item is ItemId card && game.State.Item(card).TeachesRecipe is not null)
            {
                nodes.Add(StandardUI.Button("Read recipe / ENTER", 898, 544, 294, id: "menu.primary",
                    action: UiAction.Named("primary"),
                    style: TinyFarmUiTheme.Button()));
            }
            else if (selected.Product is ProductId food && game.Definitions.Items.Any(item => item.Id == food && item.Food is not null))
            {
                nodes.Add(StandardUI.Button("Eat / ENTER", 898, 544, 294, id: "menu.primary",
                    action: UiAction.Named("primary"),
                    style: TinyFarmUiTheme.Button()));
            }
            else if (selected.Product?.Value == "turnip-broth" && game.State.Slice is not null)
            {
                nodes.Add(StandardUI.Button("Eat broth / ENTER", 898, 544, 294, id: "menu.primary",
                    action: UiAction.Named("primary"), disabled: game.State.Slice.Health >= 12,
                    style: TinyFarmUiTheme.Button(disabled: game.State.Slice.Health >= 12)));
            }
            else
            {
                nodes.Add(StandardUI.Label("Use it in the world with E / K.", 898, 548, 294, id: "item-use",
                    size: TextSize.Md, color: TinyFarmUiTheme.Text));
            }
        }
        else
        {
            nodes.Add(StandardUI.Label("Select an item to inspect it.", 898, 246, 294, id: "no-selection",
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
        }
        nodes.Add(StandardUI.Label("UP/DOWN select  LEFT/RIGHT category  ENTER use  /  Value is per item; stacks are not unique gear.", 66, 638, 1140, id: "bag-footer",
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(Short(game.Status, 100), 66, 686, 1150, id: "bag-status",
            size: TextSize.Md, color: TinyFarmUiTheme.Accent));
    }

    private static void Pause(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        nodes.Add(StandardUI.Panel(300, 100, 680, 520, id: "pause-panel", style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label("A MOMENT AT HOME", 336, 129, 590, id: "pause-title",
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("The world waits while you are here.", 338, 175, 590, id: "pause-subtitle",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        bool busy = game.SaveInProgress || game.LoadInProgress;
        if (game.Menus.Confirmation is string confirmation)
        {
            string text = confirmation == "load"
                ? "Load your checkpoint? Unsaved progress will be replaced."
                : "Quit TinyFarm? Save first to keep your latest progress.";
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(text, 580, id: "confirmation",
                foreground: TinyFarmUiTheme.Accent, measurer: measurer), 338, 250));
            nodes.Add(StandardUI.Button(confirmation == "load" ? "Load checkpoint / ENTER" : "Quit / ENTER", 338, 356, 604, id: "menu.confirm",
                action: UiAction.Named("confirm"), disabled: busy,
                style: TinyFarmUiTheme.Button(disabled: busy)));
            nodes.Add(StandardUI.Button("Cancel / ESC", 338, 407, 604, id: "menu.close",
                action: UiAction.Named("close"),
                style: TinyFarmUiTheme.Button()));
        }
        else
        {
            string[] labels = ["Resume", "Inventory & Equipment", "Agent properties", "Save checkpoint", "Load checkpoint", "Quit"];
            var entries = new List<GameMenuEntry>();
            for (int index = 0; index < labels.Length; index++)
            {
                bool disabled = busy || TinyFarmGame.PauseActions[index] == "load" && !game.MenuSaveAvailable;
                entries.Add(new GameMenuEntry(TinyFarmGame.PauseActions[index], labels[index], disabled));
            }
            GameMenuTemplate.AddButtons(nodes, entries, game.Menus.PauseSelection,
                new GameMenuButtonLayout(338, 217, 604, Height: 34, RowSpacing: 44),
                new GameMenuButtonStyle(Background: 0x29473FFF, SelectedBackground: 0x4A6655FF,
                    Foreground: Gold, DisabledForeground: 0x8A9B8EFF, Border: 0x83978480,
                    SelectedBorder: 0x83978480, SelectedBorderWidth: 1, CornerRadius: 0,
                    HorizontalPadding: 0, Alignment: TextAlignX.Center));
            string checkpoint = game.MenuSaveAvailable ? "Checkpoint available" : "No checkpoint yet / Save to create one";
            nodes.Add(StandardUI.Label(checkpoint + "  /  one local save slot", 338, 496, 604, id: "checkpoint",
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
        }
        nodes.Add(StandardUI.Place(StandardUI.Paragraph(game.Status, 600, id: "pause-status",
            foreground: TinyFarmUiTheme.Accent, measurer: measurer), 338, 535));
        nodes.Add(StandardUI.Label("UP/DOWN choose  ENTER confirm  ESC return", 338, 594, 604, id: "pause-hint",
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
    }

    private static string Header(string label, InventorySort sort, TinyFarmMenus menu)
    {
        if (menu.Sort != sort)
        {
            return label;
        }
        return label + (menu.Descending ? " v" : " ^");
    }

    private static string Short(string text, int limit)
    {
        return text.Length <= limit ? text : text[..(limit - 3)] + "...";
    }
}
