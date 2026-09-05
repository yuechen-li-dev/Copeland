using Aurelian.Spatial2D;

namespace TinyFarm.Core;

public static class TinyFarmSpatialWorldAdapter
{
    public static SpatialWorld2D BuildStaticWorld(SceneDefinition scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SpatialCollider2D[] colliders = scene.Layout
            .Where(row => scene.Object(row.ObjectId).BlocksMovement)
            .Select(row => new SpatialCollider2D(
                new SpatialColliderId(
                    $"tile-object:{row.ObjectId.Value}:{row.X}:{row.Y}:{row.Width}:{row.Height}"),
                SpatialWorldAuthoring2D.TileRectangle(
                    row.X,
                    row.Y,
                    row.Width,
                    row.Height,
                    ScenePosition.UnitsPerTile),
                SemanticOwnerId: row.ObjectId.Value))
            .OrderBy(collider => collider.Id)
            .ToArray();
        return new SpatialWorld2D(colliders);
    }

    public static Circle2 ActorPoint(ScenePosition position)
    {
        return new Circle2(
            new SpatialPoint2D(position.XUnits, position.YUnits),
            0);
    }
}
