namespace TinyFarm.Core;

public enum TinyFarmHotbarSlotVisualState
{
    Empty,
    Available,
    Unavailable
}

public sealed record TinyFarmPlayerInventoryView(
    string SemanticId,
    string Name,
    int Count);

public sealed record TinyFarmHotbarSlotView(
    HotbarSlotId Slot,
    string? BindingKind,
    string? SemanticId,
    string Label,
    int Count,
    bool IsSelected,
    TinyFarmHotbarSlotVisualState VisualState);

public sealed record TinyFarmPlayerUiView(
    int Money,
    IReadOnlyList<TinyFarmPlayerInventoryView> Inventory,
    IReadOnlyList<TinyFarmHotbarSlotView> Hotbar,
    HotbarSlotId SelectedSlot,
    string? SelectedSemanticId,
    string InteractionHint);

public readonly record struct TinyFarmUiRectangle(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public sealed record TinyFarmPlayerUiLayout(
    IReadOnlyList<TinyFarmUiRectangle> HotbarSlots,
    TinyFarmUiRectangle InventoryPanel);

public static class TinyFarmPlayerUiLayoutEngine
{
    public static TinyFarmPlayerUiLayout Compute(int width, int height, int inventoryRows)
    {
        if (width < 640 || height < 480 || inventoryRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "TinyFarm UI requires at least 640x480 and a non-negative inventory row count.");
        }

        int hudHeight = Math.Clamp(height / 12, 76, 112);
        int gap = 6;
        int slotWidth = Math.Clamp((width - 80 - (gap * 7)) / 8, 64, 112);
        int slotHeight = height >= 900 ? 72 : 60;
        int totalWidth = (slotWidth * HotbarSlotId.Count) + (gap * (HotbarSlotId.Count - 1));
        int left = (width - totalWidth) / 2;
        int top = height - hudHeight - slotHeight - 18;
        TinyFarmUiRectangle[] slots = Enumerable.Range(0, HotbarSlotId.Count)
            .Select(index => new TinyFarmUiRectangle(
                left + (index * (slotWidth + gap)),
                top,
                slotWidth,
                slotHeight))
            .ToArray();

        int panelWidth = Math.Clamp(width / 5, 300, 420);
        int panelHeight = Math.Min(80 + (inventoryRows * 34), height - 210);
        var panel = new TinyFarmUiRectangle(width - panelWidth - 24, 24, panelWidth, panelHeight);
        return new TinyFarmPlayerUiLayout(slots, panel);
    }
}

public static class TinyFarmPlayerUiProjector
{
    public static TinyFarmPlayerUiView Project(
        TinyFarmState state,
        TinyFarmDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definitions);
        if (state.Version < TinyFarmState.PlayerUiSaveVersion)
        {
            throw new InvalidOperationException("Player UI projection requires TinyFarm player-hotbar state.");
        }

        ActorState player = state.Actor(TinyFarmIds.Player);
        TinyFarmPlayerInventoryView[] inventory = state.InventoryStacks
            .Where(stack => stack.Actor == player.Id)
            .Select(stack => new TinyFarmPlayerInventoryView(
                stack.Product.Value,
                definitions.Item(stack.Product).Name,
                stack.Count))
            .Concat(player.Inventory.Select(item => new TinyFarmPlayerInventoryView(
                item.Value,
                state.Item(item).Name,
                1)))
            .OrderBy(item => item.SemanticId, StringComparer.Ordinal)
            .ToArray();

        HotbarSlotId selectedSlot = new(state.SelectedHotbarSlot);
        TinyFarmHotbarSlotView[] hotbar = TinyFarmHotbar.DefaultSlots
            .Select(slot => ProjectSlot(state, definitions, player, slot, selectedSlot))
            .ToArray();
        TinyFarmHotbarSlotView selected = hotbar.Single(slot => slot.Slot == selectedSlot);
        InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(
            state,
            player.Id,
            definitions.Scenes);
        string hint = ProjectInteractionHint(state, selected, target);

