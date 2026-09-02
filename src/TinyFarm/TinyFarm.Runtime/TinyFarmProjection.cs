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
            .Append(" — ").AppendLine(location.Name)
            .AppendLine(location.Description);

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

        return verb switch
        {
            "look" when parts.Length == 1 => new LookIntent(),
            "move" when parts.Length == 2 => new MoveIntent(new LocationId(parts[1])),
            "talk" when parts.Length == 2 => new TalkIntent(new ActorId(parts[1])),
            "take" when parts.Length == 2 => new TakeIntent(new ItemId(parts[1])),
            "give" when parts.Length == 3 => new GiveIntent(new ItemId(parts[1]), new ActorId(parts[2])),
            "buy" when parts.Length == 2 => new BuyIntent(new ItemId(parts[1])),
            "sell" when parts.Length == 2 => new SellIntent(new ItemId(parts[1])),
            "wait" when parts.Length == 2 && int.TryParse(parts[1], out int minutes) => new WaitIntent(minutes),
            _ => throw new FormatException(
                "Use: look, move <location>, talk <actor>, take <item>, give <item> <actor>, buy <item>, sell <item>, or wait <minutes>.")
        };
    }
}

public sealed record TinyFarmInspectionSnapshot(
    int Minute,
    string StateHash,
    IReadOnlyList<object> Actors,
    IReadOnlyList<object> AgentObservations,
    IReadOnlyList<object> IntentQueue,
    IReadOnlyList<IntentResult> LastResults);

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
            state.Minute,
            TinyFarmSemanticHash.Compute(state),
            actors,
            observations,
            [],
            lastResults);
        return JsonSerializer.Serialize(snapshot, Options);
    }
}
