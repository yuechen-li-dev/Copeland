namespace Aurelian.World.Units;

public sealed record ResolvedWorldUnit(
    UnitId Id,
    UnitKindId Kind,
    UnitLogicRef? Logic);