        return new TinyFarmPlayerUiView(
            player.Money,
            inventory,
            hotbar,
            selectedSlot,
            selected.SemanticId,
            hint);
    }

    private static string ProjectInteractionHint(
        TinyFarmState state,
        TinyFarmHotbarSlotView selected,
        InteractionTarget? target)
    {
        if (target?.Item is ItemId item)
        {
            return $"Take {state.Item(item).Name} [Interact]";
        }
        if (target?.Plot is FarmPlotId plot
            && state.FarmPlots.Single(candidate => candidate.Id == plot).Crop is null
            && selected.SemanticId == TinyFarmIds.TurnipSeed.Value
            && selected.VisualState == TinyFarmHotbarSlotVisualState.Available)
        {
            return $"Plant {selected.Label} [Use]";
        }
        if (selected.BindingKind is null)
        {
            return "Selected slot is empty";
        }
        return selected.VisualState == TinyFarmHotbarSlotVisualState.Available
            ? $"Selected {selected.Label}"
            : $"Selected {selected.Label} (none owned)";
    }

    private static TinyFarmHotbarSlotView ProjectSlot(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        ActorState player,
        HotbarSlot slot,
        HotbarSlotId selectedSlot)
    {
        if (slot.Binding is not ProductHotbarBinding product)
        {
            return new TinyFarmHotbarSlotView(
                slot.Id,
                null,
                null,
                "Empty",
                0,
                slot.Id == selectedSlot,
                TinyFarmHotbarSlotVisualState.Empty);
        }

        int count = state.ProductCount(player.Id, product.Product);
        return new TinyFarmHotbarSlotView(
            slot.Id,
            "Product",
            product.Product.Value,
            definitions.Item(product.Product).Name,
            count,
            slot.Id == selectedSlot,
            count > 0
                ? TinyFarmHotbarSlotVisualState.Available
                : TinyFarmHotbarSlotVisualState.Unavailable);
    }
}

public enum TinyFarmUiKey
{
    Number1,
    Number2,
    Number3,
    Number4,
    Number5,
    Number6,
    Number7,
    Number8,
    Inventory,
    PausePlay,
    FastForward,
    Wait,
    UseSelected
}

public sealed class TinyFarmPlayerUiController
{
    private readonly TinyFarmSimulationHost host;

    public TinyFarmPlayerUiController(TinyFarmSimulationHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool InventoryOpen { get; private set; }

    public bool SuppressWorldMovement => InventoryOpen;

    public void HandleKey(TinyFarmUiKey key)
    {
        switch (key)
        {
            case >= TinyFarmUiKey.Number1 and <= TinyFarmUiKey.Number8:
                SelectSlot(new HotbarSlotId((int)key - (int)TinyFarmUiKey.Number1 + 1));
                break;
            case TinyFarmUiKey.Inventory:
                InventoryOpen = !InventoryOpen;
                host.SetPlayerMovement(0, 0);
                break;
            case TinyFarmUiKey.PausePlay:
                host.Execute(new SetSimulationModeCommand(
                    host.Mode == TinyFarmSimulationMode.Paused
                        ? TinyFarmSimulationMode.Playing
                        : TinyFarmSimulationMode.Paused));
                break;
            case TinyFarmUiKey.FastForward:
                host.Execute(new SetSimulationModeCommand(
                    host.Mode == TinyFarmSimulationMode.FastForward
                        ? TinyFarmSimulationMode.Playing
                        : TinyFarmSimulationMode.FastForward));
                break;
            case TinyFarmUiKey.Wait:
                if (!InventoryOpen && host.Mode != TinyFarmSimulationMode.Paused)
                {
                    host.ExecuteIntent(new WaitIntent(1));
                }
                break;
            case TinyFarmUiKey.UseSelected:
                if (!InventoryOpen && host.Mode != TinyFarmSimulationMode.Paused)
                {
                    host.ExecuteIntent(new UseSelectedIntent());
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown TinyFarm UI key.");
        }
    }

    public void ClickSlot(HotbarSlotId slot)
    {
        SelectSlot(slot);
    }

    private void SelectSlot(HotbarSlotId slot)
    {
        host.ExecuteIntent(new SelectHotbarSlotIntent(slot));
    }
}
