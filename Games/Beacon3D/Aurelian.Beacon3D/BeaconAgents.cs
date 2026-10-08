using System.Numerics;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.Beacon3D;

public sealed record BeaconAgentState(Vector3 Position, int Health, bool Collected = false, float Cooldown = 0);
public sealed record BeaconBolt(Vector3 Position, Vector3 Velocity, float Life);

public sealed record BeaconAgentDefinition(AgentTemplate AgentTemplate, int MaximumHealth)
    : AgentDefinition<BeaconAgentState>(AgentTemplate)
{
    public string PolicyIdentity { get; init; } = "none";
    public Func<SceneAgent<BeaconAgentState>, IDisposable?>? AttachPolicy { get; init; }
    public override string Identity => $"{Template.Id}.v1:{MaximumHealth}:{PolicyIdentity}";
    public override BeaconAgentState CreateState(ScenePlacement placement) => new(placement.Position, MaximumHealth);
    public override Matrix4x4 WorldTransform(BeaconAgentState state, ScenePlacement placement) => placement.At(state.Position);

    public override void ValidatePlacement(ScenePlacement placement)
    {
        BeaconAgents.ValidatePosition(placement.Position);
        if (MaximumHealth < 0 || Template.Kind != AgentKind.Object && MaximumHealth == 0)
        {
            throw new InvalidDataException("Characters and creatures need positive health.");
        }
    }

    public override IDisposable? Activate(SceneAgent<BeaconAgentState> agent) => AttachPolicy?.Invoke(agent);
}

/// <summary>Authored agent creation used by the arena; no renderer-owned gameplay identities.</summary>
public static class BeaconAgents
{
    public static readonly AgentTemplate Player = AgentTemplate.Character("beacon.runner", AgentControl.Human);
    public static readonly AgentTemplate Beacon = AgentTemplate.Object("beacon.collectible");
    public static readonly AgentTemplate Creature = AgentTemplate.Creature("beacon.stalker");

    public static IReadOnlyList<GameAgent<BeaconAgentState>> Create(IEnumerable<AgentSpawn<Vector3>> declarations,
        IEnumerable<string> existingIds)
    {
        return AgentAuthoring.CreateBatch(declarations, existingIds, ValidatePosition, spawn =>
        {
            int health = spawn.Template.Kind switch
            {
                AgentKind.Character => 100,
                AgentKind.Creature => 2,
                _ => 0,
            };
            return new BeaconAgentState(spawn.Placement, health);
        });
    }

    internal static void ValidatePosition(Vector3 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
            || point.Y < 0 || !BeaconGame.CanStand(new Vector2(point.X, point.Z)))
        {
            throw new InvalidDataException("Agent placement must be finite, inside the arena, and clear of pillars.");
        }
    }
}
