using InputMan.Core;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

public sealed partial class TinyFarmGame
{
    public ActorId? OpenContainer { get; private set; }
    public bool ContainerMoveStack { get; private set; }
    public bool ContainerDepositSide { get; private set; } = true;
    public int ContainerPlayerOffset { get; private set; }
    public int ContainerChestOffset { get; private set; }
    public int ContainerSelection { get; private set; }
    public const int ContainerPageSize = 7;

    public IReadOnlyList<TinyFarmInventoryRow> ContainerRows(bool deposit)
    {
        ActorId owner = deposit ? TinyFarmIds.Player : OpenContainer ?? TinyFarmIds.Player;
        return TinyFarmInventory.Project(State, Definitions, owner)
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
    }

    public string ContainerCacheKey => OpenContainer + "|" + ContainerMoveStack + "|" + ContainerDepositSide
        + "|" + ContainerPlayerOffset + "|" + ContainerChestOffset + "|" + ContainerSelection
        + "|" + string.Join(";", ContainerRows(false).Select(row => row.Key + "=" + row.Quantity));

    public bool ContainerRowProtected(TinyFarmInventoryRow row)
    {
        return row.Equipped || row.Item is ItemId item && TinyFarmContainers.IsKey(State.Item(item))
            || row.Product is ProductId product && Definitions.Item(product).IsKeyItem;
    }

    public void HandleContainerAction(string action)
    {
        if (action == "quantity:one" || action == "quantity:stack")
        {
            ContainerMoveStack = action == "quantity:stack";
            return;
        }
        if (action.StartsWith("page:", StringComparison.Ordinal))
        {
            bool deposit = action.StartsWith("page:bag:", StringComparison.Ordinal);
            int direction = action.EndsWith("next", StringComparison.Ordinal) ? 1 : -1;
            int offset = deposit ? ContainerPlayerOffset : ContainerChestOffset;
            int maximum = Math.Max(0, (ContainerRows(deposit).Count - 1) / ContainerPageSize * ContainerPageSize);
            offset = Math.Clamp(offset + direction * ContainerPageSize, 0, maximum);
            if (deposit)
            {
                ContainerPlayerOffset = offset;
            }
            else
            {
                ContainerChestOffset = offset;
            }
            ContainerSelection = 0;
            return;
        }
        bool put = action.StartsWith("deposit:", StringComparison.Ordinal);
        bool take = action.StartsWith("withdraw:", StringComparison.Ordinal);
        if ((!put && !take) || OpenContainer is not ActorId chest)
        {
            return;
        }
        string key = action[(put ? 8 : 9)..];
        TinyFarmInventoryRow? row = ContainerRows(put).FirstOrDefault(value => value.Key == key);
        if (row is null)
        {
            return;
        }
        Execute(new TransferContainerIntent(chest, put, ContainerMoveStack ? row.Quantity : 1, row.Item, row.Product));
        ContainerPlayerOffset = Math.Min(ContainerPlayerOffset, Math.Max(0, (ContainerRows(true).Count - 1) / ContainerPageSize * ContainerPageSize));
        ContainerChestOffset = Math.Min(ContainerChestOffset, Math.Max(0, (ContainerRows(false).Count - 1) / ContainerPageSize * ContainerPageSize));
        int sourceOffset = put ? ContainerPlayerOffset : ContainerChestOffset;
        int visibleCount = ContainerRows(put).Skip(sourceOffset).Take(ContainerPageSize).Count();
        ContainerSelection = Math.Min(ContainerSelection, Math.Max(0, visibleCount - 1));
    }

    private void HandleContainerInput(InputFrame input)
    {
        if (input.WasPressed(GameControls.UiLeft) || input.WasPressed(GameControls.UiRight))
        {
            ContainerDepositSide = input.WasPressed(GameControls.UiLeft);
            ContainerSelection = 0;
        }
        int offset = ContainerDepositSide ? ContainerPlayerOffset : ContainerChestOffset;
        TinyFarmInventoryRow[] rows = ContainerRows(ContainerDepositSide).Skip(offset).Take(ContainerPageSize).ToArray();
        if (input.WasPressed(GameControls.UiUp))
        {
            ContainerSelection = Math.Max(0, ContainerSelection - 1);
        }
        if (input.WasPressed(GameControls.UiDown))
        {
            ContainerSelection = Math.Min(Math.Max(0, rows.Length - 1), ContainerSelection + 1);
        }
        if (input.WasPressed(GameControls.UiConfirm) && ContainerSelection < rows.Length)
        {
            string prefix = ContainerDepositSide ? "deposit:" : "withdraw:";
            HandleContainerAction(prefix + rows[ContainerSelection].Key);
        }
    }

    private void RestoreContainerPresentation()
    {
        ActorState? chest = State.Actors.FirstOrDefault(actor => actor.Agent?.Container?.OpenedBy == TinyFarmIds.Player);
        OpenContainer = chest?.Id;
        if (chest is not null)
        {
            Screen = TinyFarmScreen.Container;
            ContainerPlayerOffset = 0;
            ContainerChestOffset = 0;
            ContainerSelection = 0;
        }
    }

    private void ApplyContainerFeedback(IntentResult result)
    {
        GameEvent? opened = result.Events.FirstOrDefault(value => value.Kind == GameEventKind.ContainerOpened);
        if (opened?.Target is ActorId chest)
        {
            OpenContainer = chest;
            Screen = TinyFarmScreen.Container;
            ContainerDepositSide = true;
            ContainerPlayerOffset = 0;
            ContainerChestOffset = 0;
            ContainerSelection = 0;
            bool shipping = State.Actor(chest).Agent!.Container!.Shipping;
            Status = shipping
                ? "Click a row to transfer. Key items stay with you. Shipping pickup: 09:00 daily."
                : "Click a row to transfer. Key items stay with you. This is storage; nothing is sold.";
        }
        else if (result.Events.Any(value => value.Kind == GameEventKind.ContainerClosed))
        {
            OpenContainer = null;
            Screen = TinyFarmScreen.Playing;
            Status = State.Actor(result.Events.First(value => value.Kind == GameEventKind.ContainerClosed).Target!.Value).Agent!.Container!.Shipping
                ? "Chest closed. Shipping pickup is at 09:00; later deposits wait until tomorrow."
                : "Chest closed. Your items remain in storage.";
        }
        else if (result.Envelope.Intent is TransferContainerIntent transfer)
        {
            Status = result.Reason switch
            {
                IntentReason.KeyItemProtected => "Key items cannot be transferred.",
                IntentReason.EquippedItemProtected => "Unequip this item in I before transferring it.",
                IntentReason.InventoryFull => "Destination inventory is full. Nothing moved.",
                _ when result.Status == IntentResultStatus.Rejected => "Transfer rejected: " + result.Reason,
                _ => (transfer.Deposit ? "Deposited " : "Retrieved ") + transfer.Count + "."
            };
        }
        GameEvent? shipment = result.Events.FirstOrDefault(value => value.Kind == GameEventKind.ShipmentCollected);
        if (shipment is not null)
        {
            Status = $"Shipping collected: +{shipment.Amount} coins. Your chest receipt shows the latest pickup.";
        }
    }
}
