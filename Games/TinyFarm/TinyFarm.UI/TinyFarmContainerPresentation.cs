using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    private static void Container(List<UiNode> nodes, TinyFarmGame game)
    {
        ActorState chest = game.State.Actor(game.OpenContainer!.Value);
        TinyFarmContainerState data = chest.Agent!.Container!;
        TinyFarmNativeUi.Panel(nodes, "container-panel", 44, 56, 1192, 612, 0x19362BFC);
        Label(nodes, "container-title", chest.Name.ToUpperInvariant(), 66, 78, 900, TextSize.H1, Gold);
        Label(nodes, "container-summary", $"{game.State.Actor(TinyFarmIds.Player).Money} coins / {game.State.Minute % 1440 / 60:00}:{game.State.Minute % 60:00} / world paused", 66, 120, 960);
        Button(nodes, "close", "Close / ESC", 1022, 80, 190);
        Button(nodes, "quantity:one", "Move one", 66, 162, 180, selected: !game.ContainerMoveStack);
        Button(nodes, "quantity:stack", "Move stack", 258, 162, 180, selected: game.ContainerMoveStack);
        long estimated = game.ContainerRows(false).Sum(row => (long)row.Quantity * TinyFarmContainers.SaleValue(row, game.State, game.Definitions));
        string policy = data.Shipping ? $"Daily 09:00 pickup / queued value: {estimated} coins" : "Storage / no automatic sale";
        Label(nodes, "container-policy", policy, 466, 175, 720);
        ContainerTable(nodes, game, true, 66, "YOUR POCKETS > DEPOSIT");
        ContainerTable(nodes, game, false, 658, "CHEST > RETRIEVE");
        string receipt = data.LastCollectionDay == 0 ? "No pickup yet."
            : $"Day {data.LastCollectionDay} pickup: {data.LastCollectionItems} items / +{data.LastCollectionCoins} coins.";
        Label(nodes, "container-receipt", receipt, 66, 575, 1100, color: Gold);
        Wrap(nodes, "container-status", game.Status, 66, 610, 92);
        Label(nodes, "container-help", "Click row to move / LEFT-RIGHT side / UP-DOWN row / ENTER transfer / key and equipped items protected", 66, 684, 1140, TextSize.Sm);
    }

    private static void ContainerTable(List<UiNode> nodes, TinyFarmGame game, bool deposit, int left, string title)
    {
        Label(nodes, deposit ? "container-bag-label" : "container-chest-label", title, left, 220, 550, color: Gold);
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
            Label(nodes, deposit ? "container-bag-empty" : "container-chest-empty", "Empty", left + 16, 302, 400);
        }
        string prefix = deposit ? "page:bag:" : "page:chest:";
        Button(nodes, prefix + "previous", "Previous", left, 530, 128, disabled: offset == 0);
        Button(nodes, prefix + "next", "Next", left + 422, 530, 128, disabled: offset + TinyFarmGame.ContainerPageSize >= rows.Count);
        Label(nodes, deposit ? "container-bag-page" : "container-chest-page", $"{rows.Count} rows", left + 205, 538, 180);
    }
}
