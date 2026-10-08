using Aurelian.Runtime.Dominatus;
using Aurelian.Runtime.Sessions;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Aurelian.Beacon3D;

public enum CreatureIntent
{
    Hold,
    Chase,
    Attack,
}

/// <summary>A live Dominatus brain chooses intents from observations, never writes arena state.</summary>
public static partial class BeaconCreatureFlow
{
    internal static readonly BbKey<float> Distance = new("Beacon.Observation.Distance");
    internal static readonly BbKey<CreatureIntent> Intent = new("Beacon.Decision.Intent");
    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("beacon.creature", KeepRootFrame = true)]
    public static partial FlowDefinition Define();

    [DominatusState("Observe", Root = true)]
    private static IEnumerator<AiStep> Observe(AiCtx context)
    {
        while (true)
        {
            yield return Ai.Decide(new DecisionSlot("Beacon.CreatureAction"),
            [
                Ai.Option("Attack", new Consideration((_, agent) =>
                    agent.Bb.GetOrDefault(Distance, float.MaxValue) < 1.25f ? 1 : 0), States.Attack),
                Ai.Option("Chase", Consideration.Constant(0.5f), States.Chase),
            ], hysteresis: 0, minCommitSeconds: 0, tieEpsilon: 0.0001f);
        }
    }

    [DominatusState("Chase")]
    private static IEnumerator<AiStep> Chase(AiCtx context)
    {
        context.Bb.Set(Intent, CreatureIntent.Chase);
        yield return Ai.Succeed();
    }

    [DominatusState("Attack")]
    private static IEnumerator<AiStep> Attack(AiCtx context)
    {
        context.Bb.Set(Intent, CreatureIntent.Attack);
        yield return Ai.Succeed();
    }
}

public sealed class BeaconCreatureBrains
{
    private readonly AiWorld world = new();
    private readonly Dictionary<string, AiAgent> agents = new(StringComparer.Ordinal);
    private readonly SequentialAurelianDominatusWorldRunner runner = new();
    private ulong tick;

    public int Count => agents.Count;

    public void Add(string id)
    {
        var agent = new AiAgent(BeaconCreatureFlow.Definition.CreateBrain());
        agents.Add(id, agent);
        world.Add(agent);
    }

    public void Observe(string id, float distance)
    {
        agents[id].Bb.Set(BeaconCreatureFlow.Distance, distance);
    }

    public void Remove(string id)
    {
        if (agents.Remove(id, out var agent))
        {
            world.Remove(agent);
        }
    }

    public void Tick(float seconds)
    {
        runner.RunTickAsync(world, new AurelianRuntimeTickInput(++tick, TimeSpan.FromSeconds(seconds)))
            .GetAwaiter().GetResult();
    }

    public CreatureIntent Intent(string id)
    {
        return agents[id].Bb.GetOrDefault(BeaconCreatureFlow.Intent, CreatureIntent.Hold);
    }
}
