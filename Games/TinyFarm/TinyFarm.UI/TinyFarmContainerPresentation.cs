using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    private static void Container(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        ActorState chest = game.State.Actor(game.OpenContainer!.Value);
        TinyFarmContainerState data = chest.Agent!.Container!;
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: "container-panel", style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label(chest.Name.ToUpperInvariant(), 66, 78, 900, id: "container-title",
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label($"{game.State.Actor(TinyFarmIds.Player).Money} coins / {game.State.Minute % 1440 / 60:00}:{game.State.Minute % 60:00} / world paused", 66, 120, 960, id: "container-summary",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / ESC", 1022, 80, 190, id: "menu.close",
            action: UiAction.Named("close"),
            style: TinyFarmUiTheme.Button()));
        nodes.Add(StandardUI.Button("Move one", 66, 162, 180, id: "menu.quantity:one",
            action: UiAction.Named("quantity:one"),
            style: TinyFarmUiTheme.Button(selected: !game.ContainerMoveStack)));
        nodes.Add(StandardUI.Button("Move stack", 258, 162, 180, id: "menu.quantity:stack",
            action: UiAction.Named("quantity:stack"),
            style: TinyFarmUiTheme.Button(selected: game.ContainerMoveStack)));
        long estimated = game.ContainerRows(false).Sum(row => (long)row.Quantity * TinyFarmContainers.SaleValue(row, game.State, game.Definitions));
        string policy = data.Shipping ? $"Daily 09:00 pickup / queued value: {estimated} coins" : "Storage / no automatic sale";
        nodes.Add(StandardUI.Label(policy, 466, 175, 720, id: "container-policy",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        ContainerTable(nodes, game, true, 66, "YOUR POCKETS > DEPOSIT");
        ContainerTable(nodes, game, false, 658, "CHEST > RETRIEVE");
        string receipt = data.LastCollectionDay == 0 ? "No pickup yet."
            : $"Day {data.LastCollectionDay} pickup: {data.LastCollectionItems} items / +{data.LastCollectionCoins} coins.";
        nodes.Add(StandardUI.Label(receipt, 66, 575, 1100, id: "container-receipt",
            size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Place(StandardUI.Paragraph(game.Status, 920, id: "container-status",
            foreground: TinyFarmUiTheme.Text, measurer: measurer), 66, 610));
        nodes.Add(StandardUI.Label("Click row to move / LEFT-RIGHT side / UP-DOWN row / ENTER transfer / key and equipped items protected", 66, 684, 1140, id: "container-help",
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
    }

    private static void ContainerTable(List<UiNode> nodes, TinyFarmGame game, bool deposit, int left, string title)
    {
        nodes.Add(StandardUI.Label(title, left, 220, 550, id: deposit ? "container-bag-label" : "container-chest-label",
            size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        IReadOnlyList<TinyFarmInventoryRow> rows = game.ContainerRows(deposit);
        int offset = deposit ? game.ContainerPlayerOffset : game.ContainerChestOffset;
        UiTableColumn[] columns = [new("Name", 270), new("Qty", 65), new("Value", 75), new("Status", 140)];
        UiTableRow[] visible = rows.Skip(offset).Take(TinyFarmGame.ContainerPageSize).Select((row, index) =>
        {
            bool locked = game.ContainerRowProtected(row);
            string status = "Transfer";
            if (row.Equipped)
            {
                status = "Equipped";
            }
            else if (locked)
            {
                status = "Key item";
            }
            string action = (deposit ? "deposit:" : "withdraw:") + row.Key;
            return new UiTableRow(row.Key,
                [row.Name, row.Quantity.ToString(), row.Value.ToString(), status],
                locked ? null : UiAction.Named(action),
                game.ContainerDepositSide == deposit && game.ContainerSelection == index);
        }).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build(deposit ? "container-bag" : "container-chest", columns, visible, colors: TableColors),
            left: left, top: 254, width: 550, height: 308));
        if (rows.Count == 0)
        {
            nodes.Add(StandardUI.Label("Empty", left + 16, 302, 400, id: deposit ? "container-bag-empty" : "container-chest-empty",
                size: TextSize.Md, color: TinyFarmUiTheme.Text));
        }
        string prefix = deposit ? "page:bag:" : "page:chest:";
        nodes.Add(StandardUI.Button("Previous", left, 530, 128, id: "menu." + prefix + "previous",
            action: UiAction.Named(prefix + "previous"), disabled: offset == 0,
            style: TinyFarmUiTheme.Button(disabled: offset == 0)));
        nodes.Add(StandardUI.Button("Next", left + 422, 530, 128, id: "menu." + prefix + "next",
            action: UiAction.Named(prefix + "next"), disabled: offset + TinyFarmGame.ContainerPageSize >= rows.Count,
            style: TinyFarmUiTheme.Button(disabled: offset + TinyFarmGame.ContainerPageSize >= rows.Count)));
        nodes.Add(StandardUI.Label($"{rows.Count} rows", left + 205, 538, 180, id: deposit ? "container-bag-page" : "container-chest-page",
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
    }
}
