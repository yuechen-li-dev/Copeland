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
        return UI.Surface(id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TinyFarmMenu], width: 1280, height: 720, children: nodes);
    }

    private static void Stats(List<UiNode> nodes, TinyFarmGame game)
    {
        TinyFarmMenus menu = game.Menus;
        IReadOnlyList<TinyFarmAgentProperty> rows = menu.PropertyRows(game.State, game.Definitions);
        ActorState agent = game.State.Actor(menu.StatsAgent);
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsPanel], style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        // The native portrait occupies the square at (908, 60), before the Close button.
        nodes.Add(StandardUI.Label("AGENT / " + agent.Name.ToUpperInvariant(), 66, 78, 820, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsTitle],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("Live properties + authored RPG foundation  /  world paused", 68, 120, 820, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsSummary],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / C or ESC", TinyFarmUiActions.Close, 1022, 80, 190,
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("< Agent", TinyFarmUiActions.AgentPrevious, 66, 162, 110,
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("Agent >", TinyFarmUiActions.AgentNext, 184, 162, 110,
            style: TinyFarmUiTheme.Button()));
        string search = menu.Search.Length == 0 ? "Click to filter properties..." : menu.Search;
        if (menu.SearchFocused)
        {
            search += " |";
        }
        nodes.Add(StandardUI.Button(search, TinyFarmUiActions.Search, 308, 162, 818,
            style: TinyFarmUiTheme.Button(selected: menu.SearchFocused)));
        nodes.Add(StandardUI.Button("Clear", TinyFarmUiActions.Clear, 1134, 162, 78,
            style: TinyFarmUiTheme.Button()));
        for (int index = 0; index < TinyFarmGame.StatsGroups.Length; index++)
        {
            string group = TinyFarmGame.StatsGroups[index];
            nodes.Add(StandardUI.Button(group, TinyFarmUiActions.StatsGroups[group], 66, 218 + index * 38, 152,
                style: TinyFarmUiTheme.Button(selected: group == menu.StatsGroup)));
        }
        UiTableColumn[] columns = [new("Property", 225), new("Value", 275), new("Source / status", 430)];
        UiTableRow[] visible = rows.Skip(menu.StatsOffset).Take(TinyFarmMenus.PageSize)
            .Select((row, index) => new UiTableRow(TinyFarmUiIds.Properties[menu.StatsOffset + index],
                [row.Name, row.Value, row.Source], null)).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build(TinyFarmUiIds.Nodes[TinyFarmUiNode.AgentProperties], columns, visible, colors: TableColors),
            left: 234, top: 218, width: 930, height: 376));
        if (rows.Count == 0)
        {
            nodes.Add(StandardUI.Label("No properties match. Clear the filter or choose All.", 250, 275, 870, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsEmpty],
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        }
        var bar = TinyFarmStatsScrollbar.Geometry(rows.Count, menu.StatsOffset);
        nodes.Add(StandardUI.Panel(1180, 254, 24, 340, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsScrollTrack], style: TinyFarmUiTheme.Panel(0x102A21FF)));
        if (bar.IsVisible)
        {
            nodes.Add(StandardUI.Panel(1180, (int)bar.ThumbRect.Y, 24, (int)bar.ThumbRect.Height, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsScrollThumb], style: TinyFarmUiTheme.Panel(0x839784FF)));
        }
        nodes.Add(StandardUI.Button("Previous", TinyFarmUiActions.Previous, 234, 600, 128,
            disabled: menu.StatsOffset == 0,
            style: TinyFarmUiTheme.Button(disabled: menu.StatsOffset == 0)));
        nodes.Add(StandardUI.Button("Next", TinyFarmUiActions.Next, 1036, 600, 128,
            disabled: menu.StatsOffset + TinyFarmMenus.PageSize >= rows.Count,
            style: TinyFarmUiTheme.Button(disabled: menu.StatsOffset + TinyFarmMenus.PageSize >= rows.Count)));
        int first = rows.Count == 0 ? 0 : menu.StatsOffset + 1;
        nodes.Add(StandardUI.Label($"{first}-{Math.Min(rows.Count, menu.StatsOffset + TinyFarmMenus.PageSize)} / {rows.Count}", 430, 609, 500, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsRange],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label("Wheel / drag scroll  UP/DOWN scroll  LEFT/RIGHT agent  TAB filter  ENTER/ESC finish typing", 66, 642, 1140, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsFooter],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(game.State.Version >= TinyFarmState.CraftingSaveVersion
            ? "A2: Cooking / Fire practice is live; general ability and trait modifiers remain pending."
            : "A1: skill practice and trait/condition modifiers are pending; combat retains its existing rules.", 66, 686, 1140, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StatsBoundary],
            size: TextSize.Sm, color: TinyFarmUiTheme.Accent));
    }

    private static void Inventory(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        TinyFarmMenus menu = game.Menus;
        IReadOnlyList<TinyFarmInventoryRow> rows = menu.Rows(game.State, game.Definitions);
        TinyFarmInventoryRow? selected = rows.SingleOrDefault(row => row.Key == menu.SelectedKey);
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.BagPanel], style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label("POCKETS & EQUIPMENT", 66, 78, 650, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.BagTitle],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label($"{game.State.Actor(TinyFarmIds.Player).Money} coins  /  {rows.Count} matching items  /  world paused", 68, 120, 790, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.BagSummary],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / I or ESC", TinyFarmUiActions.Close, 1022, 80, 190,
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("Inventory", TinyFarmUiActions.Bag, 234, 162, 138,
            style: TinyFarmUiTheme.Button(selected: !menu.EquipmentOnly)));
        nodes.Add(StandardUI.Button("Equipment", TinyFarmUiActions.Equipment, 380, 162, 140,
            style: TinyFarmUiTheme.Button(selected: menu.EquipmentOnly)));
        string search = menu.Search.Length == 0 ? "Click to search..." : menu.Search;
        if (menu.SearchFocused)
        {
            search += " |";
        }
        nodes.Add(StandardUI.Button(search, TinyFarmUiActions.Search, 536, 162, 590,
            style: TinyFarmUiTheme.Button(selected: menu.SearchFocused)));
        nodes.Add(StandardUI.Button("Clear", TinyFarmUiActions.Clear, 1134, 162, 78,
            style: TinyFarmUiTheme.Button()));

        for (int index = 0; index < TinyFarmGame.MenuCategories.Length; index++)
        {
            InventoryCategory category = TinyFarmGame.MenuCategories[index];
            nodes.Add(StandardUI.Button(category.ToString(), TinyFarmUiActions.Categories[category], 66, 218 + index * 39, 152,
                style: TinyFarmUiTheme.Button(selected: menu.Category == category)));
        }

        UiTableColumn[] columns =
        [
            new(Header("Name", InventorySort.Name, menu), 225, TinyFarmUiActions.Sorts[InventorySort.Name]),
            new(Header("Category", InventorySort.Category, menu), 130, TinyFarmUiActions.Sorts[InventorySort.Category]),
            new(Header("Qty", InventorySort.Quantity, menu), 65, TinyFarmUiActions.Sorts[InventorySort.Quantity]),
            new(Header("Value", InventorySort.Value, menu), 90, TinyFarmUiActions.Sorts[InventorySort.Value]),
            new(Header("Equipped", InventorySort.Equipped, menu), 110, TinyFarmUiActions.Sorts[InventorySort.Equipped])
        ];
        UiTableRow[] visible = rows.Skip(menu.Offset).Take(TinyFarmMenus.PageSize).Select(row => new UiTableRow(
            TinyFarmUiIds.InventoryRow(row.Key), [row.Name, row.Category.ToString(), row.Quantity.ToString(), row.Value.ToString(), row.Equipped ? "Yes" : "-"],
            TinyFarmUiActions.SelectRow(row.Key), row.Key == menu.SelectedKey)).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build(TinyFarmUiIds.Nodes[TinyFarmUiNode.InventoryTable], columns, visible, colors: TableColors),
            left: 234, top: 218, width: 620, height: 376));
        if (rows.Count == 0)
        {
            nodes.Add(StandardUI.Label("No items match. Try All or Clear.", 250, 279, 590, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Empty],
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        }
        nodes.Add(StandardUI.Button("Previous", TinyFarmUiActions.Previous, 234, 600, 128,
            disabled: menu.Offset == 0,
            style: TinyFarmUiTheme.Button(disabled: menu.Offset == 0)));
        nodes.Add(StandardUI.Button("Next", TinyFarmUiActions.Next, 726, 600, 128,
            disabled: menu.Offset + TinyFarmMenus.PageSize >= rows.Count,
            style: TinyFarmUiTheme.Button(disabled: menu.Offset + TinyFarmMenus.PageSize >= rows.Count)));
        int first = rows.Count == 0 ? 0 : menu.Offset + 1;
        nodes.Add(StandardUI.Label($"{first}-{Math.Min(rows.Count, menu.Offset + TinyFarmMenus.PageSize)} / {rows.Count}", 400, 609, 300, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Range],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));

        nodes.Add(StandardUI.Panel(878, 218, 334, 376, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemDetails], style: TinyFarmUiTheme.Panel(0x233D31FF)));
        if (selected is not null)
        {
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(selected.Name, 300, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemName],
                foreground: TinyFarmUiTheme.Accent, measurer: measurer), 898, 236));
            nodes.Add(StandardUI.Label(selected.Category.ToString().ToUpperInvariant(), 898, 301, 294, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemCategory],
                size: TextSize.Md, color: TinyFarmUiTheme.Accent));
            nodes.Add(StandardUI.Label($"Quantity: {selected.Quantity}  /  Value: {selected.Value}", 898, 337, 294, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemQuantity],
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(selected.Description, 300, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Description],
                foreground: TinyFarmUiTheme.Text, measurer: measurer), 898, 377));
            if (selected.Slot is EquipmentSlot slot)
            {
                nodes.Add(StandardUI.Label("Slot: " + slot, 898, 491, 294, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemSlot],
                    size: TextSize.Md, color: TinyFarmUiTheme.Text));
                nodes.Add(StandardUI.Button(selected.Equipped ? "Unequip / ENTER" : "Equip / ENTER", TinyFarmUiActions.Primary, 898, 544, 294,
                    disabled: game.State.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 },
                    style: TinyFarmUiTheme.Button(disabled: game.State.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 })));
            }
            else if (selected.Item is ItemId card && game.State.Item(card).TeachesRecipe is not null)
            {
                nodes.Add(StandardUI.Button("Read recipe / ENTER", TinyFarmUiActions.Primary, 898, 544, 294,
                    style: TinyFarmUiTheme.Button()));
            }
            else if (selected.Product is ProductId food && game.Definitions.Items.Any(item => item.Id == food && item.Food is not null))
            {
                nodes.Add(StandardUI.Button("Eat / ENTER", TinyFarmUiActions.Primary, 898, 544, 294,
                    style: TinyFarmUiTheme.Button()));
            }
            else if (selected.Product?.Value == "turnip-broth" && game.State.Slice is not null)
            {
                nodes.Add(StandardUI.Button("Eat broth / ENTER", TinyFarmUiActions.Primary, 898, 544, 294,
                    disabled: game.State.Slice.Health >= 12,
                    style: TinyFarmUiTheme.Button(disabled: game.State.Slice.Health >= 12)));
            }
            else
            {
                nodes.Add(StandardUI.Label("Use it in the world with E / K.", 898, 548, 294, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ItemUse],
                    size: TextSize.Md, color: TinyFarmUiTheme.Text));
            }
        }
        else
        {
            nodes.Add(StandardUI.Label("Select an item to inspect it.", 898, 246, 294, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.NoSelection],
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
        }
        nodes.Add(StandardUI.Label("UP/DOWN select  LEFT/RIGHT category  ENTER use  /  Value is per item; stacks are not unique gear.", 66, 638, 1140, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.BagFooter],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(Short(game.Status, 100), 66, 686, 1150, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.BagStatus],
            size: TextSize.Md, color: TinyFarmUiTheme.Accent));
    }

    private static void Pause(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        nodes.Add(StandardUI.Panel(300, 100, 680, 520, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PausePanel], style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label("A MOMENT AT HOME", 336, 129, 590, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PauseTitle],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("The world waits while you are here.", 338, 175, 590, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PauseSubtitle],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        bool busy = game.SaveInProgress || game.LoadInProgress;
        if (game.Menus.Confirmation is TinyFarmConfirmation confirmation)
        {
            string text = confirmation == TinyFarmConfirmation.Load
                ? "Load your checkpoint? Unsaved progress will be replaced."
                : "Quit TinyFarm? Save first to keep your latest progress.";
            nodes.Add(StandardUI.Place(StandardUI.Paragraph(text, 580, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Confirmation],
                foreground: TinyFarmUiTheme.Accent, measurer: measurer), 338, 250));
            nodes.Add(StandardUI.Button(confirmation == TinyFarmConfirmation.Load ? "Load checkpoint / ENTER" : "Quit / ENTER", TinyFarmUiActions.Confirm, 338, 356, 604,
                disabled: busy,
                style: TinyFarmUiTheme.Button(disabled: busy)));
            nodes.Add(StandardUI.Button("Cancel / ESC", TinyFarmUiActions.Close, 338, 407, 604,
                style: TinyFarmUiTheme.Button()));
        }
        else
        {
            string[] labels = ["Resume", "Inventory & Equipment", "Agent properties", "Save checkpoint", "Load checkpoint", "Quit"];
            var entries = new List<GameMenuEntry>();
            for (int index = 0; index < labels.Length; index++)
            {
                bool disabled = busy || TinyFarmGame.PauseActions[index] == TinyFarmUiActions.Load && !game.MenuSaveAvailable;
                entries.Add(new GameMenuEntry(TinyFarmGame.PauseActions[index].Name, labels[index], disabled, Action: TinyFarmGame.PauseActions[index]));
            }
            GameMenuTemplate.AddButtons(nodes, entries, game.Menus.PauseSelection,
                new GameMenuButtonLayout(338, 217, 604, Height: 34, RowSpacing: 44),
                new GameMenuButtonStyle(Background: 0x29473FFF, SelectedBackground: 0x4A6655FF,
                    Foreground: Gold, DisabledForeground: 0x8A9B8EFF, Border: 0x83978480,
                    SelectedBorder: 0x83978480, SelectedBorderWidth: 1, CornerRadius: 0,
                    HorizontalPadding: 0, Alignment: TextAlignX.Center));
            string checkpoint = game.MenuSaveAvailable ? "Checkpoint available" : "No checkpoint yet / Save to create one";
            nodes.Add(StandardUI.Label(checkpoint + "  /  one local save slot", 338, 496, 604, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Checkpoint],
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
        }
        nodes.Add(StandardUI.Place(StandardUI.Paragraph(game.Status, 600, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PauseStatus],
            foreground: TinyFarmUiTheme.Accent, measurer: measurer), 338, 535));
        nodes.Add(StandardUI.Label("UP/DOWN choose  ENTER confirm  ESC return", 338, 594, 604, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PauseHint],
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
