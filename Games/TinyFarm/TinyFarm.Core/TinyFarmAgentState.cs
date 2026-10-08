using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public enum TinyFarmAgentKind
{
    Character,
    Object
}

public enum TinyFarmAgentControl
{
    Human,
    Schedule,
    Idle
}

public enum TinyFarmAgentSprite
{
    Gardener,
    Mara,
    ObjectMarker,
    Chest
}

public enum TinyFarmObjectPose
{
    Closed,
    Open
}

public sealed record TinyFarmAgentHealth(int Current, int Maximum);

public sealed record TinyFarmAgentAppearance(
    TinyFarmAgentSprite OverworldSprite,
    string? ConversationSprite = null,
    bool WalkingAnimation = true,
    int ScalePercent = 100);

/// <summary>
/// Agent-local authored state. Identity, inventory and position remain in their existing owners.
/// Legacy agents omit this extension; their existing slice health/loadout are not duplicated here.
/// </summary>
public sealed record TinyFarmAgentState(
    string TemplateId,
    TinyFarmAgentKind Kind,
    TinyFarmAgentControl Control,
    TinyFarmAgentHealth? Health,
    int Level,
    IReadOnlyList<string> Conditions,
    TinyFarmEquipment Equipment,
    TinyFarmObjectPose? ObjectPose,
    TinyFarmAgentAppearance Appearance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TinyFarmContainerState? Container = null);

public static class TinyFarmAgentPolicy
{
    public static bool IsScheduled(ActorState actor)
    {
        return !actor.IsPlayer && (actor.Agent is null || actor.Agent.Control == TinyFarmAgentControl.Schedule);
    }

    public static bool IsObject(ActorState actor)
    {
        return actor.Agent?.Kind == TinyFarmAgentKind.Object;
    }
}

[JsonSerializable(typeof(TinyFarmAgentState))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TinyFarmAgentJsonContext : JsonSerializerContext;
