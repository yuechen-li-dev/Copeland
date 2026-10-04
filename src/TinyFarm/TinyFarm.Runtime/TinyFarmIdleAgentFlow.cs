using Dominatus.Core;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

/// <summary>The same generated passive brain can control a character or an object.</summary>
public static partial class TinyFarmIdleAgentFlow
{
    public static FlowDefinition Definition { get; } = Define();
    [DominatusFlow("tiny-farm.agent-idle", KeepRootFrame = true)]
    public static partial FlowDefinition Define();

    [DominatusState("Idle", Root = true)]
    private static IEnumerator<AiStep> Idle(AiCtx context)
    {
        while (true)
        {
            yield return Ai.Steady("idle");
        }
    }
}

internal sealed class TinyFarmIdleAgentRuntime
{
    private readonly Dictionary<ActorId, (AiWorld World, AiAgent Agent)> agents = [];

    public GameIntent Decide(ActorState actor)
    {
        if (!agents.TryGetValue(actor.Id, out var runtime))
        {
            var world = new AiWorld();
            var agent = new AiAgent(TinyFarmIdleAgentFlow.Definition.CreateBrain());
            world.Add(agent);
            runtime = (world, agent);
            agents.Add(actor.Id, runtime);
        }
        runtime.Agent.Tick(runtime.World);
        if (runtime.Agent.Brain.GetActivePath().Count == 0)
        {
            throw new InvalidOperationException($"Passive agent '{actor.Id}' lost its live Dominatus state.");
        }
        return new LookIntent();
    }

    public int Count => agents.Count;
}
