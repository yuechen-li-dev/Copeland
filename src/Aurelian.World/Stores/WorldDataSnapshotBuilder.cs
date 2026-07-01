using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public static class WorldDataSnapshotBuilder
{
    public static WorldDataSnapshot Create(WorldDataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        WorldResolutionResult resolution = WorldUnitResolver.Resolve(document.World);
        if (!resolution.Success || resolution.World is null)
        {
            throw new InvalidOperationException(
                "World data snapshots require a world document that resolves successfully.");
        }

        UnitDataSnapshot[] units = resolution.World.PreOrder
            .Select(id => CreateUnitSnapshot(document, resolution.World, id))
            .ToArray();

        return new WorldDataSnapshot(resolution.World, units);
    }

    private static UnitDataSnapshot CreateUnitSnapshot(
        WorldDataDocument document,
        ResolvedWorld world,
        UnitId id)
    {
        ResolvedWorldUnit unit = world.Units[id];
        document.Names.TryGet(id, out UnitName? name);
        document.Renderables.TryGet(id, out Renderable2DData? renderable);

        return new UnitDataSnapshot(
            unit.Id,
            unit.Kind,
            unit.Logic,
            name,
            document.Transforms.GetOrIdentity(id),
            renderable,
            world.GetImmediateChildren(id));
    }
}
