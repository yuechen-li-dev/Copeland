namespace TinyFarm.Core;

/// <summary>Inventory identity, independent of display names, ordering, or UI serialization.</summary>
public abstract record TinyFarmInventoryKey
{
    public abstract string Value { get; }

    public sealed record Item(ItemId Id) : TinyFarmInventoryKey
    {
        public override string Value => Id.Value;
    }

    public sealed record Product(ProductId Id) : TinyFarmInventoryKey
    {
        public override string Value => Id.Value;
    }
}
