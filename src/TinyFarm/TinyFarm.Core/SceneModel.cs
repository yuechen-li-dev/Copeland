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

public readonly record struct SceneAnchorId(string Value)
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

public enum SceneAnchorKind
{
    Spawn,
    Work,
    ShopCounter,
    Home,
    Social,
    Rest,
    Wander,
    Exit
}

public enum SceneObjectKind
{
    Portal,
    Plot,
    Prop,
    Shop,
    Landmark,
    Decoration,
    Bed,
    Forage
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

public sealed record SceneAnchorDefinition(
    SceneAnchorId Id,
    SceneId Scene,
    ScenePosition Position,
    SceneAnchorKind Kind,
    LocationId? SemanticLocation = null,
    SceneObjectId? SemanticObject = null,
    ActorFacing? Facing = null,
    int ArrivalRadiusUnits = ScenePosition.UnitsPerTile / 8);

public sealed record SceneRoute(
    SceneRouteId Id,
    SceneId SourceScene,
    SceneObjectId TriggerObject,
    SceneId TargetScene,
    SceneAnchorId TargetAnchor,
    string InteractionLabel);

public sealed class SceneDefinition
{
    private bool[]? blockedTiles;
    public SceneDefinition(
        SceneId id,
        string name,
        int width,
        int height,
        IEnumerable<SceneObjectDefinition> objects,
        IEnumerable<SceneLayoutRow> layout,
        IEnumerable<SceneAnchorDefinition> anchors,
        IEnumerable<SceneRoute> routes)
    {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Objects = objects.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Layout = layout.OrderBy(item => item.Layer).ThenBy(item => item.ObjectId.Value, StringComparer.Ordinal).ToArray();
        Anchors = anchors.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Routes = routes.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
    }

