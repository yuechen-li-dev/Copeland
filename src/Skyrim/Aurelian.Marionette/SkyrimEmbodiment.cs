using System.Text.Json;
using System.Text.Json.Serialization;
using Dominatus.Core.Blackboard;
using Marionette.Skyrim;

namespace Marionette.Skyrim.App;

/// <summary>Logical character identity; never an engine handle, load-order prefix, or live control lease.</summary>
public sealed record EmbodiedCharacter(Guid CharacterId, string PluginName, uint LocalFormId)
{
    [JsonIgnore]
    public SkyrimPlacedActorOrigin Origin => new(PluginName, LocalFormId);
}

public sealed record EmbodimentState(int Version, Guid? SelectedCharacterId, EmbodiedCharacter[] Characters)
{
    public void Validate()
    {
        if (Version != 1 || Characters is null)
        {
            throw new InvalidDataException("embodiment_schema_invalid");
        }
        var ids = new HashSet<Guid>();
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (EmbodiedCharacter character in Characters)
        {
            if (character is null || character.CharacterId == Guid.Empty || !ids.Add(character.CharacterId) ||
                !origins.Add(character.Origin.StableKey))
            {
                throw new InvalidDataException("embodiment_character_identity_invalid");
            }
        }
        if (SelectedCharacterId.HasValue && !ids.Contains(SelectedCharacterId.Value))
        {
            throw new InvalidDataException("embodiment_selection_missing");
        }
    }

    [JsonIgnore]
    public EmbodiedCharacter? Selected => Characters.SingleOrDefault(character => character.CharacterId == SelectedCharacterId);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmbodimentState))]
internal sealed partial class EmbodimentJsonContext : JsonSerializerContext;

public sealed partial class SkyrimWorldOwnerRuntime
{
    private static readonly BbKey<string> EmbodimentKey = new("skyrim.embodiment.v1");

    public EmbodimentState? ReadEmbodiment()
    {
        foreach (var entry in world.Bb.EnumerateSnapshotEntries())
        {
            if (entry.Key != EmbodimentKey.Name)
            {
                continue;
            }
            if (entry.Value is not string text || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException("embodiment_payload_invalid");
            }
            try
            {
                var saved = JsonSerializer.Deserialize(text, EmbodimentJsonContext.Default.EmbodimentState)
                    ?? throw new InvalidDataException("embodiment_payload_missing");
                saved.Validate();
                return saved;
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                throw new InvalidDataException("embodiment_payload_invalid", exception);
            }
        }
        return null;
    }

    public void WriteEmbodiment(EmbodimentState state)
    {
        state.Validate();
        world.Bb.Set(EmbodimentKey, JsonSerializer.Serialize(state, EmbodimentJsonContext.Default.EmbodimentState));
    }

    internal void RecordEmbodiment(BodyControlObservation body)
    {
        var state = ReadEmbodiment() ?? new EmbodimentState(1, null, []);
        if (string.IsNullOrEmpty(body.PluginName) || body.LocalFormId == 0)
        {
            // Temporary owned-agent bodies retain their existing semantic rebuild path.
            // They are usable in M7 but do not become placed-character identities.
            WriteEmbodiment(state with { SelectedCharacterId = null });
            return;
        }
        var origin = new SkyrimPlacedActorOrigin(body.PluginName, body.LocalFormId);
        EmbodiedCharacter? known = state.Characters.FirstOrDefault(character => character.Origin == origin);
        if (known is null)
        {
            known = new EmbodiedCharacter(Guid.NewGuid(), origin.PluginName, origin.LocalFormId);
            state = state with { Characters = [.. state.Characters, known] };
        }
        WriteEmbodiment(state with { SelectedCharacterId = known.CharacterId });
    }

    internal void ClearEmbodimentSelection()
    {
        if (ReadEmbodiment() is { } state)
        {
            WriteEmbodiment(state with { SelectedCharacterId = null });
        }
    }

    internal void ClearEmbodiment() => world.Bb.Remove(EmbodimentKey);
}

/// <summary>Reacquires a selected placed actor through normal admission after save/load.
/// No actor is spawned, no old native token is adopted, and no off-screen teleport is issued.</summary>
internal sealed class SkyrimEmbodimentController
{
    private (ulong Generation, Guid Character) lastIdentity;
    private DateTimeOffset nextAttempt;
    private string lastReason = "";

    internal async ValueTask TickAsync(SkyrimHostContext context, bool ready, CancellationToken cancellationToken)
    {
        SkyrimBodyControl? bodies = context.Bodies;
        EmbodiedCharacter? selected = context.WorldOwner?.ReadEmbodiment()?.Selected;
        if (!ready || bodies is null || bodies.Lease is not null || selected is null)
        {
            return;
        }
        var identity = (context.State.WorldGeneration, selected.CharacterId);
        if (lastIdentity != identity)
        {
            lastIdentity = identity;
            nextAttempt = DateTimeOffset.MinValue;
            lastReason = "";
        }
        if (DateTimeOffset.UtcNow < nextAttempt)
        {
            return;
        }
        nextAttempt = DateTimeOffset.UtcNow.AddSeconds(1);
        try
        {
            LoadedActorBody target = await bodies.ResolveAsync(selected.Origin, context.State.WorldGeneration, cancellationToken);
            BodyControlObservation body = await context.AcquireBodyAsync(target, cancellationToken: cancellationToken);
            lastReason = "";
            Console.WriteLine($"SKYRIM_EMBODIMENT resumed character={selected.CharacterId} origin={selected.Origin.StableKey} " +
                $"form={body.FormId} generation={body.WorldGeneration} lease={body.LeaseId}");
        }
        catch (SkyrimBodyControlException exception) when (!exception.IsDeferred)
        {
            if (lastReason != exception.Reason)
            {
                lastReason = exception.Reason;
                Console.Error.WriteLine($"SKYRIM_EMBODIMENT dormant character={selected.CharacterId} reason={exception.Reason}");
            }
        }
    }
}
