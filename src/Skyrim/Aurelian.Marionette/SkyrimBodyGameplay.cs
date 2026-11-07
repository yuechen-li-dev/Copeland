namespace Marionette.Skyrim.App;

public enum SkyrimActorValue : uint
{
    OneHanded = 6,
    TwoHanded = 7,
    Archery = 8,
    Block = 9,
    Smithing = 10,
    HeavyArmor = 11,
    LightArmor = 12,
    Pickpocket = 13,
    Lockpicking = 14,
    Sneak = 15,
    Alchemy = 16,
    Speech = 17,
    Alteration = 18,
    Conjuration = 19,
    Destruction = 20,
    Illusion = 21,
    Restoration = 22,
    Enchanting = 23,
    Health = 24,
    Magicka = 25,
    Stamina = 26,
    HealRate = 27,
    MagickaRate = 28,
    StaminaRate = 29,
    SpeedMultiplier = 30,
    InventoryWeight = 31,
    CarryWeight = 32,
    CriticalChance = 33,
    MeleeDamage = 34,
    UnarmedDamage = 35,
    Mass = 36,
    VoicePoints = 37,
    VoiceRate = 38,
    DamageResist = 39,
    PoisonResist = 40,
    ResistFire = 41,
    ResistShock = 42,
    ResistFrost = 43,
    ResistMagic = 44,
}

public enum SkyrimVital : uint
{
    Health = 24,
    Magicka = 25,
    Stamina = 26,
}

public sealed record BodyActorValue(SkyrimActorValue ActorValue, float Current, float Base, float Maximum);
public sealed record BodyInventoryItem(uint FormId, string Name, int Count, bool Worn, bool QuestObject, uint ExtraStacks);
public sealed record BodyQuestStage(uint FormId, uint Stage);
public sealed record BodyGameplayObservation(uint FormId, uint QuestOwner, uint RightHand, uint LeftHand,
    int PlayerGold, float PlayerHealth, bool InventoryComplete, BodyActorValue[] Stats, BodyInventoryItem[] Inventory,
    float HudHealthPercent = 0, float HudMagickaPercent = 0, float HudStaminaPercent = 0,
    uint InventoryMenuOwner = 0, uint[]? InventoryMenuForms = null, BodyQuestStage[]? QuestStages = null, float CarriedWeight = 0);
public sealed record BodyGameplaySnapshot(SkyrimBodyLease Lease, BodyGameplayObservation Observation);

public sealed partial class SkyrimBodyControl
{
    public ValueTask<BodyGameplaySnapshot> ObserveGameplayAsync(CancellationToken cancellationToken = default)
    {
        SkyrimBodyLease current = RequireLease();
        return SendGameplayAsync(GameplayRequest(current, "gameplay"), current, cancellationToken);
    }

    public ValueTask<BodyGameplaySnapshot> EquipAsync(BodyGameplaySnapshot source, uint itemFormId,
        bool unequip = false, CancellationToken cancellationToken = default)
    {
        return ChangeInventoryAsync(source, itemFormId, 1, unequip ? "unequip" : "equip", cancellationToken);
    }

    public ValueTask<BodyGameplaySnapshot> DropAsync(BodyGameplaySnapshot source, uint itemFormId, uint count = 1,
        CancellationToken cancellationToken = default)
    {
        return ChangeInventoryAsync(source, itemFormId, count, "drop", cancellationToken);
    }

    public ValueTask<BodyGameplaySnapshot> DamageVitalAsync(SkyrimVital vital, float amount,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(vital) || !float.IsFinite(amount) || amount is <= 0 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        SkyrimBodyLease current = RequireLease();
        var request = GameplayRequest(current, "damage") with { BodyActorValue = (uint)vital, BodyDamage = amount };
        return SendGameplayAsync(request, current, cancellationToken);
    }

    private ValueTask<BodyGameplaySnapshot> ChangeInventoryAsync(BodyGameplaySnapshot source, uint form, uint count,
        string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        SkyrimBodyLease current = RequireLease();
        if (source.Lease != current || source.Observation.FormId != current.FormId)
        {
            throw new ArgumentException("body_inventory_snapshot_stale", nameof(source));
        }
        BodyInventoryItem item = source.Observation.Inventory.SingleOrDefault(item => item.FormId == form)
            ?? throw new ArgumentException("body_item_not_observed", nameof(form));
        if (count == 0 || count > item.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        bool goldDrop = form == 0xF && action == "drop";
        if (item.QuestObject || item.ExtraStacks > 1 && !goldDrop)
        {
            throw new ArgumentException(item.QuestObject ? "body_quest_item_protected" : "body_item_instance_ambiguous");
        }
        var request = GameplayRequest(current, action) with
        {
            BodyItemFormId = form,
            BodyExpectedItemCount = checked((uint)item.Count),
            BodyItemCount = count,
        };
        return SendGameplayAsync(request, current, cancellationToken);
    }

    private static BodyControlRequest GameplayRequest(SkyrimBodyLease current, string action)
    {
        return new BodyControlRequest(1, "body_control", Guid.NewGuid().ToString("N"), 2000,
            current.WorldGeneration, current.LeaseId, 0, action);
    }

    private async ValueTask<BodyGameplaySnapshot> SendGameplayAsync(BodyControlRequest request, SkyrimBodyLease current,
        CancellationToken cancellationToken)
    {
        BodyControlResult result = await SendResultAsync(request, cancellationToken).ConfigureAwait(false);
        BodyGameplayObservation value = result.Gameplay ?? throw new InvalidDataException("body_gameplay_receipt_missing");
        if (Lease != current || result.WorldGeneration != current.WorldGeneration || value.FormId != current.FormId ||
            value.QuestOwner != 20 || value.Inventory is null || value.Stats is null ||
            value.Inventory.Any(item => item.Count <= 0 || item.FormId == 0) ||
            value.Inventory.Select(item => item.FormId).Distinct().Count() != value.Inventory.Length)
        {
            throw new InvalidDataException("body_gameplay_receipt_invalid");
        }
        return new BodyGameplaySnapshot(current, value);
    }
}
