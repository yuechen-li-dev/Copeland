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
    bool IsPlayer);

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
    IReadOnlyList<string> Narrative);

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
