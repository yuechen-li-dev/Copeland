namespace TinyFarm.Core;

/// <summary>Agent-local behavior; contents remain in Actor.Inventory and InventoryStacks.</summary>
public sealed record TinyFarmContainerState(
    bool Shipping = false,
    ActorId? Beneficiary = null,
    ActorId? OpenedBy = null,
    int LastCollectionDay = 0,
    int LastCollectionCoins = 0,
    int LastCollectionItems = 0);

public sealed record OpenContainerIntent(ActorId Container) : GameIntent;
public sealed record CloseContainerIntent(ActorId Container) : GameIntent;
public sealed record TransferContainerIntent(ActorId Container, bool Deposit, int Count = 1,
    ItemId? Item = null, ProductId? Product = null) : GameIntent;

public static class TinyFarmContainers
{
    public const int CollectionMinute = 9 * 60;

    public static bool IsKey(ItemState item)
    {
        // Legacy delivery letters predate explicit item metadata.
        return item.IsKeyItem || item.Id == TinyFarmIds.Letter || item.TeachesRecipe is not null;
    }

    public static int SaleValue(TinyFarmInventoryRow row, TinyFarmState state, TinyFarmDefinitions definitions)
    {
        if (row.Item is ItemId item && IsKey(state.Item(item)))
        {
            return 0;
        }
        if (row.Product is ProductId product && definitions.Item(product).IsKeyItem)
        {
            return 0;
        }
        return Math.Max(0, row.Value);
    }
}

public sealed partial class TinyFarmResolver
{
    private static bool CanReachContainer(TinyFarmState state, ActorId actor, ActorId container)
    {
        ActorSceneState? player = state.ActorScenes.FirstOrDefault(value => value.Actor == actor);
        ActorSceneState? chest = state.ActorScenes.FirstOrDefault(value => value.Actor == container);
        return player is not null && chest is not null && player.Scene == chest.Scene
            && player.WorldPosition.SquaredDistance(chest.WorldPosition)
                <= (long)TinyFarmSpatialQueries.InteractionRangeUnits * TinyFarmSpatialQueries.InteractionRangeUnits;
    }

