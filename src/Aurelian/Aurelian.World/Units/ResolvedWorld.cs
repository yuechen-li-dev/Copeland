namespace Aurelian.World.Units;

public sealed record ResolvedWorld(
    UnitId RootId,
    IReadOnlyDictionary<UnitId, ResolvedWorldUnit> Units,
    IReadOnlyDictionary<UnitId, IReadOnlyList<UnitId>> Children,
    IReadOnlyDictionary<UnitId, UnitId> Parents,
    IReadOnlyList<UnitId> PreOrder)
{
    public IReadOnlyList<UnitId> GetImmediateChildren(UnitId id) =>
        Children.TryGetValue(id, out IReadOnlyList<UnitId>? children) ? children : Array.Empty<UnitId>();

    public IReadOnlyList<UnitId> GetTransitiveDescendants(UnitId id)
    {
        if (!Children.ContainsKey(id))
        {
            return Array.Empty<UnitId>();
        }

        List<UnitId> descendants = [];
        AppendDescendants(id, descendants);
        return descendants;
    }

    public bool TryGetParent(UnitId id, out UnitId parent) => Parents.TryGetValue(id, out parent);

    public bool Contains(UnitId id) => Units.ContainsKey(id);

    private void AppendDescendants(UnitId id, List<UnitId> descendants)
    {
        foreach (UnitId child in GetImmediateChildren(id))
        {
            descendants.Add(child);
            AppendDescendants(child, descendants);
        }
    }
}
