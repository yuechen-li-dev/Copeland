using Aurelian.Combat;
using System.Numerics;
using Aurelian.World.Agents;
using Aurelian.Runtime.Inspection;
using Aurelian.World.Scenes;
using SceneDocument = Aurelian.World.Scenes.Scene;

namespace Aurelian.Beacon3D;

public sealed partial class BeaconGame
{
    private readonly Dictionary<string, SceneAgent<BeaconAgentState>> agents = new(StringComparer.Ordinal);
    private readonly SceneInstance scene;
    private readonly BeaconAgentDefinition creatureDefinition;
    private readonly BeaconCreatureBrains brains;
    private readonly List<BeaconBolt> bolts = [];
    private readonly bool combat;
    private float nextWave = 1.5f;
    private readonly ReloadableGun gun = new();
    private float hurtRemaining;

    public BeaconGame(bool combat = true, int traceCapacity = 0)
    {
        this.combat = combat;
        brains = new BeaconCreatureBrains(traceCapacity);
        creatureDefinition = new(BeaconAgents.Creature, 2)
        {
            PolicyIdentity = "beacon.creature.v1",
            AttachPolicy = brains.Bind,
        };
        var nodes = new List<SceneNode>
        {
            SceneDocument.Agent("runner", new BeaconAgentDefinition(BeaconAgents.Player, 100), at: new(0, 0, 9), name: "Runner"),
        };
        for (int index = 0; index < BeaconPositions.Count; index++)
        {
            Vector2 point = BeaconPositions[index];
            nodes.Add(SceneDocument.Agent($"beacon-{index}", new BeaconAgentDefinition(BeaconAgents.Beacon, 0),
                at: new(point.X, 0, point.Y), name: "Beacon"));
        }
        var document = BeaconScene.Arena();
        document = document with { Children = document.Children.AddRange(nodes) };
        scene = SceneCompiler.Compile(document).Mount();
        foreach (SceneAgent agent in scene.Agents)
        {
            agents.Add(agent.Id, scene.Agent<BeaconAgentState>(agent.Id));
        }
    }

    public IReadOnlyList<GameAgent<BeaconAgentState>> Agents => agents.Values
        .OrderBy(agent => agent.Id, StringComparer.Ordinal).Select(agent => agent.Agent).ToArray();
    public SceneInstance Scene => scene;
    public IReadOnlyList<GameAgent<BeaconAgentState>> Creatures => Agents.Where(agent => agent.Template.Kind == AgentKind.Creature).ToArray();
    public IReadOnlyList<BeaconBolt> Bolts => bolts.AsReadOnly();
    public int Health => agents["runner"].State.Health;
    public bool Dead => Health == 0;
    public int Wave { get; private set; }
    public int WavesCleared { get; private set; }
    public int Kills { get; private set; }
    public int Shots => gun.State.Shots;
    public int Ammo => gun.State.Ammo;
    public bool Reloading => gun.State.ReloadRemaining > 0;
    public bool Hurt => hurtRemaining > 0;
    public bool GateOpen => CollectedCount == 3 && (!combat || WavesCleared == 3);
    public long AgentTicks { get; private set; }
    public int ActiveCreatureBrains => brains.Count;
    public AgentInspection InspectBrains() => brains.Inspect();
    public string CombatStatus => Reloading ? "RELOADING" : Ammo == 0 ? "R TO RELOAD" : "LMB FIRE / R RELOAD";

    private void SetPlayerPosition(Vector2 position)
    {
        var player = agents["runner"];
        player.State = player.State with { Position = new(position.X, 0, position.Y) };
    }

    private void Collect(int index)
    {
        string id = $"beacon-{index}";
        var agent = agents[id];
        agent.State = agent.State with { Collected = true };
    }

    private void StepCombat(BeaconInput input, float seconds)
    {
        hurtRemaining = MathF.Max(0, hurtRemaining - seconds);
        if (gun.Step(input.Fire, input.Reload, seconds))
        {
            bolts.Add(new BeaconBolt(Eye + Direction * 0.4f, Direction * 28, 1.5f));
        }
        StepBolts(seconds);
        var creatures = Creatures.Where(agent => agent.State.Health > 0).ToArray();
        if (creatures.Length == 0)
        {
            if (Wave > WavesCleared)
            {
                WavesCleared = Wave;
                nextWave = 2;
            }
            nextWave -= seconds;
            if (nextWave <= 0 && Wave < 3)
            {
                SpawnWave();
                creatures = Creatures.ToArray();
            }
        }
        foreach (var creature in creatures)
        {
            brains.Observe(creature.Id, Vector3.Distance(creature.State.Position, new Vector3(Position.X, 0, Position.Y)));
        }
        brains.Tick(seconds);
        AgentTicks += creatures.Length;
        foreach (var creature in creatures)
        {
            ResolveCreature(creature, brains.Intent(creature.Id), seconds);
        }
    }

    private void SpawnWave()
    {
        Wave++;
        foreach (string id in Creatures.Select(agent => agent.Id))
        {
            scene.Despawn(id);
            agents.Remove(id);
        }
        Vector2[] points = [new(-8, -8), new(8, -8), new(-8, 8), new(8, 8)];
        for (int index = 0; index < Wave + 1; index++)
        {
            Vector2 point = points[index];
            string id = $"creature-{Wave}-{index}";
            var agent = scene.Spawn(id, creatureDefinition, SceneTransform.At(new(point.X, 0, point.Y)), "Stalker");
            agents.Add(id, agent);
        }
    }

