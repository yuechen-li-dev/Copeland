using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmRuntimeSave(
    long NextSequence,
    List<GameEvent> RecentEvents);

public sealed record TinyFarmAgentSave(
    string Runtime,
    string DecisionMemory);

public sealed record TinyFarmNarrativeSave(
    string Runtime,
    string AuthorityBoundary);

public sealed record TinyFarmSave(
    string RuntimeVersion,
    TinyFarmState Game,
    TinyFarmRuntimeSave Runtime,
    TinyFarmAgentSave Agents,
    TinyFarmNarrativeSave Narrative);

public static class TinyFarmSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(TinyFarmSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        Validate(save);
        return JsonSerializer.Serialize(save, Options);
    }

    public static TinyFarmSession Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TinyFarmSave save = JsonSerializer.Deserialize<TinyFarmSave>(json, Options)
            ?? throw new InvalidDataException("The TinyFarm save document was empty.");
        Validate(save);
        return new TinyFarmSession(save.Game, save.Runtime.NextSequence, save.Runtime.RecentEvents);
    }

    private static void Validate(TinyFarmSave save)
    {
        if (save.RuntimeVersion != "tiny-farm-m1@1")
        {
            throw new InvalidDataException($"Unsupported runtime version '{save.RuntimeVersion}'.");
        }

        if (save.Game.Version != TinyFarmState.SaveVersion)
        {
            throw new InvalidDataException($"Unsupported game save version {save.Game.Version}.");
        }

        string[] actorIds = save.Game.Actors.Select(actor => actor.Id.Value).ToArray();
        if (actorIds.Length != actorIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException("Actor identities must be unique.");
        }

        string[] itemIds = save.Game.Items.Select(item => item.Id.Value).ToArray();
        if (itemIds.Length != itemIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException("Item identities must be unique.");
        }

        foreach (ItemState item in save.Game.Items)
        {
            bool hasOwner = item.Owner is not null;
            bool isGrounded = item.GroundLocation is not null;
            if (hasOwner == isGrounded)
            {
                throw new InvalidDataException($"Item '{item.Id}' must have exactly one container.");
            }

            if (item.Owner is ActorId owner && !save.Game.Actor(owner).Inventory.Contains(item.Id))
            {
                throw new InvalidDataException($"Item '{item.Id}' disagrees with owner '{owner}'.");
            }
        }
    }
}
