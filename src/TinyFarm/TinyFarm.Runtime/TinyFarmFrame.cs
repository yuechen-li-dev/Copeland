using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public readonly record struct TinyFarmPoint(int X, int Y);

public sealed record TinyFarmLocationView(
    LocationId Id,
    string Name,
    TinyFarmPoint Position,
    IReadOnlyList<LocationId> Exits,
    bool IsCurrent);

public sealed record TinyFarmActorView(
    ActorId Id,
    string Name,
    LocationId Location,
    TinyFarmPoint Position,
    bool IsPlayer,
    ActorFacing Facing = ActorFacing.Down,
    bool IsInteractionTarget = false,
    SceneAnchorId? SemanticTarget = null);

public sealed record TinyFarmItemView(
    ItemId Id,
    string Name,
    LocationId Location,
    TinyFarmPoint Position);

public sealed record TinyFarmPlotView(
    FarmPlotId Id,
    LocationId Location,
    TinyFarmPoint Position,
    CropId? Crop,
    int GrowthStage,
    int GrowthDays,
    bool WateredToday,
    bool Harvestable);

public sealed record TinyFarmInventoryView(string Id, string Name, int Count);

public sealed record TinyFarmSceneObjectView(
    SceneObjectId Id,
    SceneObjectKind Kind,
    string Label,
    TinyFarmPoint Position,
    int Width,
    int Height,
    int Layer,
    bool BlocksMovement,
    string? SemanticReference);

public sealed record TinyFarmRouteView(
    SceneRouteId Id,
    SceneObjectId TriggerObject,
    SceneId TargetScene,
    SceneAnchorId TargetAnchor,
    string InteractionLabel);

public sealed record TinyFarmFrame(
    int Day,
    int Minute,
    string Time,
    int Money,
    LocationId CurrentLocation,
    string CurrentLocationName,
    IReadOnlyList<TinyFarmLocationView> Locations,
    IReadOnlyList<TinyFarmActorView> Actors,
    IReadOnlyList<TinyFarmItemView> GroundItems,
    IReadOnlyList<TinyFarmPlotView> Plots,
    IReadOnlyList<TinyFarmInventoryView> Inventory,
    IReadOnlyList<string> InteractionHints,
    IReadOnlyList<string> Narrative,
    SceneId? ActiveScene = null,
    int SceneWidth = 0,
    int SceneHeight = 0,
    IReadOnlyList<TinyFarmSceneObjectView>? SceneObjects = null,
    IReadOnlyList<TinyFarmRouteView>? SceneRoutes = null,
    int SceneUnitsPerTile = 1,
    string? InteractionTarget = null);

public static class TinyFarmFrameProjector
{
    private static readonly IReadOnlyDictionary<LocationId, TinyFarmPoint> LocationPositions =
        new Dictionary<LocationId, TinyFarmPoint>
        {
            [TinyFarmIds.Farmhouse] = new(160, 350),
            [TinyFarmIds.TownSquare] = new(430, 260),
            [TinyFarmIds.GeneralStore] = new(710, 140),
            [TinyFarmIds.Riverside] = new(710, 400)
        };

    public static TinyFarmFrame Project(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        IReadOnlyList<NarrativeLine>? narrative = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definitions);

