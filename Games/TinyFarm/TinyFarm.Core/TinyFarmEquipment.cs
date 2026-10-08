namespace TinyFarm.Core;

public enum EquipmentSlot
{
    Weapon,
    Tool
}

/// <summary>Player loadout truth. Ownership remains in the existing identity-item containers.</summary>
public sealed record TinyFarmEquipment(ItemId? Weapon, ItemId? Tool);

public static class TinyFarmEquipmentRules
{
    public static EquipmentSlot? Slot(TinyFarmState state, ItemId item)
    {
        return state.Items.SingleOrDefault(candidate => candidate.Id == item)?.EquipmentSlot ?? Slot(item);
    }

    public static EquipmentSlot? Slot(ItemId item)
    {
        if (item == TinyFarmIds.Sword)
        {
            return EquipmentSlot.Weapon;
        }
        if (item == TinyFarmIds.Axe)
        {
            return EquipmentSlot.Tool;
        }
        return null;
    }

    public static TinyFarmEquipment Current(TinyFarmState state)
    {
        // Version 11 and earlier used owned tools implicitly. Preserve that behavior until a loadout edit.
        return state.Equipment ?? new TinyFarmEquipment(
            Owned(state, TinyFarmIds.Sword) ? TinyFarmIds.Sword : null,
            Owned(state, TinyFarmIds.Axe) ? TinyFarmIds.Axe : null);
    }

    public static bool IsEquipped(TinyFarmState state, ItemId item)
    {
        TinyFarmEquipment current = Current(state);
        return Owned(state, item) && (current.Weapon == item || current.Tool == item);
    }

    private static bool Owned(TinyFarmState state, ItemId item)
    {
        return state.Actor(TinyFarmIds.Player).Inventory.Contains(item)
            && state.Items.SingleOrDefault(candidate => candidate.Id == item)?.Owner == TinyFarmIds.Player;
    }
}

public sealed partial class TinyFarmResolver
{
    private static IntentResult ResolveEquipment(TinyFarmState state, ActorState actor,
        IntentEnvelope envelope, SetEquipmentIntent intent)
    {
        if (actor.Id != TinyFarmIds.Player || !actor.IsPlayer || state.Version < TinyFarmState.DungeonCombatSaveVersion)
        {
            return Rejected(envelope, IntentReason.WrongTool);
        }
        if (intent.Slot is not EquipmentSlot.Weapon and not EquipmentSlot.Tool)
        {
            return Rejected(envelope, IntentReason.WrongTool);
        }
        if (intent.Item is ItemId item)
        {
            if (!OwnsItem(state, actor, item))
            {
                return Rejected(envelope, IntentReason.ItemNotOwned);
            }
            if (TinyFarmEquipmentRules.Slot(state, item) != intent.Slot)
            {
                return Rejected(envelope, IntentReason.WrongTool);
            }
        }
        // Do not let opening a menu interrupt an active semantic swing or dodge.
        if (state.Slice is { SwordTicks: > 0 } or { DodgeTicks: > 0 })
        {
            return Rejected(envelope, IntentReason.WrongWeapon);
        }
        TinyFarmEquipment current = TinyFarmEquipmentRules.Current(state);
        TinyFarmEquipment next = intent.Slot == EquipmentSlot.Weapon
            ? current with { Weapon = intent.Item }
            : current with { Tool = intent.Item };
        if (current == next)
        {
            return NoOp(envelope, IntentReason.None);
        }
        state.Equipment = next;
        state.Version = Math.Max(state.Version, TinyFarmState.EquipmentSaveVersion);
        return Accepted(envelope, new GameEvent(GameEventKind.EquipmentChanged, actor.Id, Item: intent.Item));
    }
}