    public SceneId Id { get; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<SceneObjectDefinition> Objects { get; }
    public IReadOnlyList<SceneLayoutRow> Layout { get; }
    public IReadOnlyList<SceneAnchorDefinition> Anchors { get; }
    public IReadOnlyList<SceneRoute> Routes { get; }

    public SceneObjectDefinition Object(SceneObjectId id)
    {
        return Objects.Single(item => item.Id == id);
    }

    public SceneLayoutRow Placement(SceneObjectId id)
    {
        return Layout.Single(item => item.ObjectId == id);
    }

    public SceneAnchorDefinition Anchor(SceneAnchorId id)
    {
        return Anchors.Single(item => item.Id == id);
    }

    internal bool IsBlocked(GridPosition position)
    {
        if (blockedTiles is not null)
        {
            return blockedTiles[(position.Y * Width) + position.X];
        }

        foreach (SceneLayoutRow row in Layout)
        {
            if (row.Contains(position) && Object(row.ObjectId).BlocksMovement)
            {
                return true;
            }
        }
        return false;
    }

    internal void BuildSpatialIndex()
    {
        var blocked = new bool[Width * Height];
        var objectIndex = Objects.ToDictionary(item => item.Id);
        foreach (SceneLayoutRow row in Layout)
        {
            if (!objectIndex[row.ObjectId].BlocksMovement)
            {
                continue;
            }

            for (int y = row.Y; y < row.Y + row.Height; y++)
            {
                for (int x = row.X; x < row.X + row.Width; x++)
                {
                    blocked[(y * Width) + x] = true;
                }
            }
        }
        blockedTiles = blocked;
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
    public static readonly SceneId Residence = new("residence");
}

public static class TinyFarmAnchorIds
{
    public static readonly SceneAnchorId FarmHome = new("farm.home");
    public static readonly SceneAnchorId FarmWorkArea = new("farm.work-area");
    public static readonly SceneAnchorId TownSquare = new("town.square");
    public static readonly SceneAnchorId StoreCounter = new("general-store.counter");
    public static readonly SceneAnchorId RiversideMeetingPoint = new("riverside.meeting-point");
    public static readonly SceneAnchorId EliasHomeBed = new("elias.home-bed");
    public static readonly SceneAnchorId MaraHomeBed = new("mara.home-bed");
    public static readonly SceneAnchorId SelaHomeBed = new("sela.home-bed");
    public static readonly SceneAnchorId FarmWanderA = new("farm.wander-a");
    public static readonly SceneAnchorId FarmWanderB = new("farm.wander-b");

    public static SceneAnchorId HomeBedFor(ActorId actor)
    {
        if (actor == TinyFarmIds.Elias) return EliasHomeBed;
        if (actor == TinyFarmIds.Mara) return MaraHomeBed;
        if (actor == TinyFarmIds.Sela) return SelaHomeBed;
        throw new KeyNotFoundException($"Actor '{actor}' has no personal bed.");
    }

    public static bool IsHomeBed(SceneAnchorId anchor)
    {
        return anchor == EliasHomeBed || anchor == MaraHomeBed || anchor == SelaHomeBed;
    }

    public static bool IsWander(SceneAnchorId anchor)
    {
        return anchor == FarmWanderA || anchor == FarmWanderB;
    }
}

public sealed class TinyFarmSceneCatalog
{
    private readonly IReadOnlyDictionary<SceneId, SceneDefinition> sceneIndex;
    private readonly IReadOnlyDictionary<SceneAnchorId, SceneAnchorDefinition> anchorIndex;

    public TinyFarmSceneCatalog(IEnumerable<SceneDefinition> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        SceneDefinition[] materialized = scenes
            .OrderBy(scene => scene.Id.Value, StringComparer.Ordinal)
            .ToArray();
        TinyFarmScenes.Validate(materialized);
        foreach (SceneDefinition scene in materialized)
        {
            scene.BuildSpatialIndex();
        }
        All = materialized;
        sceneIndex = materialized.ToDictionary(scene => scene.Id);
        anchorIndex = materialized
            .SelectMany(scene => scene.Anchors)
            .ToDictionary(anchor => anchor.Id);
    }

    public IReadOnlyList<SceneDefinition> All { get; }

    public SceneDefinition Get(SceneId id)
    {
        return sceneIndex.TryGetValue(id, out SceneDefinition? scene)
            ? scene
            : throw new KeyNotFoundException($"Unknown scene '{id}'.");
    }

    public bool TryGetAnchor(SceneAnchorId id, out SceneAnchorDefinition anchor)
    {
        return anchorIndex.TryGetValue(id, out anchor!);
    }

    public SceneAnchorDefinition GetAnchor(SceneAnchorId id)
    {
        return anchorIndex.TryGetValue(id, out SceneAnchorDefinition? anchor)
            ? anchor
            : throw new KeyNotFoundException($"Unknown scene anchor '{id}'.");
    }

    public SceneAnchorDefinition AnchorForLocation(LocationId location)
    {
        SceneAnchorId anchor = location == TinyFarmIds.Farmhouse
            ? TinyFarmAnchorIds.FarmHome
            : location == TinyFarmIds.GeneralStore
                ? TinyFarmAnchorIds.StoreCounter
                : location == TinyFarmIds.Riverside
                    ? TinyFarmAnchorIds.RiversideMeetingPoint
                    : TinyFarmAnchorIds.TownSquare;
        return GetAnchor(anchor);
    }

    public SceneAnchorDefinition CoarseEntryAnchor(SceneId scene)
    {
        return Get(scene).Anchors
            .Where(anchor => anchor.Kind == SceneAnchorKind.Spawn)
            .OrderBy(anchor => anchor.Id.Value, StringComparer.Ordinal)
            .First();
    }

}

public static class TinyFarmScenes
{
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
        if (scene == TinyFarmSceneIds.Farm || scene == TinyFarmSceneIds.Residence)
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

    public static bool SceneAgreesWithLocation(SceneId scene, LocationId location)
    {
        return SceneForLocation(location) == scene
            || scene == TinyFarmSceneIds.Residence && location == TinyFarmIds.Farmhouse
            || scene == TinyFarmSceneIds.Overworld && location == TinyFarmIds.TownSquare;
    }

    public static void Validate(IEnumerable<SceneDefinition> definitions)
    {
        SceneDefinition[] scenes = definitions.ToArray();
        if (scenes.Select(scene => scene.Id).Distinct().Count() != scenes.Length)
        {
            throw new InvalidDataException("Scene identities must be unique.");
        }

        SceneAnchorDefinition[] allAnchors = scenes.SelectMany(scene => scene.Anchors).ToArray();
        if (allAnchors.Select(anchor => anchor.Id).Distinct().Count() != allAnchors.Length)
        {
            throw new InvalidDataException("Scene anchor identities must be globally unique.");
        }

        SceneRoute[] allRoutes = scenes.SelectMany(scene => scene.Routes).ToArray();
        if (allRoutes.Select(route => route.Id).Distinct().Count() != allRoutes.Length)
        {
            throw new InvalidDataException("Scene route identities must be globally unique.");
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

            if (scene.Anchors.Select(item => item.Id).Distinct().Count() != scene.Anchors.Count)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' has duplicate anchor identities.");
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

            foreach (SceneAnchorDefinition anchor in scene.Anchors)
            {
                bool validSemanticLocation = anchor.SemanticLocation is null
                    || TinyFarmContent.Locations.Any(location => location.Id == anchor.SemanticLocation);
                bool validSemanticObject = anchor.SemanticObject is null
                    || scene.Objects.Any(item => item.Id == anchor.SemanticObject);
                if (anchor.Scene != scene.Id
                    || !IsInBounds(scene, anchor.Position)
                    || IsBlocked(scene, anchor.Position)
                    || anchor.ArrivalRadiusUnits < 0
                    || !validSemanticLocation
                    || !validSemanticObject)
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' anchor '{anchor.Id}' is invalid or not walkable.");
                }
            }

            foreach (SceneRoute route in scene.Routes)
            {
                SceneDefinition? target = scenes.SingleOrDefault(candidate => candidate.Id == route.TargetScene);
                if (route.SourceScene != scene.Id
                    || !scene.Objects.Any(item => item.Id == route.TriggerObject && item.Kind == SceneObjectKind.Portal)
                    || target is null
                    || !target.Anchors.Any(anchor => anchor.Id == route.TargetAnchor))
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
        return scene.IsBlocked(position);
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

}
