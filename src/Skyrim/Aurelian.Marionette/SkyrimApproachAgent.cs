using Aurelian.Actuation.Host;
using Marionette.Skyrim;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Marionette.Skyrim.App;

public sealed record ReachTargetGoal(
    HostPosition3 TargetPosition,
    float StoppingDistance,
    bool RelativeToBoundPosition = false);

public sealed record ApproachTargetOption(
    float MaximumDistance,
    HostMovementSpeedPolicy SpeedPolicy);

public sealed record SkyrimBodyAgentDefinition(
    AurelianAgentId Id,
    BodyId Body,
    ulong CandidateGeneration,
    ReachTargetGoal Goal,
    ApproachTargetOption Option)
{
    public SkyrimBodyAgentRuntime CreateRuntime(IHostPresenterBackend backend)
    {
        return new SkyrimBodyAgentRuntime(this, backend);
    }
}

public static class SkyrimAgent
{
    public static SkyrimBodyAgentDefinition Define(
        AurelianAgentId id,
        BodyId body,
        ulong candidateGeneration,
        ReachTargetGoal goal,
        ApproachTargetOption option)
    {
        if (candidateGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateGeneration),
                "Candidate generation must be nonzero.");
        }

        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(option);
        return new SkyrimBodyAgentDefinition(id, body, candidateGeneration, goal, option);
    }
}

public enum SkyrimBodyAgentState
{
    Unbound,
    RequestBinding,
    BoundIdle,
    ApproachTarget,
    ReleaseBinding,
    Completed,
    Failed,
    RestoreRequired,
}

public sealed record BindBodyActuationCommand(HostCommandRequest Request) : IActuationCommand;

public sealed record MoveBodyTowardActuationCommand(HostCommandRequest Request) : IActuationCommand;

public sealed record ReleaseBodyActuationCommand(HostCommandRequest Request) : IActuationCommand;

public sealed class SkyrimBodyAgentRuntime
{
    internal static readonly BbKey<BodyCommandResult> BindingResultKey = new(
        "Marionette.SkyrimBodyAgent.BindingResult");
    internal static readonly BbKey<BodyCommandResult> MovementResultKey = new(
        "Marionette.SkyrimBodyAgent.MovementResult");
    internal static readonly BbKey<BodyCommandResult> ReleaseResultKey = new(
        "Marionette.SkyrimBodyAgent.ReleaseResult");

    private static readonly BbKey<SkyrimBodyAgentState> StateKey = new(
        "Marionette.SkyrimBodyAgent.State");
    private static readonly OperationSite<BodyCommandResult> BindBodySite = Operation.Site<BodyCommandResult>(
        "aurelian.skyrim.body.bind.v1");
    private static readonly OperationSite<BodyCommandResult> MoveBodySite = Operation.Site<BodyCommandResult>(
        "aurelian.skyrim.body.move-toward.v1");
    private static readonly OperationSite<BodyCommandResult> ReleaseBodySite = Operation.Site<BodyCommandResult>(
        "aurelian.skyrim.body.release.v1");

    private readonly SkyrimBodyAgentDefinition definition;
    private readonly AiWorld world;
    private readonly AiAgent agent;
    private readonly Dominatus.Core.Hfsm.HfsmInstance brain;
    private readonly DecisionTrace trace = new();
    private BodyBinding? binding;
    private BodyObservation? body;
    private HostPosition3? resolvedTarget;
    private bool releaseAfterFailure;

    internal SkyrimBodyAgentRuntime(
        SkyrimBodyAgentDefinition definition,
        IHostPresenterBackend backend)
    {
        this.definition = definition;
        ArgumentNullException.ThrowIfNull(backend);

        var host = new ActuatorHost();
        host.Register(new BindHandler(backend));
        host.Register(new MoveHandler(backend));
        host.Register(new ReleaseHandler(backend));
        world = new AiWorld(host);

        FlowDefinition generatedFlow = SkyrimBodyAgentFlow.Define(this);
        brain = generatedFlow.CreateBrain();
        brain.Trace = trace;
        agent = new AiAgent(brain);
        agent.Bb.Set(StateKey, SkyrimBodyAgentState.Unbound);
        world.Add(agent);
    }

    public SkyrimBodyAgentState State => agent.Bb.GetOrDefault(
        StateKey,
        SkyrimBodyAgentState.Unbound);

    public string? SelectedOption => trace.LastDecision?.BestId;

    public DecisionReport? Decision => trace.LastDecision;

    public BodyBinding? Binding => binding;

    public BodyObservation? Body => body;

    public HostPosition3? ResolvedTarget => resolvedTarget;

    public BodyCommandResult? BindingResult => GetResult(BindingResultKey);

