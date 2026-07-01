namespace Aurelian.World.Units;

public sealed record WorldDocument(
    UnitId RootId,
    IReadOnlyDictionary<UnitId, WorldUnitDescriptor> Units)
{
    public bool TryGetUnit(UnitId id, out WorldUnitDescriptor descriptor) => Units.TryGetValue(id, out descriptor!);
}
