using InputMan.Core;
using TinyFarm.Core;
using static TinyFarm.InputMan.TinyFarmMenuMessage;

namespace TinyFarm.InputMan;

public sealed partial class TinyFarmGame
{
    private TinyFarmContainerViewState ContainerState => UiScope.GetOrCreate(TinyFarmUiStateKeys.Container, () => new TinyFarmContainerViewState());
    public ActorId? OpenContainer
    {
        get => ContainerState.Container;
        private set => ContainerState.Container = value;
    }

    public bool ContainerMoveStack
    {
        get => ContainerState.MoveStack;
        private set => ContainerState.MoveStack = value;
    }

    public bool ContainerDepositSide
    {
        get => ContainerState.DepositSide;
        private set => ContainerState.DepositSide = value;
    }

    public int ContainerPlayerOffset
    {
        get => ContainerState.PlayerOffset;
        private set => ContainerState.PlayerOffset = value;
    }

    public int ContainerChestOffset
    {
        get => ContainerState.ChestOffset;
        private set => ContainerState.ChestOffset = value;
    }

    public int ContainerSelection
    {
        get => ContainerState.Selection;
        private set => ContainerState.Selection = value;
    }

    public const int ContainerPageSize = 7;

    public IReadOnlyList<TinyFarmInventoryRow> ContainerRows(bool deposit)
    {
        ActorId owner = deposit ? TinyFarmIds.Player : OpenContainer ?? TinyFarmIds.Player;
        return TinyFarmInventory.Project(State, Definitions, owner)
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Key is TinyFarmInventoryKey.Product)
            .ThenBy(row => row.Key.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public bool ContainerRowProtected(TinyFarmInventoryRow row)
    {
        return row.Equipped || row.Item is ItemId item && TinyFarmContainers.IsKey(State.Item(item))
            || row.Product is ProductId product && Definitions.Item(product).IsKeyItem;
    }

    private void HandleContainerAction(TinyFarmMenuMessage action)
    {
        if (action is MoveStack(var moveStack))
        {
            ContainerMoveStack = moveStack;
            return;
        }
        if (action is ContainerPage(var side, var direction) && Enum.IsDefined(side) && direction is -1 or 1)
        {
            bool deposit = side == TinyFarmContainerSide.Bag;
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
        if (action is not Transfer(var source, var key) || !Enum.IsDefined(source) || OpenContainer is not ActorId chest)
        {
            return;
        }
        bool put = source == TinyFarmContainerSide.Bag;
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
            TinyFarmContainerSide side = ContainerDepositSide ? TinyFarmContainerSide.Bag : TinyFarmContainerSide.Chest;
            DispatchMenu(TinyFarmUiActions.Transfer(side, rows[ContainerSelection].Key));
        }
    }

    private void RestoreContainerPresentation()
    {
        ActorState? chest = State.Actors.FirstOrDefault(actor => actor.Agent?.Container?.OpenedBy == TinyFarmIds.Player);
        if (chest is not null)
        {
            uiFlow.Push(TinyFarmScreen.Container);
            OpenContainer = chest.Id;
        }
    }

    private void ApplyContainerFeedback(IntentResult result)
    {
        GameEvent? opened = result.Events.FirstOrDefault(value => value.Kind == GameEventKind.ContainerOpened);
        if (opened?.Target is ActorId chest)
        {
            uiFlow.ReturnTo(TinyFarmScreen.Playing);
            uiFlow.Push(TinyFarmScreen.Container);
            OpenContainer = chest;
            bool shipping = State.Actor(chest).Agent!.Container!.Shipping;
            Status = shipping
                ? "Click a row to transfer. Key items stay with you. Shipping pickup: 09:00 daily."
                : "Click a row to transfer. Key items stay with you. This is storage; nothing is sold.";
        }
        else if (result.Events.Any(value => value.Kind == GameEventKind.ContainerClosed))
        {
            uiFlow.ReturnTo(TinyFarmScreen.Playing);
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