    public BodyCommandResult? MovementResult => GetResult(MovementResultKey);

    public BodyCommandResult? ReleaseResult => GetResult(ReleaseResultKey);

    public FlowInspection FlowInspection => SkyrimBodyAgentFlow.Define(this).Inspect();

    public static IReadOnlyList<OperationSiteInspection> OperationSites { get; } =
    [
        BindBodySite.Inspect(typeof(BindBodyActuationCommand)),
        MoveBodySite.Inspect(typeof(MoveBodyTowardActuationCommand)),
        ReleaseBodySite.Inspect(typeof(ReleaseBodyActuationCommand)),
    ];

    public SkyrimBodyAgentState RunUntilTerminal(int maximumTicks = 96)
    {
        for (int index = 0; index < maximumTicks; index++)
        {
            try
            {
                world.Tick(0.01f);
            }
            catch (InvalidOperationException exception)
            {
                string path = string.Join(" > ", brain.GetActivePath().Select(id => id.Value));
                throw new InvalidOperationException(
                    $"Skyrim body-agent flow failed at '{path}' (pendingReturn={brain.HasPendingChildReturn}).",
                    exception);
            }
            if (State is SkyrimBodyAgentState.Completed
                or SkyrimBodyAgentState.Failed
                or SkyrimBodyAgentState.RestoreRequired)
            {
                return State;
            }
        }

        throw new TimeoutException("The Skyrim body agent did not reach a terminal state.");
    }

    public void Tick()
    {
        world.Tick(0.01f);
    }

    internal HostCommandRequest CreateBindCommand()
    {
        return new HostCommandRequest(
            RequestId(1),
            definition.CandidateGeneration,
            HostCommandKind.BindBody,
            TimeSpan.FromSeconds(2),
            new BindBodyArguments(
                definition.Id,
                definition.Body,
                BodyBindingKind.ExclusiveControl,
                definition.CandidateGeneration));
    }

    internal HostCommandRequest CreateMoveCommand()
    {
        BodyObservation current = body
            ?? throw new InvalidOperationException("A body observation is required before movement.");
        BodyBinding currentBinding = binding
            ?? throw new InvalidOperationException("A binding is required before movement.");
        return new HostCommandRequest(
            RequestId(2),
            currentBinding.Generation,
            HostCommandKind.MoveBodyToward,
            TimeSpan.FromSeconds(2),
            new MoveBodyTowardArguments(
                definition.Id,
                definition.Body,
                resolvedTarget ?? definition.Goal.TargetPosition,
                definition.Goal.StoppingDistance,
                definition.Option.MaximumDistance,
                definition.Option.SpeedPolicy,
                currentBinding.Generation,
                current.Sequence));
    }

    internal HostCommandRequest CreateReleaseCommand()
    {
        BodyBinding currentBinding = binding
            ?? throw new InvalidOperationException("A binding is required before release.");
        return new HostCommandRequest(
            RequestId(3),
            currentBinding.Generation,
            HostCommandKind.ReleaseBody,
            TimeSpan.FromSeconds(2),
            new ReleaseBodyArguments(
                definition.Id,
                definition.Body,
                currentBinding.Generation));
    }

    internal void SetState(AiCtx context, SkyrimBodyAgentState state)
    {
        context.Bb.Set(StateKey, state);
    }

    internal void Accept(BodyCommandResult result)
    {
        binding = result.Binding?.Binding ?? binding;
        body = result.Body ?? body;
        if (resolvedTarget is null && body is not null)
        {
            resolvedTarget = definition.Goal.RelativeToBoundPosition
                ? new HostPosition3(
                    body.Position.X + definition.Goal.TargetPosition.X,
                    body.Position.Y + definition.Goal.TargetPosition.Y,
                    body.Position.Z + definition.Goal.TargetPosition.Z)
                : definition.Goal.TargetPosition;
        }
    }

    internal bool BodyLost()
    {
        return body is null
            || !body.IsLoaded
            || binding?.State is BodyBindingState.Lost or BodyBindingState.RestoreRequired;
    }

    internal bool GoalReached()
    {
        return body is not null
            && resolvedTarget.HasValue
            && body.Position.DistanceTo(resolvedTarget.Value)
                <= definition.Goal.StoppingDistance;
    }

    internal bool CanMove()
    {
        return body is not null
            && binding?.State == BodyBindingState.Bound
            && body.IsLoaded
            && body.IsAlive
            && body.Capabilities.CanMove
            && !GoalReached();
    }

    internal void MarkReleaseAfterFailure()
    {
        releaseAfterFailure = true;
    }

    internal bool ReleaseAfterFailure => releaseAfterFailure;

