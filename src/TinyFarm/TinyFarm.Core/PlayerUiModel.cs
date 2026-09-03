namespace TinyFarm.Core;

public readonly record struct HotbarSlotId
{
    public const int Count = 8;

    public HotbarSlotId(int value)
    {
        if (value is < 1 or > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Hotbar slot must be between 1 and {Count}.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public abstract record HotbarBinding;

public sealed record ProductHotbarBinding(ProductId Product) : HotbarBinding;

public sealed record ItemHotbarBinding(ItemId Item) : HotbarBinding;

public sealed record HotbarSlot(HotbarSlotId Id, HotbarBinding? Binding);

public static class TinyFarmHotbar
{
    public static IReadOnlyList<HotbarSlot> DefaultSlots { get; } =
    [
        new(new HotbarSlotId(1), new ProductHotbarBinding(TinyFarmIds.TurnipSeed)),
        new(new HotbarSlotId(2), new ProductHotbarBinding(TinyFarmIds.Turnip)),
        new(new HotbarSlotId(3), new ItemHotbarBinding(TinyFarmIds.Axe)),
        new(new HotbarSlotId(4), null),
        new(new HotbarSlotId(5), null),
        new(new HotbarSlotId(6), null),
        new(new HotbarSlotId(7), null),
        new(new HotbarSlotId(8), null)
    ];
}
