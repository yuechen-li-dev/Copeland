using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dominatus.Core.Blackboard;

namespace Marionette.Skyrim.App;

public sealed record OwnedAgentPoint(float X, float Y, float Z)
{
    [JsonIgnore]
    public Vector3 Vector => new(X, Y, Z);
    public static OwnedAgentPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

/// <summary>Versioned semantic resume state. Contains no native lease, actor form, operation, or receipt IDs.</summary>
public sealed record OwnedNpcAgentSaveState(int Version, Guid AgentId, string Definition,
    uint CellFormId, OwnedAgentPoint Origin, OwnedAgentPoint Offset, int Waypoint,
    int CompletedLegs, int Failures, string Mode, float RemainingWaitSeconds,
    float RestSeconds, int MaximumRetries)
{
    public bool CanMaterialize(uint playerCell, Vector3 playerPosition)
    {
        return playerCell == CellFormId && Finite(playerPosition)
            && Vector3.Distance(playerPosition, Origin.Vector) <= 800
            && Vector3.Distance(playerPosition, Origin.Vector + Offset.Vector) <= 800;
    }

    public void Validate()
    {
        if (Version != 1 || Definition != "owned-patrol-v1")
        {
            throw new InvalidDataException("owned_agent_checkpoint_version_unsupported");
        }
        if (AgentId == Guid.Empty || CellFormId == 0 || CellFormId >= 0xFF000000
            || Origin is null || Offset is null || !Finite(Origin.Vector) || !Finite(Offset.Vector)
            || Offset.Vector.Length() is < 128 or > 512 || Waypoint is < 0 or > 1
            || CompletedLegs < 0 || MaximumRetries is < 0 or > 5
            || Failures < 0 || Failures > MaximumRetries + 1
            || !float.IsFinite(RestSeconds) || RestSeconds is < 0 or > 30
            || !float.IsFinite(RemainingWaitSeconds) || RemainingWaitSeconds < 0
            || Mode is not ("patrol" or "rest" or "recover" or "stopped")
            || (Mode == "rest" && RemainingWaitSeconds > RestSeconds)
            || (Mode == "recover" && RemainingWaitSeconds > 1)
            || (Mode is "patrol" or "stopped" && RemainingWaitSeconds != 0))
        {
            throw new InvalidDataException("owned_agent_checkpoint_invalid");
        }
    }

    private static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(OwnedNpcAgentSaveState))]
[JsonSerializable(typeof(PatrolTrialState))]
internal sealed partial class OwnedNpcCheckpointJsonContext : JsonSerializerContext;

public sealed partial class SkyrimWorldOwnerRuntime
{
    private static readonly BbKey<string> OwnedAgentStateKey = new("skyrim.owned-agent.semantic.v1");

    public void WriteOwnedAgentState(OwnedNpcAgentSaveState state)
    {
        state.Validate();
        world.Bb.Set(OwnedAgentStateKey, JsonSerializer.Serialize(state,
            OwnedNpcCheckpointJsonContext.Default.OwnedNpcAgentSaveState));
    }

    public OwnedNpcAgentSaveState? ReadOwnedAgentState()
    {
        string? json = null;
        foreach (var entry in world.Bb.EnumerateSnapshotEntries())
        {
            if (entry.Key != OwnedAgentStateKey.Name) continue;
            if (entry.Value is not string text || string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("owned_agent_checkpoint_payload_type_invalid");
            json = text;
            break;
        }
        if (json is null) return null;
        try
        {
            var saved = JsonSerializer.Deserialize(json,
                OwnedNpcCheckpointJsonContext.Default.OwnedNpcAgentSaveState)
                ?? throw new InvalidDataException("owned_agent_checkpoint_missing_payload");
            saved.Validate();
            return saved;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("owned_agent_checkpoint_json_invalid", exception);
        }
    }

    public void ClearOwnedAgentState()
    {
        world.Bb.Remove(OwnedAgentStateKey);
    }
}