    internal static OperationSite<BodyCommandResult> BindSite => BindBodySite;

    internal static OperationSite<BodyCommandResult> MoveSite => MoveBodySite;

    internal static OperationSite<BodyCommandResult> ReleaseSite => ReleaseBodySite;

    private BodyCommandResult? GetResult(BbKey<BodyCommandResult> key)
    {
        return agent.Bb.TryGet(key, out BodyCommandResult? result) ? result : null;
    }

    private Guid RequestId(byte operation)
    {
        byte[] bytes = definition.Id.Value.ToByteArray();
        bytes[^1] ^= operation;
        return new Guid(bytes);
    }

    private static ActuatorHost.HandlerResult Execute(
        IHostPresenterBackend backend,
        HostCommandRequest request)
    {
        HostActionResult action = HostActionRunner.ExecuteAsync(
            backend,
            request,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();
        BodyCommandResult result = action.BodyResult ?? new BodyCommandResult(
            request.RequestId,
            action.State,
            action.FailureReason);

        // Command-level failure is authored data. The operation itself completed
        // and the flow chooses Succeed/Fail explicitly from the typed payload.
        return ActuatorHost.HandlerResult.CompletedWithPayload(result, ok: true);
    }

    private sealed class BindHandler : IActuationHandler<BindBodyActuationCommand>
    {
        private readonly IHostPresenterBackend backend;

        public BindHandler(IHostPresenterBackend backend)
        {
            this.backend = backend;
        }

        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx context,
            ActuationId id,
            BindBodyActuationCommand command)
        {
            return Execute(backend, command.Request);
        }
    }

    private sealed class MoveHandler : IActuationHandler<MoveBodyTowardActuationCommand>
    {
        private readonly IHostPresenterBackend backend;

        public MoveHandler(IHostPresenterBackend backend)
        {
            this.backend = backend;
        }

        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx context,
            ActuationId id,
            MoveBodyTowardActuationCommand command)
        {
            return Execute(backend, command.Request);
        }
    }

    private sealed class ReleaseHandler : IActuationHandler<ReleaseBodyActuationCommand>
    {
        private readonly IHostPresenterBackend backend;

        public ReleaseHandler(IHostPresenterBackend backend)
        {
            this.backend = backend;
        }

        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx context,
            ActuationId id,
            ReleaseBodyActuationCommand command)
        {
            return Execute(backend, command.Request);
        }
    }

    private sealed class DecisionTrace : IAiTraceSink
    {
        public DecisionReport? LastDecision { get; private set; }

        public void OnEnter(StateId state, float time, string reason) { }

        public void OnExit(StateId state, float time, string reason) { }

        public void OnTransition(StateId from, StateId to, float time, string reason) { }

        public void OnYield(StateId state, float time, object yielded)
        {
            if (yielded is DecisionReport report)
            {
                LastDecision = report;
            }
        }
    }
}

public static partial class SkyrimBodyAgentFlow
{
    [DominatusFlow("aurelian.skyrim.body-agent.m1")]
    public static partial FlowDefinition Define(SkyrimBodyAgentRuntime runtime);

