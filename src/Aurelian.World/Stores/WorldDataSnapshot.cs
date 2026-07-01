using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public sealed record WorldDataSnapshot(
    ResolvedWorld World,
    IReadOnlyList<UnitDataSnapshot> Units);
