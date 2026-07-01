using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public sealed record UnitNameStore(IReadOnlyDictionary<UnitId, UnitName> Names)
{
    public static UnitNameStore Empty { get; } = new(new Dictionary<UnitId, UnitName>());

    public bool TryGet(UnitId unitId, out UnitName name) => Names.TryGetValue(unitId, out name!);

    public UnitNameStore Set(UnitId unitId, UnitName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Dictionary<UnitId, UnitName> names = CloneNames();
        names[unitId] = name;
        return new UnitNameStore(names);
    }

    public UnitNameStore Remove(UnitId unitId)
    {
        Dictionary<UnitId, UnitName> names = CloneNames();
        names.Remove(unitId);
        return new UnitNameStore(names);
    }

    private Dictionary<UnitId, UnitName> CloneNames() =>
        Names
            .OrderBy(x => x.Key.Value)
            .ToDictionary(x => x.Key, x => x.Value);
}