    [DominatusState("aurelian.skyrim.body-agent.root", Root = true)]
    private static IEnumerator<AiStep> Root(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.Unbound);
        yield return Ai.Push(States.RequestBinding, "acquire materialized body");
        yield return Ai.MatchReturn(
            Ai.OnSuccess(States.BoundIdle),
            Ai.OnFailure(States.Failed),
            Ai.OnReturn(States.Failed));
    }

    [DominatusState("aurelian.skyrim.body-agent.request-binding")]
    private static IEnumerator<AiStep> RequestBinding(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.RequestBinding);
        yield return Ai.Perform(
            SkyrimBodyAgentRuntime.BindSite,
            new BindBodyActuationCommand(runtime.CreateBindCommand()),
            SkyrimBodyAgentRuntime.BindingResultKey);
        BodyCommandResult result = context.Bb.GetOrDefault(
            SkyrimBodyAgentRuntime.BindingResultKey,
            new BodyCommandResult(Guid.Empty, HostActionState.Failed, "binding_result_missing"));
        runtime.Accept(result);
        if (result.Completed && result.Binding?.Binding.State == BodyBindingState.Bound)
        {
            yield return Ai.Succeed("body binding observed");
        }
        else
        {
            yield return Ai.Fail(result.FailureReason ?? "body binding failed");
        }
    }

    [DominatusState("aurelian.skyrim.body-agent.bound-idle")]
    private static IEnumerator<AiStep> BoundIdle(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.BoundIdle);
        yield return Ai.Decide(
        [
            Ai.Option(
                "BodyLost",
                Consideration.FromBool((_, _) => runtime.BodyLost()),
                States.ReleaseAfterFailure),
            Ai.Option(
                "GoalReached",
                Consideration.FromBool((_, _) => runtime.GoalReached()),
                States.ReleaseSuccess),
            Ai.Option(
                "MoveToward",
                Consideration.FromBool((_, _) => runtime.CanMove()),
                States.ApproachParent),
            Ai.Option(
                "CannotAct",
                Consideration.Constant(0.1f),
                States.ReleaseAfterFailure),
        ],
        hysteresis: 0.0f,
        minCommitSeconds: 0.0f);
    }

    [DominatusState("aurelian.skyrim.body-agent.approach-parent")]
    private static IEnumerator<AiStep> ApproachParent(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        yield return Ai.Push(States.ApproachTarget, "perform owned movement");
        yield return Ai.MatchReturn(
            Ai.OnSuccess(States.ReleaseSuccess),
            Ai.OnFailure(States.ReleaseAfterFailure),
            Ai.OnReturn(States.ReleaseAfterFailure));
    }

    [DominatusState("aurelian.skyrim.body-agent.approach-target")]
    private static IEnumerator<AiStep> ApproachTarget(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.ApproachTarget);
        yield return Ai.Perform(
            SkyrimBodyAgentRuntime.MoveSite,
            new MoveBodyTowardActuationCommand(runtime.CreateMoveCommand()),
            SkyrimBodyAgentRuntime.MovementResultKey);
        BodyCommandResult result = context.Bb.GetOrDefault(
            SkyrimBodyAgentRuntime.MovementResultKey,
            new BodyCommandResult(Guid.Empty, HostActionState.Failed, "movement_result_missing"));
        runtime.Accept(result);
        if (result.Completed)
        {
            yield return Ai.Succeed("movement completion observed");
        }
        else
        {
            runtime.MarkReleaseAfterFailure();
            yield return Ai.Fail(result.FailureReason ?? "movement failed");
        }
    }

    [DominatusState("aurelian.skyrim.body-agent.release-success")]
    private static IEnumerator<AiStep> ReleaseSuccess(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.ReleaseBinding);
        yield return Ai.Perform(
            SkyrimBodyAgentRuntime.ReleaseSite,
            new ReleaseBodyActuationCommand(runtime.CreateReleaseCommand()),
            SkyrimBodyAgentRuntime.ReleaseResultKey);
        BodyCommandResult result = context.Bb.GetOrDefault(
            SkyrimBodyAgentRuntime.ReleaseResultKey,
            new BodyCommandResult(Guid.Empty, HostActionState.Failed, "release_result_missing"));
        runtime.Accept(result);
        yield return result.Completed
            ? Ai.Goto(States.Completed, "body released")
            : Ai.Goto(States.RestoreRequired, "release failed");
    }

    [DominatusState("aurelian.skyrim.body-agent.release-after-failure")]
    private static IEnumerator<AiStep> ReleaseAfterFailure(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.MarkReleaseAfterFailure();
        runtime.SetState(context, SkyrimBodyAgentState.ReleaseBinding);
        yield return Ai.Perform(
            SkyrimBodyAgentRuntime.ReleaseSite,
            new ReleaseBodyActuationCommand(runtime.CreateReleaseCommand()),
            SkyrimBodyAgentRuntime.ReleaseResultKey);
        BodyCommandResult result = context.Bb.GetOrDefault(
            SkyrimBodyAgentRuntime.ReleaseResultKey,
            new BodyCommandResult(Guid.Empty, HostActionState.Failed, "release_result_missing"));
        runtime.Accept(result);
        yield return result.Completed
            ? Ai.Goto(States.Failed, "body released after authored failure")
            : Ai.Goto(States.RestoreRequired, "release failed");
    }

    [DominatusState("aurelian.skyrim.body-agent.completed")]
    private static IEnumerator<AiStep> Completed(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.Completed);
        while (true)
        {
            yield return Ai.Steady("agent goal complete and body released");
        }
    }

    [DominatusState("aurelian.skyrim.body-agent.failed")]
    private static IEnumerator<AiStep> Failed(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.Failed);
        while (true)
        {
            yield return Ai.Steady("authored body-agent failure");
        }
    }

    [DominatusState("aurelian.skyrim.body-agent.restore-required")]
    private static IEnumerator<AiStep> RestoreRequired(AiCtx context, SkyrimBodyAgentRuntime runtime)
    {
        runtime.SetState(context, SkyrimBodyAgentState.RestoreRequired);
        while (true)
        {
            yield return Ai.Steady("backend restoration outcome is uncertain");
        }
    }

}