    private IntentResult ResolveOpenContainer(TinyFarmState state, ActorState actor, IntentEnvelope envelope, ActorId id)
    {
        ActorState? chest = FindActor(state, id);
        if (state.Version < TinyFarmState.ContainerSaveVersion || chest?.Agent?.Container is not { } container
            || !TinyFarmAgentPolicy.IsObject(chest) || id == actor.Id)
        {
            return Rejected(envelope, IntentReason.ContainerUnavailable);
        }
        if (!CanReachContainer(state, actor.Id, id))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }
        if (container.OpenedBy is ActorId opener && opener != actor.Id
            || state.Actors.Any(candidate => candidate.Id != id && candidate.Agent?.Container?.OpenedBy == actor.Id))
        {
            return Rejected(envelope, IntentReason.ContainerUnavailable);
        }
        ReplaceActor(state, chest with { Agent = chest.Agent with
        {
            ObjectPose = TinyFarmObjectPose.Open,
            Container = container with { OpenedBy = actor.Id }
        } });
        return Accepted(envelope, new GameEvent(GameEventKind.ContainerOpened, actor.Id, Target: id));
    }

    private IntentResult ResolveCloseContainer(TinyFarmState state, ActorState actor, IntentEnvelope envelope, ActorId id)
    {
        ActorState? chest = FindActor(state, id);
        if (chest?.Agent?.Container is not { } container || container.OpenedBy != actor.Id)
        {
            return Rejected(envelope, IntentReason.ContainerClosed);
        }
        ReplaceActor(state, chest with { Agent = chest.Agent with
        {
            ObjectPose = TinyFarmObjectPose.Closed,
            Container = container with { OpenedBy = null }
        } });
        return Accepted(envelope, new GameEvent(GameEventKind.ContainerClosed, actor.Id, Target: id));
    }

    private IntentResult ResolveContainerTransfer(TinyFarmState state, ActorState actor,
        IntentEnvelope envelope, TransferContainerIntent intent)
    {
        ActorState? chest = FindActor(state, intent.Container);
        if (definitions is null || state.Version < TinyFarmState.ContainerSaveVersion
            || chest?.Agent?.Container is not { } container || intent.Container == actor.Id)
        {
            return Rejected(envelope, IntentReason.ContainerUnavailable);
        }
        if (container.OpenedBy != actor.Id || chest.Agent.ObjectPose != TinyFarmObjectPose.Open)
        {
            return Rejected(envelope, IntentReason.ContainerClosed);
        }
        if (!CanReachContainer(state, actor.Id, chest.Id))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }
        if (intent.Count <= 0 || (intent.Item is null) == (intent.Product is null))
        {
            return Rejected(envelope, IntentReason.InvalidTransfer);
        }
        ActorState source = intent.Deposit ? actor : chest;
        ActorState destination = intent.Deposit ? chest : actor;
        if (intent.Item is ItemId id)
        {
            ItemState? item = FindItem(state, id);
            if (item is null || item.Owner != source.Id || !source.Inventory.Contains(id))
            {
                return Rejected(envelope, IntentReason.ItemNotOwned);
            }
            if (intent.Count != 1)
            {
                return Rejected(envelope, IntentReason.InvalidTransfer);
            }
            if (TinyFarmContainers.IsKey(item))
            {
                return Rejected(envelope, IntentReason.KeyItemProtected);
            }
            bool equipped = source.Agent?.Equipment.Weapon == id || source.Agent?.Equipment.Tool == id
                || source.Id == TinyFarmIds.Player && TinyFarmEquipmentRules.IsEquipped(state, id);
            if (equipped)
            {
                return Rejected(envelope, IntentReason.EquippedItemProtected);
            }
            ReplaceActor(state, source with { Inventory = source.Inventory.Where(value => value != id).ToList() });
            ReplaceActor(state, destination with { Inventory = destination.Inventory.Append(id).ToList() });
            ReplaceItem(state, item with { Owner = destination.Id });
        }
        else if (intent.Product is ProductId product)
        {
            ItemDefinition? definition = definitions.Items.FirstOrDefault(value => value.Id == product);
            if (definition is null)
            {
                return Rejected(envelope, IntentReason.UnknownItem);
            }
            if (definition.IsKeyItem)
            {
                return Rejected(envelope, IntentReason.KeyItemProtected);
            }
            if (state.ProductCount(source.Id, product) < intent.Count)
            {
                return Rejected(envelope, IntentReason.ItemAbsent);
            }
            if ((long)state.ProductCount(destination.Id, product) + intent.Count > TinyFarmCrafting.StackLimit
                || state.ProductCount(destination.Id, product) == 0
                && state.InventoryStacks.Count(stack => stack.Actor == destination.Id) >= TinyFarmCrafting.BagSlots)
            {
                return Rejected(envelope, IntentReason.InventoryFull);
            }
            SetProductCount(state, source.Id, product, state.ProductCount(source.Id, product) - intent.Count);
            SetProductCount(state, destination.Id, product, state.ProductCount(destination.Id, product) + intent.Count);
        }
        return Accepted(envelope, new GameEvent(GameEventKind.ContainerTransferred, actor.Id,
            Target: chest.Id, Item: intent.Item, Product: intent.Product, Amount: intent.Count));
    }

    /// <summary>Clock owner calls this after every accepted time jump, including rest.</summary>
    private void CollectShipments(TinyFarmState state, List<GameEvent> events)
    {
        if (definitions is null || state.Version < TinyFarmState.ContainerSaveVersion)
        {
            return;
        }
        int latestDay = state.Day;
        if (state.Minute % 1440 < TinyFarmContainers.CollectionMinute)
        {
            latestDay--;
        }
        foreach (ActorId id in state.Actors.Where(value => value.Agent?.Container?.Shipping == true)
            .Select(value => value.Id).OrderBy(value => value.Value, StringComparer.Ordinal).ToArray())
        {
            ActorState chest = state.Actor(id);
            TinyFarmContainerState container = chest.Agent!.Container!;
            if (latestDay <= container.LastCollectionDay || container.Beneficiary is not ActorId beneficiary)
            {
                continue;
            }
            TinyFarmInventoryRow[] sellable = TinyFarmInventory.Project(state, definitions, id)
                .Where(row => TinyFarmContainers.SaleValue(row, state, definitions) > 0).ToArray();
            long value = sellable.Sum(row => (long)row.Quantity * row.Value);
            ActorState recipient = state.Actor(beneficiary);
            long shipmentCount = sellable.Sum(row => (long)row.Quantity);
            if (value > int.MaxValue - (long)recipient.Money || shipmentCount > int.MaxValue)
            {
                // This pickup has happened, but contents must stay until a future pickup can pay safely.
                ReplaceActor(state, chest with { Agent = chest.Agent with { Container = container with
                {
                    LastCollectionDay = latestDay, LastCollectionCoins = 0, LastCollectionItems = 0
                } } });
                continue;
            }
            int count = (int)shipmentCount;
            foreach (TinyFarmInventoryRow row in sellable)
            {
                if (row.Product is ProductId product)
                {
                    SetProductCount(state, id, product, 0);
                }
                else if (row.Item is ItemId item)
                {
                    state.MutableItems.RemoveAll(candidate => candidate.Id == item);
                }
            }
            ReplaceActor(state, recipient with { Money = recipient.Money + (int)value });
            ReplaceActor(state, chest with
            {
                Inventory = chest.Inventory.Where(item => !sellable.Any(row => row.Item == item)).ToList(),
                Agent = chest.Agent with { Container = container with
                {
                    LastCollectionDay = latestDay,
                    LastCollectionCoins = (int)value,
                    LastCollectionItems = count
                } }
            });
            if (value > 0)
            {
                if (sellable.Any(row => definitions.Crops.Any(crop => crop.HarvestItemId == row.Product)))
                {
                    AddFact(state, WorldFact.FirstCropSold);
                }
                events.Add(new GameEvent(GameEventKind.ShipmentCollected, beneficiary, Target: id, Amount: (int)value));
            }
        }
    }
}
