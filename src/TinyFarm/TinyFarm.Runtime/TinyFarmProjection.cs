using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public static class TinyFarmTextProjection
{
    public static string Describe(TinyFarmState state)
    {
        ActorState player = state.Actor(TinyFarmIds.Player);
        LocationDefinition location = TinyFarmContent.Location(player.Location);
        int day = (state.Minute / (24 * 60)) + 1;
        int minuteOfDay = state.Minute % (24 * 60);
        var text = new StringBuilder();
        text.Append("Day ").Append(day).Append(' ')
            .Append(minuteOfDay / 60).Append(':')
            .Append((minuteOfDay % 60).ToString("00"))
            .Append(" — ").AppendLine(state.CurrentScene is SceneId scene
                ? TinyFarmScenes.Get(scene).Name
                : location.Name)
            .AppendLine(location.Description);
        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            ActorSceneState placement = state.ActorScene(player.Id);
            text.Append("Tile: ").Append(placement.Position.X).Append(',').Append(placement.Position.Y).AppendLine();
        }

        string actors = string.Join(
            ", ",
            state.Actors
                .Where(actor => !actor.IsPlayer && actor.Location == player.Location)
                .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
                .Select(actor => actor.Name));
        text.AppendLine(actors.Length == 0 ? "No one else is here." : $"Here: {actors}.");

        string groundItems = string.Join(
            ", ",
            state.Items
                .Where(item => item.GroundLocation == player.Location)
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .Select(item => item.Name));
        if (groundItems.Length > 0)
        {
            text.Append("Visible: ").Append(groundItems).AppendLine(".");
        }

        string inventory = string.Join(
            ", ",
            player.Inventory.Select(state.Item).Select(item => item.Name));
        text.Append("Money: ").Append(player.Money)
            .Append(" | Inventory: ").Append(inventory.Length == 0 ? "empty" : inventory)
            .Append(" | Favor: ").Append(state.Favor);
        if (state.Version >= TinyFarmState.SaveVersion)
        {
            IEnumerable<string> productDescriptions = state.InventoryStacks
                .Where(stack => stack.Actor == player.Id)
                .Select(stack => $"{stack.Product.Value} x{stack.Count}");
            string products = string.Join(", ", productDescriptions);
            IEnumerable<string> plotDescriptions = state.FarmPlots.Select(plot => plot.Crop is null
                ? $"{plot.Id.Value}: empty"
                : $"{plot.Id.Value}: {plot.Crop.Value.Value} stage {plot.GrowthStage}");
            string plots = string.Join(", ", plotDescriptions);
            text.Append(" | Products: ").Append(products.Length == 0 ? "empty" : products)
                .AppendLine().Append("Plots: ").Append(plots);
        }
        return text.ToString();
    }
}

public static class TinyFarmCommandParser
{
    public static GameIntent Parse(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        string[] parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0].ToLowerInvariant();

        if (verb == "move"
            && (parts.Length == 3 || parts.Length == 4)
            && ParseDirection(parts[1]) is SpatialMoveIntent direction
            && int.TryParse(parts[2], out int distance)
            && (parts.Length == 3 || parts[3].Equals("units", StringComparison.OrdinalIgnoreCase)))
        {
            return direction with { Distance = distance };
        }

        return verb switch
        {
            "look" when parts.Length == 1 => new LookIntent(),
            "move" when parts.Length == 2 && ParseDirection(parts[1]) is SpatialMoveIntent spatial => spatial,
            "move" when parts.Length == 2 => new MoveIntent(new LocationId(parts[1])),
            "interact" when parts.Length == 1 => new InteractIntent(),
            "talk" when parts.Length == 2 => new TalkIntent(new ActorId(parts[1])),
            "take" when parts.Length == 2 => new TakeIntent(new ItemId(parts[1])),
            "give" when parts.Length == 3 => new GiveIntent(new ItemId(parts[1]), new ActorId(parts[2])),
            "buy" when parts.Length == 2 => new BuyIntent(new ItemId(parts[1])),
            "sell" when parts.Length == 2 => new SellIntent(new ItemId(parts[1])),
            "buy-product" when parts.Length == 2 => new BuyProductIntent(new ProductId(parts[1])),
            "sell-product" when parts.Length == 2 => new SellProductIntent(new ProductId(parts[1])),
            "plant" when parts.Length == 3 => new PlantIntent(new FarmPlotId(parts[1]), new CropId(parts[2])),
            "water" when parts.Length == 2 => new WaterIntent(new FarmPlotId(parts[1])),
            "harvest" when parts.Length == 2 => new HarvestIntent(new FarmPlotId(parts[1])),
            "wait" when parts.Length == 2 && int.TryParse(parts[1], out int minutes) => new WaitIntent(minutes),
            _ => throw new FormatException(
                "Use: look, move <left/right/up/down> [distance] [units], move <location>, interact, talk/take/buy/sell, buy-product/sell-product <product>, plant <plot> <crop>, water/harvest <plot>, or wait <minutes>.")
        };
    }

    private static SpatialMoveIntent? ParseDirection(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "left" or "west" => new SpatialMoveIntent(-1, 0),
            "right" or "east" => new SpatialMoveIntent(1, 0),
            "up" or "north" => new SpatialMoveIntent(0, -1),
            "down" or "south" => new SpatialMoveIntent(0, 1),
            _ => null
        };
    }
}

public sealed record TinyFarmInspectionSnapshot(
    int Day,
    int Minute,
    string StateHash,
    IReadOnlyList<object> Actors,
    IReadOnlyList<object> AgentObservations,
    IReadOnlyList<object> IntentQueue,
    IReadOnlyList<IntentResult> LastResults,
    IReadOnlyList<FarmPlotState> Plots,
    IReadOnlyList<ShopStock> ShopStock,
    IReadOnlyList<InventoryStack> Inventory,
    SceneId? CurrentScene,
    IReadOnlyList<ActorSceneState> ActorScenes);

public static class TinyFarmInspector
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string WriteJson(TinyFarmSession session, IReadOnlyList<IntentResult> lastResults)
    {
        TinyFarmState state = session.State;
        object[] actors = state.Actors
            .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
            .Select(actor => (object)new
            {
                id = actor.Id.Value,
                location = actor.Location.Value,
                actor.Money,
                inventory = actor.Inventory.Select(item => item.Value).ToArray()
            })
            .ToArray();
        object[] observations = state.Actors
            .Where(actor => !actor.IsPlayer)
            .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
            .Select(actor => (object)new
            {
                self = actor.Id.Value,
                location = actor.Location.Value,
                nearby = state.Actors
                    .Where(other => other.Id != actor.Id && other.Location == actor.Location)
                    .Select(other => other.Id.Value)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                state.Minute
            })
            .ToArray();

        var snapshot = new TinyFarmInspectionSnapshot(
            state.Day,
            state.Minute,
            TinyFarmSemanticHash.Compute(state),
            actors,
            observations,
            [],
            lastResults,
            state.FarmPlots,
            state.ShopStock,
            state.InventoryStacks,
            state.CurrentScene,
            state.ActorScenes);
        return JsonSerializer.Serialize(snapshot, Options);
    }
}
