using Aurelian.Runtime.Inspection;
using Aurelian.Runtime.Dominatus.Inspection;
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
    internal static readonly BbKey<string> Intent = new("Beacon.Decision.Intent");
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
        context.Bb.Set(Intent, nameof(CreatureIntent.Chase));
        yield return Ai.Succeed();
    }

    [DominatusState("Attack")]
    private static IEnumerator<AiStep> Attack(AiCtx context)
    {
        context.Bb.Set(Intent, nameof(CreatureIntent.Attack));
        yield return Ai.Succeed();
    }
}

public sealed class BeaconCreatureBrains(int traceCapacity = 0)
{
    private readonly AurelianAgentRuntime runtime = new(traceCapacity);

    public int Count => runtime.Count;
    public AgentInspection Inspect() => runtime.Inspector.Observe();

    public void Add(string id)
    {
        runtime.Add(id, BeaconCreatureFlow.Definition.CreateBrain());
    }

    public void Observe(string id, float distance)
    {
        runtime.Agent(id).Bb.Set(BeaconCreatureFlow.Distance, distance);
    }

    public void Remove(string id)
    {
        runtime.Remove(id);
    }

    public void Tick(float seconds)
    {
        runtime.Tick(TimeSpan.FromSeconds(seconds));
    }

    public CreatureIntent Intent(string id)
    {
        string intent = runtime.Agent(id).Bb.GetOrDefault(BeaconCreatureFlow.Intent, nameof(CreatureIntent.Hold));
        return Enum.Parse<CreatureIntent>(intent);
    }
}