    private void ResolveCreature(GameAgent<BeaconAgentState> creature, CreatureIntent intent, float seconds)
    {
        var state = creature.State;
        Vector2 position = new(state.Position.X, state.Position.Z);
        Vector2 delta = Position - position;
        float cooldown = MathF.Max(0, state.Cooldown - seconds);
        if (intent == CreatureIntent.Attack && delta.Length() < 1.25f && Height < 0.8f && cooldown == 0)
        {
            var player = agents["runner"];
            player.State = player.State with { Health = Math.Max(0, Health - 10) };
            cooldown = 0.8f;
            hurtRemaining = 0.2f;
        }
        else if (intent == CreatureIntent.Chase && delta.LengthSquared() > 0.8f)
        {
            Vector2 direction = Vector2.Normalize(delta);
            Vector2 step = direction * (1.25f * seconds);
            Vector2 candidate = position + step;
            if (CanStand(candidate, 0.45f))
            {
                position = candidate;
            }
            else
            {
                // Local obstacle steering, then axis sliding. Both routes respect the same geometry.
                Vector2 tangent = new(-direction.Y, direction.X);
                Vector2 sideways = position + tangent * (1.25f * seconds);
                if (!CanStand(sideways, 0.45f))
                {
                    sideways = position - tangent * (1.25f * seconds);
                }
                if (CanStand(sideways, 0.45f))
                {
                    position = sideways;
                }
            }
        }
        agents[creature.Id].State = state with { Position = new(position.X, 0, position.Y), Cooldown = cooldown };
    }

    private void StepBolts(float seconds)
    {
        for (int index = bolts.Count - 1; index >= 0; index--)
        {
            BeaconBolt bolt = bolts[index];
            Vector3 end = bolt.Position + bolt.Velocity * seconds;
            Vector3 segment = end - bolt.Position;
            float length = segment.Length();
            Vector3 direction = segment / length;
            float nearest = WorldHit(bolt.Position, direction, length);
            GameAgent<BeaconAgentState>? victim = null;
            foreach (var creature in Creatures.Where(agent => agent.State.Health > 0))
            {
                float hit = RaySphere(bolt.Position, direction, creature.State.Position + Vector3.UnitY, 0.65f);
                if (hit >= 0 && hit <= length && hit < nearest)
                {
                    nearest = hit;
                    victim = creature;
                }
            }
            if (victim is not null)
            {
                int health = Math.Max(0, victim.State.Health - 1);
                agents[victim.Id].State = victim.State with { Health = health };
                if (health == 0)
                {
                    Kills++;
                    brains.Remove(victim.Id);
                }
            }
            if (victim is not null || nearest <= length || bolt.Life <= seconds)
            {
                bolts.RemoveAt(index);
            }
            else
            {
                bolts[index] = bolt with { Position = end, Life = bolt.Life - seconds };
            }
        }
    }

    public static float RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius)
    {
        Vector3 offset = origin - center;
        float b = Vector3.Dot(offset, direction);
        float c = offset.LengthSquared() - radius * radius;
        if (c <= 0)
        {
            return 0;
        }
        float discriminant = b * b - c;
        if (discriminant < 0)
        {
            return float.PositiveInfinity;
        }
        float distance = -b - MathF.Sqrt(discriminant);
        return distance >= 0 ? distance : float.PositiveInfinity;
    }

    public static float WorldHit(Vector3 origin, Vector3 direction, float maximum)
    {
        float result = float.PositiveInfinity;
        foreach (ArenaPillar pillar in Pillars)
        {
            Vector3 minimum = new(pillar.Center.X - pillar.HalfSize.X, 0, pillar.Center.Y - pillar.HalfSize.Y);
            Vector3 maximumPoint = new(pillar.Center.X + pillar.HalfSize.X, pillar.Height, pillar.Center.Y + pillar.HalfSize.Y);
            result = MathF.Min(result, RayBox(origin, direction, minimum, maximumPoint));
            Vector3 capMinimum = new(minimum.X - 0.15f, pillar.Height, minimum.Z - 0.15f);
            Vector3 capMaximum = new(maximumPoint.X + 0.15f, pillar.Height + 0.16f, maximumPoint.Z + 0.15f);
            result = MathF.Min(result, RayBox(origin, direction, capMinimum, capMaximum));
        }
        if (direction.Y < 0)
        {
            result = MathF.Min(result, -origin.Y / direction.Y);
        }
        Vector3 end = origin + direction * maximum;
        if (MathF.Abs(end.X) > 11 || MathF.Abs(end.Z) > 11)
        {
            // Boundary crossing terminates a bolt even above the low arena wall.
            result = MathF.Min(result, maximum);
        }
        return result;
    }

    private static float RayBox(Vector3 origin, Vector3 direction, Vector3 minimum, Vector3 maximum)
    {
        float near = 0;
        float far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float component = direction[axis];
            if (MathF.Abs(component) < 0.00001f)
            {
                if (origin[axis] < minimum[axis] || origin[axis] > maximum[axis])
                {
                    return float.PositiveInfinity;
                }
                continue;
            }
            float a = (minimum[axis] - origin[axis]) / component;
            float b = (maximum[axis] - origin[axis]) / component;
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far)
            {
                return float.PositiveInfinity;
            }
        }
        return near;
    }
}

