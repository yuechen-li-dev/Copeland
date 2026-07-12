using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aurelian.Rendering.Contracts.Compositor;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Compositor;

public static class CompositorPolicySession
{
    private const float PolicyDeltaSeconds = 1f / 60f;
    private const string RootStateName = "aurelian.compositor.policy.m0.root";
    private const string ReasonDispatchPassthrough = "DispatchPassthrough";
    private const string ReasonRequiredOutputsNotReady = "RequiredOutputsNotReady";
    private const string ReasonUnsupportedPolicy = "UnsupportedPolicy";

    public static CompositorPolicyDecision Decide(CompositorPolicyFacts facts)
    {
        if (facts.RequestedPolicy != CompositorPolicyKind.Passthrough)
        {
            return new CompositorPolicyDecision(
                facts.RequestedPolicy,
                ShouldDispatch: false,
                Request: null,
                ReasonUnsupportedPolicy);
        }

        if (facts.RequiredOutputs.Policy != CompositorPolicyKind.Passthrough)
        {
            return new CompositorPolicyDecision(
                facts.RequiredOutputs.Policy,
                ShouldDispatch: false,
                Request: null,
                ReasonUnsupportedPolicy);
        }

        if (!facts.RequiredOutputs.IsSatisfiedBy(facts.FrameFacts.Outputs))
        {
            return new CompositorPolicyDecision(
                CompositorPolicyKind.Passthrough,
                ShouldDispatch: false,
                Request: null,
                ReasonRequiredOutputsNotReady);
        }

        var request = new CompositorDispatchRequest(
            facts.FrameFacts.FrameId,
            CompositorPolicyKind.Passthrough,
            facts.RequiredOutputs.RequiredOutputs,
            facts.Target);

        return new CompositorPolicyDecision(
            CompositorPolicyKind.Passthrough,
            ShouldDispatch: true,
            request,
            ReasonDispatchPassthrough);
    }

    public static Task<CompositorPolicyResult> RunOnceAsync(
        CompositorPolicyFacts facts,
        ActuatorHost actuatorHost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(actuatorHost);

        var decision = Decide(facts);
        if (decision.Reason == ReasonUnsupportedPolicy)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Rejected,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.UnsupportedPolicy, $"Compositor policy '{decision.Policy}' is not supported by M0.")]));
        }

        if (!decision.ShouldDispatch || decision.Request is null)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.WaitingForOutputs,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.RequiredOutputsNotReady, "Required passthrough plant outputs are not ready or reusable.", CompositorPolicyDiagnosticSeverity.Info)]));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchActFailed, "Compositor policy session was canceled before dispatch.")]));
        }

        var world = new AiWorld(actuatorHost);
        var root = StateId.Of(RootStateName);
        var graph = new HfsmGraph { Root = root };
        graph.Add(root, PolicyNode);

        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);
        agent.Bb.Set(CompositorPolicyKeys.Facts, facts);
        agent.Bb.Set(CompositorPolicyKeys.Decision, decision);
        world.Add(agent);

        world.Tick(PolicyDeltaSeconds);

        var actuationId = agent.Bb.GetOrDefault(CompositorPolicyKeys.DispatchActuationId, default);
        if (actuationId.Value == 0)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchActFailed, "Dominatus policy node did not emit a compositor dispatch act.")]));
        }

        if (!TryReadCompletion(agent, actuationId, out var completion))
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchActFailed, "Dominatus dispatch act did not complete during the M0 policy tick.")]));
        }

        if (!completion.Ok)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchActFailed, completion.Error ?? "Compositor dispatch act failed.")]));
        }

        if (!agent.Bb.TryGet(CompositorPolicyKeys.DispatchResult, out CompositorDispatchResult? dispatchResult)
            || dispatchResult is null)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                DispatchResult: null,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchResultFailed, "Compositor dispatch act completed without a neutral dispatch result payload.")]));
        }

        if (!dispatchResult.Success)
        {
            return Task.FromResult(new CompositorPolicyResult(
                CompositorPolicyStatus.Failed,
                decision,
                dispatchResult,
                [Diagnostic(CompositorPolicyDiagnosticCodes.DispatchResultFailed, "Compositor dispatch result reported failure.")]));
        }

        return Task.FromResult(new CompositorPolicyResult(
            CompositorPolicyStatus.Dispatched,
            decision,
            dispatchResult,
            []));
    }

    private static IEnumerator<AiStep> PolicyNode(AiCtx ctx)
    {
        var decision = ctx.Agent.Bb.GetOrDefault(CompositorPolicyKeys.Decision, default!);
        if (decision?.Request is null)
        {
            yield return new Fail("No compositor dispatch request was decided.");
            yield break;
        }

        yield return new Act(new CompositorDispatchAct(decision.Request), CompositorPolicyKeys.DispatchActuationId);
        yield return new AwaitActuation<CompositorDispatchResult>(CompositorPolicyKeys.DispatchActuationId, CompositorPolicyKeys.DispatchResult);
        yield return new Succeed("AurelianCompositorPolicyM0Complete");
    }

    private static bool TryReadCompletion(AiAgent agent, ActuationId actuationId, out ActuationCompleted completion)
    {
        var cursor = default(EventCursor);
        return agent.Events.TryConsume(ref cursor, (ActuationCompleted e) => e.Id.Equals(actuationId), out completion);
    }

    private static CompositorPolicyDiagnostic Diagnostic(
        string code,
        string message,
        CompositorPolicyDiagnosticSeverity severity = CompositorPolicyDiagnosticSeverity.Error)
        => new(code, severity, message);
}
