using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Runtime.Commands;

namespace Aurelian.Runtime;

/// <summary>
/// A deliberately tiny Dominatus runtime smoke harness for Aurelian's A1 spine milestone.
/// </summary>
public static class DominatusSmokeRuntime
{
    private const float SmokeDeltaSeconds = 1f / 60f;
    private const string RootStateName = "aurelian.smoke.root";
    private const string LogMessage = "aurelian-dominatus-smoke";

    private static readonly BbKey<ActuationId> ActuationIdKey = new("aurelian.smoke.actuationId");
    private static readonly BbKey<LogCommand> LogPayloadKey = new("aurelian.smoke.logPayload");

    public static DominatusSmokeResult TickOnce()
    {
        var actuator = new ActuatorHost();
        actuator.Register(new LogHandler());

        var world = new AiWorld(actuator);
        var root = StateId.Of(RootStateName);
        var graph = new HfsmGraph { Root = root };
        graph.Add(root, SmokeNode);

        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(SmokeDeltaSeconds);

        var activePath = brain.GetActivePath();
        var actuationId = agent.Bb.GetOrDefault(ActuationIdKey, default);
        agent.Bb.TryGet(LogPayloadKey, out LogCommand? payload);

        var status = activePath.Count == 1
            && activePath[0].Equals(root)
            && actuationId.Value == 1
            && payload?.Message == LogMessage
                ? "SucceededThenReinitialized"
                : "Unexpected";

        return new DominatusSmokeResult(
            status,
            activePath.Count,
            activePath[^1].Value,
            agent.Id.Value,
            actuationId.Value,
            payload?.Message,
            world.Clock.Time);
    }

    public static string TickOnceAndReport()
    {
        var result = TickOnce();
        return $"{result.Status};frames={result.ActiveFrameCount};leaf={result.LeafState};agent={result.AgentId};act={result.ActuationId};payload={result.PayloadMessage};time={result.ClockSeconds:0.000000}";
    }

    private static IEnumerator<AiStep> SmokeNode(AiCtx ctx)
    {
        yield return new Act(new LogCommand(LogMessage), ActuationIdKey);
        yield return new AwaitActuation<LogCommand>(ActuationIdKey, LogPayloadKey);
        yield return new Succeed("AurelianDominatusSmokeComplete");
    }
}

public sealed record DominatusSmokeResult(
    string Status,
    int ActiveFrameCount,
    string LeafState,
    int AgentId,
    long ActuationId,
    string? PayloadMessage,
    float ClockSeconds);
