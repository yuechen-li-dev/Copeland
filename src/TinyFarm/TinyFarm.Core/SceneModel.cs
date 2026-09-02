using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public readonly record struct SceneId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SceneObjectId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SceneRouteId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SceneSpawnId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GridPosition(int X, int Y)
{
    public int ManhattanDistance(GridPosition other)
    {
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    }
}

public readonly record struct ScenePosition(int XUnits, int YUnits)
{
    public const int UnitsPerTile = 1024;

    public static ScenePosition FromGrid(GridPosition position)
    {
        return new ScenePosition(
            checked((position.X * UnitsPerTile) + (UnitsPerTile / 2)),
            checked((position.Y * UnitsPerTile) + (UnitsPerTile / 2)));
    }

    public GridPosition Tile => new(ToTile(XUnits), ToTile(YUnits));

    public long SquaredDistance(ScenePosition other)
    {
        long deltaX = (long)XUnits - other.XUnits;
        long deltaY = (long)YUnits - other.YUnits;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static int ToTile(int units)
    {
        return units >= 0 ? units / UnitsPerTile : -1;
    }
}

public enum ActorFacing
{
    Down,
    Left,
    Right,
    Up
}

public enum SceneObjectKind
{
    Portal,
    Plot,
    Prop,
    Shop,
    Landmark,
    Decoration
}

public sealed record SceneObjectDefinition(
    SceneObjectId Id,
    SceneObjectKind Kind,
    string Label,
    bool BlocksMovement,
    string? SemanticReference = null);

public sealed record SceneLayoutRow(
    SceneObjectId ObjectId,
    int X,
    int Y,
    int Width,
    int Height,
    int Layer)
{
    public bool Contains(GridPosition position)
    {
        return position.X >= X
            && position.X < X + Width
            && position.Y >= Y
            && position.Y < Y + Height;
    }
}

public sealed record SceneSpawnDefinition(SceneSpawnId Id, GridPosition Position);

public sealed record SceneRoute(
    SceneRouteId Id,
    SceneId SourceScene,
    SceneObjectId TriggerObject,
    SceneId TargetScene,
    SceneSpawnId TargetSpawn,
    string InteractionLabel);

public sealed class SceneDefinition
{
    public SceneDefinition(
        SceneId id,
        string name,
        int width,
        int height,
        IEnumerable<SceneObjectDefinition> objects,
        IEnumerable<SceneLayoutRow> layout,
        IEnumerable<SceneSpawnDefinition> spawns,
        IEnumerable<SceneRoute> routes)
    {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Objects = objects.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Layout = layout.OrderBy(item => item.Layer).ThenBy(item => item.ObjectId.Value, StringComparer.Ordinal).ToArray();
        Spawns = spawns.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Routes = routes.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
    }

    public SceneId Id { get; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<SceneObjectDefinition> Objects { get; }
    public IReadOnlyList<SceneLayoutRow> Layout { get; }
    public IReadOnlyList<SceneSpawnDefinition> Spawns { get; }
    public IReadOnlyList<SceneRoute> Routes { get; }

    public SceneObjectDefinition Object(SceneObjectId id)
    {
        return Objects.Single(item => item.Id == id);
    }

    public SceneLayoutRow Placement(SceneObjectId id)
    {
        return Layout.Single(item => item.ObjectId == id);
    }

    public SceneSpawnDefinition Spawn(SceneSpawnId id)
    {
        return Spawns.Single(item => item.Id == id);
    }
}

[method: JsonConstructor]
public sealed record ActorSceneState(
    ActorId Actor,
    SceneId Scene,
    ScenePosition WorldPosition,
    ActorFacing Facing = ActorFacing.Down)
{
    public ActorSceneState(ActorId actor, SceneId scene, GridPosition position)
        : this(actor, scene, ScenePosition.FromGrid(position))
    {
    }

    public GridPosition Position => WorldPosition.Tile;
}

public static class TinyFarmSceneIds
{
    public static readonly SceneId Overworld = new("overworld");
    public static readonly SceneId Farm = new("farm");
    public static readonly SceneId Town = new("town");
    public static readonly SceneId GeneralStore = new("general-store");
    public static readonly SceneId Riverside = new("riverside");
}

public static class TinyFarmScenes
{
    public static IReadOnlyList<SceneDefinition> All { get; } = CreateAndValidate();

    public static SceneDefinition Get(SceneId id)
    {
        return All.Single(scene => scene.Id == id);
    }

    public static SceneId SceneForLocation(LocationId location)
    {
        if (location == TinyFarmIds.Farmhouse)
        {
            return TinyFarmSceneIds.Farm;
        }

        if (location == TinyFarmIds.GeneralStore)
        {
            return TinyFarmSceneIds.GeneralStore;
        }

        if (location == TinyFarmIds.Riverside)
        {
            return TinyFarmSceneIds.Riverside;
        }

        return TinyFarmSceneIds.Town;
    }

    public static LocationId LocationForScene(SceneId scene)
    {
        if (scene == TinyFarmSceneIds.Farm)
        {
            return TinyFarmIds.Farmhouse;
        }

        if (scene == TinyFarmSceneIds.GeneralStore)
        {
            return TinyFarmIds.GeneralStore;
        }

        if (scene == TinyFarmSceneIds.Riverside)
        {
            return TinyFarmIds.Riverside;
        }

        return TinyFarmIds.TownSquare;
    }

    public static void Validate(IEnumerable<SceneDefinition> definitions)
    {
        SceneDefinition[] scenes = definitions.ToArray();
        if (scenes.Select(scene => scene.Id).Distinct().Count() != scenes.Length)
        {
            throw new InvalidDataException("Scene identities must be unique.");
        }

        foreach (SceneDefinition scene in scenes)
        {
            if (scene.Width <= 0 || scene.Height <= 0)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' has invalid bounds.");
            }

            if (scene.Objects.Select(item => item.Id).Distinct().Count() != scene.Objects.Count)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' has duplicate object identities.");
            }

            if (scene.Spawns.Select(item => item.Id).Distinct().Count() != scene.Spawns.Count)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' has duplicate spawn identities.");
            }

            foreach (SceneLayoutRow row in scene.Layout)
            {
                if (!scene.Objects.Any(item => item.Id == row.ObjectId))
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' layout references unknown object '{row.ObjectId}'.");
                }

                if (row.Width <= 0 || row.Height <= 0 || row.X < 0 || row.Y < 0
                    || row.X + row.Width > scene.Width || row.Y + row.Height > scene.Height)
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' object '{row.ObjectId}' is outside scene bounds.");
                }
            }

            if (scene.Layout.Select(item => item.ObjectId).Distinct().Count() != scene.Layout.Count
                || scene.Layout.Count != scene.Objects.Count)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' requires exactly one layout row per object.");
            }

            foreach (SceneSpawnDefinition spawn in scene.Spawns)
            {
                if (!IsInBounds(scene, spawn.Position) || IsBlocked(scene, spawn.Position))
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' spawn '{spawn.Id}' is outside walkable scene bounds.");
                }
            }

            foreach (SceneRoute route in scene.Routes)
            {
                SceneDefinition? target = scenes.SingleOrDefault(candidate => candidate.Id == route.TargetScene);
                if (route.SourceScene != scene.Id
                    || !scene.Objects.Any(item => item.Id == route.TriggerObject && item.Kind == SceneObjectKind.Portal)
                    || target is null
                    || !target.Spawns.Any(spawn => spawn.Id == route.TargetSpawn))
                {
                    throw new InvalidDataException($"Scene route '{route.Id}' has an invalid trigger, target, or spawn.");
                }
            }
        }
    }

    public static bool IsInBounds(SceneDefinition scene, GridPosition position)
    {
        return position.X >= 0 && position.X < scene.Width && position.Y >= 0 && position.Y < scene.Height;
    }

    public static bool IsBlocked(SceneDefinition scene, GridPosition position)
    {
        return scene.Layout.Any(row => row.Contains(position) && scene.Object(row.ObjectId).BlocksMovement);
    }

    public static bool IsInBounds(SceneDefinition scene, ScenePosition position)
    {
        return position.XUnits >= 0
            && position.XUnits < scene.Width * ScenePosition.UnitsPerTile
            && position.YUnits >= 0
            && position.YUnits < scene.Height * ScenePosition.UnitsPerTile;
    }

    public static bool IsBlocked(SceneDefinition scene, ScenePosition position)
    {
        return IsBlocked(scene, position.Tile);
    }

    private static IReadOnlyList<SceneDefinition> CreateAndValidate()
    {
        SceneDefinition[] scenes =
        [
            CreateOverworld(),
            CreateFarm(),
            CreateTown(),
            CreateStore(),
            CreateRiverside()
        ];
        Validate(scenes);
        return scenes.OrderBy(scene => scene.Id.Value, StringComparer.Ordinal).ToArray();
    }

    private static SceneDefinition CreateOverworld()
    {
        return new SceneDefinition(
            TinyFarmSceneIds.Overworld,
            "Overworld",
            22,
            14,
            [
                Object("farm-entrance", SceneObjectKind.Portal, "Farm", false),
                Object("town-entrance", SceneObjectKind.Portal, "Town", false),
                Object("riverside-entrance", SceneObjectKind.Portal, "Riverside", false),
                Object("hill", SceneObjectKind.Prop, "Hill", true)
            ],
            [
                Layout("farm-entrance", 2, 7),
                Layout("town-entrance", 11, 5),
                Layout("riverside-entrance", 19, 9),
                Layout("hill", 7, 2, 3, 2)
            ],
            [Spawn("from-farm", 3, 7), Spawn("from-town", 10, 5), Spawn("from-riverside", 18, 9)],
            [
                Route("overworld-farm", "farm-entrance", TinyFarmSceneIds.Farm, "from-overworld", "ENTER FARM"),
                Route("overworld-town", "town-entrance", TinyFarmSceneIds.Town, "south-gate", "ENTER TOWN"),
                Route("overworld-riverside", "riverside-entrance", TinyFarmSceneIds.Riverside, "from-overworld", "ENTER RIVERSIDE")
            ]);
    }

    private static SceneDefinition CreateFarm()
    {
        return new SceneDefinition(
            TinyFarmSceneIds.Farm,
            "Farm",
            18,
            12,
            [
                Object("farm-exit", SceneObjectKind.Portal, "Overworld", false),
                Object("farmhouse", SceneObjectKind.Landmark, "Farmhouse", true),
                Object("plot-1", SceneObjectKind.Plot, "Plot 1", false, TinyFarmIds.PlotOne.Value),
                Object("plot-2", SceneObjectKind.Plot, "Plot 2", false, TinyFarmIds.PlotTwo.Value),
                Object("fence", SceneObjectKind.Prop, "Fence", true)
            ],
            [
                Layout("farm-exit", 17, 6),
                Layout("farmhouse", 1, 1, 4, 3),
                Layout("plot-1", 7, 5),
                Layout("plot-2", 9, 5),
                Layout("fence", 12, 2, 1, 5)
            ],
            [Spawn("from-overworld", 16, 6), Spawn("farm-start", 6, 5)],
            [Route("farm-overworld", "farm-exit", TinyFarmSceneIds.Overworld, "from-farm", "RETURN TO OVERWORLD")]);
    }

    private static SceneDefinition CreateTown()
    {
        return new SceneDefinition(
            TinyFarmSceneIds.Town,
            "Town",
            20,
            14,
            [
                Object("town-exit", SceneObjectKind.Portal, "Overworld", false),
                Object("store-entrance", SceneObjectKind.Portal, "General Store", false),
                Object("well", SceneObjectKind.Landmark, "Well", true),
                Object("market-stall", SceneObjectKind.Prop, "Market Stall", true)
            ],
            [
                Layout("town-exit", 10, 13),
                Layout("store-entrance", 17, 4),
                Layout("well", 9, 6, 2, 2),
                Layout("market-stall", 3, 3, 3, 2)
            ],
            [Spawn("south-gate", 10, 12), Spawn("from-store", 16, 4)],
            [
                Route("town-overworld", "town-exit", TinyFarmSceneIds.Overworld, "from-town", "RETURN TO OVERWORLD"),
                Route("town-store", "store-entrance", TinyFarmSceneIds.GeneralStore, "door", "ENTER STORE")
            ]);
    }

    private static SceneDefinition CreateStore()
    {
        return new SceneDefinition(
            TinyFarmSceneIds.GeneralStore,
            "General Store",
            10,
            8,
            [
                Object("store-exit", SceneObjectKind.Portal, "Town", false),
                Object("shop-counter", SceneObjectKind.Shop, "Seed Counter", true, "general-store"),
                Object("shelves", SceneObjectKind.Prop, "Shelves", true)
            ],
            [
                Layout("store-exit", 5, 7),
                Layout("shop-counter", 4, 2, 3, 1),
                Layout("shelves", 1, 1, 1, 4)
            ],
            [Spawn("door", 5, 6)],
            [Route("store-town", "store-exit", TinyFarmSceneIds.Town, "from-store", "LEAVE STORE")]);
    }

    private static SceneDefinition CreateRiverside()
    {
        return new SceneDefinition(
            TinyFarmSceneIds.Riverside,
            "Riverside",
            16,
            10,
            [
                Object("riverside-exit", SceneObjectKind.Portal, "Overworld", false),
                Object("river", SceneObjectKind.Decoration, "River", true),
                Object("reeds", SceneObjectKind.Prop, "Reeds", true)
            ],
            [
                Layout("riverside-exit", 1, 5),
                Layout("river", 10, 0, 6, 10),
                Layout("reeds", 8, 3, 1, 3)
            ],
            [Spawn("from-overworld", 2, 5)],
            [Route("riverside-overworld", "riverside-exit", TinyFarmSceneIds.Overworld, "from-riverside", "RETURN TO OVERWORLD")]);
    }

    private static SceneObjectDefinition Object(
        string id,
        SceneObjectKind kind,
        string label,
        bool blocked,
        string? semanticReference = null)
    {
        return new SceneObjectDefinition(new SceneObjectId(id), kind, label, blocked, semanticReference);
    }

    private static SceneLayoutRow Layout(string id, int x, int y, int width = 1, int height = 1, int layer = 0)
    {
        return new SceneLayoutRow(new SceneObjectId(id), x, y, width, height, layer);
    }

    private static SceneSpawnDefinition Spawn(string id, int x, int y)
    {
        return new SceneSpawnDefinition(new SceneSpawnId(id), new GridPosition(x, y));
    }

    private static SceneRoute Route(string id, string trigger, SceneId target, string spawn, string label)
    {
        SceneId source = id.StartsWith("overworld-", StringComparison.Ordinal)
            ? TinyFarmSceneIds.Overworld
            : id.StartsWith("farm-", StringComparison.Ordinal)
                ? TinyFarmSceneIds.Farm
                : id.StartsWith("town-", StringComparison.Ordinal)
                    ? TinyFarmSceneIds.Town
                    : id.StartsWith("store-", StringComparison.Ordinal)
                        ? TinyFarmSceneIds.GeneralStore
                        : TinyFarmSceneIds.Riverside;
        return new SceneRoute(
            new SceneRouteId(id),
            source,
            new SceneObjectId(trigger),
            target,
            new SceneSpawnId(spawn),
            label);
    }
}