        ActorState player = state.Actor(TinyFarmIds.Player);
        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            return ProjectScene(state, definitions, narrative, player);
        }

        LocationDefinition current = TinyFarmContent.Location(player.Location);
        TinyFarmLocationView[] locations = TinyFarmContent.Locations
            .OrderBy(location => location.Id.Value, StringComparer.Ordinal)
            .Select(location => new TinyFarmLocationView(
                location.Id,
                location.Name,
                LocationPositions[location.Id],
                location.Exits.OrderBy(exit => exit.Value, StringComparer.Ordinal).ToArray(),
                location.Id == player.Location))
            .ToArray();

        TinyFarmActorView[] actors = state.Actors
            .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
            .Select((actor, index) => new TinyFarmActorView(
                actor.Id,
                actor.Name,
                actor.Location,
                Offset(LocationPositions[actor.Location], actor.IsPlayer ? 0 : 28 + (index * 18), actor.IsPlayer ? -12 : 18),
                actor.IsPlayer))
            .ToArray();

        TinyFarmItemView[] groundItems = state.Items
            .Where(item => item.GroundLocation is not null)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select((item, index) => new TinyFarmItemView(
                item.Id,
                item.Name,
                item.GroundLocation!.Value,
                Offset(LocationPositions[item.GroundLocation.Value], -35 + (index * 16), 30)))
            .ToArray();

        TinyFarmPlotView[] plots = state.FarmPlots
            .OrderBy(plot => plot.Id.Value, StringComparer.Ordinal)
            .Select((plot, index) => ProjectPlot(plot, definitions, index))
            .ToArray();

        var inventory = state.InventoryStacks
            .Where(stack => stack.Actor == player.Id)
            .Select(stack => new TinyFarmInventoryView(
                stack.Product.Value,
                definitions.Item(stack.Product).Name,
                stack.Count))
            .Concat(player.Inventory.Select(item => new TinyFarmInventoryView(
                item.Value,
                state.Item(item).Name,
                1)))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        return new TinyFarmFrame(
            state.Day,
            state.Minute,
            FormatTime(state.Minute),
            player.Money,
            player.Location,
            current.Name,
            locations,
            actors,
            groundItems,
            plots,
            inventory,
            BuildHints(state, definitions, player),
            narrative?.Select(line => $"{line.Speaker}: {line.Text}").ToArray() ?? []);
    }

    private static TinyFarmFrame ProjectScene(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        IReadOnlyList<NarrativeLine>? narrative,
        ActorState player)
    {
        ActorSceneState playerPlacement = state.ActorScene(player.Id);
        SceneDefinition scene = definitions.Scenes.Get(playerPlacement.Scene);
        InteractionTarget? interactionTarget = TinyFarmSpatialQueries.SelectInteractionTarget(
            state,
            player.Id,
            definitions.Scenes);
        TinyFarmActorView[] actors = state.ActorScenes
            .Where(placement => placement.Scene == scene.Id)
            .OrderBy(placement => placement.Actor.Value, StringComparer.Ordinal)
            .Select(placement =>
            {
                ActorState actor = state.Actor(placement.Actor);
                return new TinyFarmActorView(
                    actor.Id,
                    actor.Name,
                    actor.Location,
                    state.Version >= TinyFarmState.ContinuousSceneSaveVersion
                        ? new TinyFarmPoint(placement.WorldPosition.XUnits, placement.WorldPosition.YUnits)
                        : new TinyFarmPoint(placement.Position.X, placement.Position.Y),
                    actor.IsPlayer,
                    placement.Facing,
                    interactionTarget?.Actor == actor.Id,
                    actor.IsPlayer
                        ? null
                        : TinyFarmNpcController.ScheduledAnchor(actor.Id, state.Minute, definitions.Schedules));
            })
            .ToArray();
        TinyFarmSceneObjectView[] objects = scene.Layout
            .Select(row =>
            {
                SceneObjectDefinition definition = scene.Object(row.ObjectId);
                return new TinyFarmSceneObjectView(
                    definition.Id,
                    definition.Kind,
                    definition.Label,
                    new TinyFarmPoint(row.X, row.Y),
                    row.Width,
                    row.Height,
                    row.Layer,
                    definition.BlocksMovement,
                    definition.SemanticReference);
            })
            .ToArray();
        TinyFarmPlotView[] plots = state.FarmPlots
            .Where(_ => scene.Id == TinyFarmSceneIds.Farm)
            .OrderBy(plot => plot.Id.Value, StringComparer.Ordinal)
            .Select(plot => ProjectScenePlot(plot, definitions, scene))
            .ToArray();
        TinyFarmRouteView[] routes = scene.Routes
            .Select(route => new TinyFarmRouteView(
                route.Id,
                route.TriggerObject,
                route.TargetScene,
                route.TargetAnchor,
                route.InteractionLabel))
            .ToArray();
        TinyFarmInventoryView[] inventory = ProjectInventory(state, definitions, player);

        return new TinyFarmFrame(
            state.Day,
            state.Minute,
            FormatTime(state.Minute),
            player.Money,
            player.Location,
            scene.Name,
            [],
            actors,
            [],
            plots,
            inventory,
            BuildSceneHints(state, definitions, playerPlacement, scene),
            narrative?.Select(line => $"{line.Speaker}: {line.Text}").ToArray() ?? [],
            scene.Id,
            scene.Width,
            scene.Height,
            objects,
            routes,
            state.Version >= TinyFarmState.ContinuousSceneSaveVersion ? ScenePosition.UnitsPerTile : 1,
            interactionTarget?.StableId);
    }

    public static string ComputeHash(TinyFarmFrame frame)
    {
        string json = JsonSerializer.Serialize(frame, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string WriteJson(TinyFarmFrame frame, bool indented = true)
    {
        var options = new JsonSerializerOptions(JsonOptions) { WriteIndented = indented };
        return JsonSerializer.Serialize(frame, options);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static TinyFarmPlotView ProjectPlot(FarmPlotState plot, TinyFarmDefinitions definitions, int index)
    {
        int growthDays = plot.Crop is CropId crop ? definitions.Crop(crop).GrowthDays : 0;
        TinyFarmPoint farm = LocationPositions[plot.Location];
        return new TinyFarmPlotView(
            plot.Id,
            plot.Location,
            Offset(farm, -58 + (index * 58), 62),
            plot.Crop,
            plot.GrowthStage,
            growthDays,
            plot.WateredToday,
            plot.Crop is not null && plot.GrowthStage >= growthDays);
    }

    private static TinyFarmPlotView ProjectScenePlot(
        FarmPlotState plot,
        TinyFarmDefinitions definitions,
        SceneDefinition scene)
    {
        SceneLayoutRow layout = scene.Layout.Single(row =>
            scene.Object(row.ObjectId).SemanticReference == plot.Id.Value);
        int growthDays = plot.Crop is CropId crop ? definitions.Crop(crop).GrowthDays : 0;
        return new TinyFarmPlotView(
            plot.Id,
            plot.Location,
            new TinyFarmPoint(layout.X, layout.Y),
            plot.Crop,
            plot.GrowthStage,
            growthDays,
            plot.WateredToday,
            plot.Crop is not null && plot.GrowthStage >= growthDays);
    }

    private static TinyFarmInventoryView[] ProjectInventory(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        ActorState player)
    {
        return state.InventoryStacks
            .Where(stack => stack.Actor == player.Id)
            .Select(stack => new TinyFarmInventoryView(
                stack.Product.Value,
                definitions.Item(stack.Product).Name,
                stack.Count))
            .Concat(player.Inventory.Select(item => new TinyFarmInventoryView(
                item.Value,
                state.Item(item).Name,
                1)))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSceneHints(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        ActorSceneState player,
        SceneDefinition scene)
    {
        var hints = new List<string> { "Move", "Wait", "Save", "Load" };
        if (state.Version >= TinyFarmState.ContinuousSceneSaveVersion)
        {
            InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(
                state,
                player.Actor,
                definitions.Scenes);
            if (target is not null)
            {
                string label = target.Actor is ActorId actorId
                    ? state.Actor(actorId).Name
                    : scene.Object(target.SceneObject!.Value).Label;
                hints.Add($"{label} [Interact]");
            }
            return hints;
        }
        SceneRoute? route = scene.Routes
            .Where(candidate => scene.Placement(candidate.TriggerObject).Contains(player.Position))
            .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (route is not null)
        {
            hints.Add(route.InteractionLabel);
        }

        ActorState? nearby = state.ActorScenes
            .Where(placement => placement.Actor != player.Actor
                && placement.Scene == player.Scene
                && placement.Position.ManhattanDistance(player.Position) <= 1)
            .OrderBy(placement => placement.Actor.Value, StringComparer.Ordinal)
            .Select(placement => state.Actor(placement.Actor))
            .FirstOrDefault();
        if (nearby is not null)
        {
            hints.Add($"Talk to {nearby.Name}");
        }

        if (scene.Id == TinyFarmSceneIds.GeneralStore
            && state.ActorScene(TinyFarmIds.Sela).Position.ManhattanDistance(player.Position) <= 1)
        {
            ItemDefinition seed = definitions.Item(TinyFarmIds.TurnipSeed);
            hints.Add($"Buy {seed.Name} ({seed.BuyPrice})");
        }

        if (scene.Id == TinyFarmSceneIds.Farm)
        {
            SceneObjectDefinition? plot = scene.Objects
                .Where(item => item.Kind == SceneObjectKind.Plot)
                .FirstOrDefault(item =>
                    player.Position.ManhattanDistance(ToGrid(scene.Placement(item.Id))) == 1);
            if (plot is not null)
            {
                hints.Add("Plant / Water / Harvest");
            }
        }

        return hints;
    }

    private static GridPosition ToGrid(SceneLayoutRow row)
    {
        return new GridPosition(row.X, row.Y);
    }

    private static IReadOnlyList<string> BuildHints(TinyFarmState state, TinyFarmDefinitions definitions, ActorState player)
    {
        var hints = new List<string> { "Move", "Wait", "Save", "Load" };
        ActorState? nearby = state.Actors
            .Where(actor => !actor.IsPlayer && actor.Location == player.Location)
            .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (nearby is not null)
        {
            hints.Add($"Talk to {nearby.Name}");
        }

        ItemState? ground = state.Items
            .Where(item => item.GroundLocation == player.Location)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (ground is not null)
        {
            hints.Add($"Take {ground.Name}");
        }

        if (player.Location == TinyFarmIds.GeneralStore)
        {
            ItemDefinition seed = definitions.Item(TinyFarmIds.TurnipSeed);
            hints.Add($"Buy {seed.Name} ({seed.BuyPrice})");
            if (state.ProductCount(player.Id, TinyFarmIds.Turnip) > 0)
            {
                hints.Add($"Sell {definitions.Item(TinyFarmIds.Turnip).Name}");
            }
        }

        if (player.Location == TinyFarmIds.Farmhouse)
        {
            hints.Add("Plant");
            hints.Add("Water");
            hints.Add("Harvest");
        }

        return hints;
    }

    private static TinyFarmPoint Offset(TinyFarmPoint point, int x, int y) => new(point.X + x, point.Y + y);

    private static string FormatTime(int minute)
    {
        int minuteOfDay = minute % 1440;
        return $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";
    }
}
