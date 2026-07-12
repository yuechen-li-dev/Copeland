namespace Aurelian.World.Units;

public sealed record WorldUnitDescriptor(
    UnitId Id,
    UnitKindId Kind,
    UnitComposition Composition,
    UnitLogicRef? Logic = null,
    string? Name = null);
