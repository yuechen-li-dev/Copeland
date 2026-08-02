using Aurelian.Actuation.Host;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;

namespace Aurelian.Marionette.Transport;

public sealed record SkyrimActorBinding(uint FormId, ulong RuntimeGeneration)
{
    public HostActorId ActorId => new(FormId, RuntimeGeneration);
}

public sealed record ReachTargetGoal(HostPosition3 TargetPosition, float StoppingDistance);

public sealed record ApproachTargetOption(
    float MaximumDistance,
    HostMovementSpeedPolicy SpeedPolicy,
    int MaximumRetries);

public sealed record SkyrimApproachAgentDefinition(
    string Id,
    SkyrimActorBinding Binding,
    ReachTargetGoal Goal,
    ApproachTargetOption Option)
{
    public SkyrimApproachAgentRuntime CreateRuntime(
        IHostPresenterBackend backend,
        HostActorObservation observation,
        Guid requestId)
    {
        return new SkyrimApproachAgentRuntime(this, backend, observation, requestId);
    }
}

/// <summary>
/// Deliberately small ordinary-C# authoring surface for the experiment. It
/// builds the repository's existing Dominatus HFSM/utility/actuation types.
/// </summary>
public static class SkyrimAgent
{
    public static SkyrimApproachAgentDefinition Define(
        string id,
        SkyrimActorBinding binding,
        ReachTargetGoal goal,
        ApproachTargetOption option)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(option);
        return new SkyrimApproachAgentDefinition(id, binding, goal, option);
    }
}

public enum SkyrimApproachTransition
{
    Deciding,
    AlreadyReached,
    Moving,
    Completed,
    Blocked,
    Failed,
    Unsupported,
}

public sealed record MoveTowardActuationCommand(HostCommandRequest Request) : IActuationCommand;

public sealed class SkyrimApproachAgentRuntime
{
    private static readonly StateId RootState = StateId.Of("Root");
    private static readonly StateId AlreadyReachedState = StateId.Of("AlreadyReached");
    private static readonly StateId MoveTowardState = StateId.Of("MoveToward");
    private static readonly StateId UnsupportedState = StateId.Of("Unsupported");
    private static readonly StateId FailedState = StateId.Of("Failed");
    private static readonly StateId CompletedState = StateId.Of("Completed");
    private static readonly StateId BlockedState = StateId.Of("Blocked");
    private static readonly BbKey<ActuationId> ActionIdKey = new("SkyrimApproach.ActionId");
    private static readonly BbKey<HostActionResult> ActionResultKey = new("SkyrimApproach.ActionResult");
    private static readonly BbKey<int> RetryCountKey = new("SkyrimApproach.RetryCount");
    private static readonly BbKey<SkyrimApproachTransition> TransitionKey = new("SkyrimApproach.Transition");

    private readonly SkyrimApproachAgentDefinition definition;
    private HostActorObservation observation;
    private readonly Guid requestId;
    private readonly AiWorld world;
    private readonly AiAgent agent;
    private readonly DecisionTrace trace = new();

    internal SkyrimApproachAgentRuntime(
        SkyrimApproachAgentDefinition definition,
        IHostPresenterBackend backend,
        HostActorObservation observation,
        Guid requestId)
    {
        this.definition = definition;
        this.observation = observation;
        this.requestId = requestId;

        var host = new ActuatorHost();
        host.Register(new MoveTowardHandler(backend));
        world = new AiWorld(host);

        var graph = new HfsmGraph { Root = RootState };
        graph.Add(new HfsmStateDef { Id = RootState, Node = Decide });
        graph.Add(new HfsmStateDef { Id = AlreadyReachedState, Node = AlreadyReached });
        graph.Add(new HfsmStateDef { Id = MoveTowardState, Node = MoveToward });
        graph.Add(new HfsmStateDef { Id = UnsupportedState, Node = Unsupported });
        graph.Add(new HfsmStateDef { Id = FailedState, Node = Failed });
        graph.Add(new HfsmStateDef { Id = CompletedState, Node = Completed });
        graph.Add(new HfsmStateDef { Id = BlockedState, Node = Blocked });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true })
        {
            Trace = trace,
        };
        agent = new AiAgent(brain);
        agent.Bb.Set(RetryCountKey, 0);
        agent.Bb.Set(TransitionKey, SkyrimApproachTransition.Deciding);
        world.Add(agent);
    }

    public string? SelectedOption => trace.FirstDecision?.BestId;

    public DecisionReport? Decision => trace.FirstDecision;

    public SkyrimApproachTransition Transition => agent.Bb.GetOrDefault(
        TransitionKey,
        SkyrimApproachTransition.Deciding);

    public HostActionResult? LastResult => agent.Bb.TryGet(ActionResultKey, out HostActionResult? result)
        ? result
        : null;

    public int RetryCount => agent.Bb.GetOrDefault(RetryCountKey, 0);

    public HostCommandRequest CreateCommand() => new(
        requestId,
        definition.Binding.RuntimeGeneration,
        HostCommandKind.MoveToward,
        TimeSpan.FromSeconds(2),
        new MoveTowardArguments(
            definition.Binding.ActorId,
            definition.Goal.TargetPosition,
            definition.Goal.StoppingDistance,
            definition.Option.MaximumDistance,
            definition.Option.SpeedPolicy,
            observation.Sequence));

    public SkyrimApproachTransition RunUntilTerminal(int maximumTicks = 64)
    {
        for (int index = 0; index < maximumTicks; index++)
        {
            world.Tick(0.01f);
            if (Transition is SkyrimApproachTransition.AlreadyReached
                or SkyrimApproachTransition.Completed
                or SkyrimApproachTransition.Blocked
                or SkyrimApproachTransition.Failed
                or SkyrimApproachTransition.Unsupported)
            {
                return Transition;
            }
        }

        throw new TimeoutException("Dominatus approach transition did not reach a terminal state.");
    }

    private IEnumerator<AiStep> Decide(AiCtx context)
    {
        while (true)
        {
            yield return Ai.Decide(
            [
                Ai.Option("TargetInvalid", Consideration.FromBool((_, _) => !TargetIsValid()), FailedState),
                Ai.Option("AlreadyReached", Consideration.FromBool((_, _) => IsAlreadyReached()), AlreadyReachedState),
                Ai.Option("MoveToward", Consideration.FromBool((_, _) => CanMove()), MoveTowardState),
                Ai.Option("Unsupported", Consideration.Constant(0.25f), UnsupportedState),
            ],
            hysteresis: 0.0f,
            minCommitSeconds: 0.0f);
        }
    }

    private IEnumerator<AiStep> AlreadyReached(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.AlreadyReached);
        while (true)
        {
            yield return Ai.Steady("target already within stopping distance");
        }
    }

    private IEnumerator<AiStep> MoveToward(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.Moving);
        yield return Ai.Act(new MoveTowardActuationCommand(CreateCommand()), ActionIdKey);
        yield return Ai.Await(ActionIdKey, ActionResultKey);

        HostActionResult result = context.Bb.GetOrDefault(
            ActionResultKey,
            new HostActionResult(requestId, HostActionState.Failed, "result_missing"));
        if (result.Observation is not null)
        {
            observation = result.Observation;
        }
        if (result.State == HostActionState.Completed)
        {
            context.Bb.Set(TransitionKey, SkyrimApproachTransition.Completed);
            yield return Ai.Goto(CompletedState, "movement completed");
            yield break;
        }

        if (result.State is HostActionState.Blocked or HostActionState.TimedOut)
        {
            int retryCount = context.Bb.GetOrDefault(RetryCountKey, 0);
            if (retryCount < definition.Option.MaximumRetries)
            {
                context.Bb.Set(RetryCountKey, retryCount + 1);
                yield return Ai.Goto(MoveTowardState, "bounded retry");
                yield break;
            }

            yield return Ai.Goto(BlockedState, "retry exhausted");
            yield break;
        }

        yield return Ai.Goto(FailedState, result.FailureReason ?? result.State.ToString());
    }

    private IEnumerator<AiStep> Completed(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.Completed);
        while (true)
        {
            yield return Ai.Steady("completion observed");
        }
    }

    private IEnumerator<AiStep> Blocked(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.Blocked);
        while (true)
        {
            yield return Ai.Steady("movement blocked");
        }
    }

    private IEnumerator<AiStep> Unsupported(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.Unsupported);
        while (true)
        {
            yield return Ai.Steady("movement unsupported");
        }
    }

    private IEnumerator<AiStep> Failed(AiCtx context)
    {
        context.Bb.Set(TransitionKey, SkyrimApproachTransition.Failed);
        while (true)
        {
            yield return Ai.Steady("target or action failed");
        }
    }

    private bool TargetIsValid()
    {
        return definition.Binding.ActorId == observation.ActorId
            && definition.Goal.TargetPosition.IsFinite
            && observation.Loaded
            && observation.LifeState == HostActorLifeState.Alive;
    }

    private bool IsAlreadyReached()
    {
        return TargetIsValid()
            && observation.Position.DistanceTo(definition.Goal.TargetPosition)
                <= definition.Goal.StoppingDistance;
    }

    private bool CanMove()
    {
        return TargetIsValid()
            && !IsAlreadyReached()
            && observation.Capabilities.CanMoveToward;
    }

    private sealed class MoveTowardHandler : IActuationHandler<MoveTowardActuationCommand>
    {
        private readonly IHostPresenterBackend backend;

        public MoveTowardHandler(IHostPresenterBackend backend)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx context,
            ActuationId id,
            MoveTowardActuationCommand command)
        {
            HostActionResult result = HostActionRunner.ExecuteAsync(
                backend,
                command.Request,
                CancellationToken.None).AsTask().GetAwaiter().GetResult();
            return ActuatorHost.HandlerResult.CompletedWithPayload(
                result,
                ok: result.State == HostActionState.Completed,
                error: result.FailureReason);
        }
    }

    private sealed class DecisionTrace : IAiTraceSink
    {
        public DecisionReport? FirstDecision { get; private set; }

        public DecisionReport? LastDecision { get; private set; }

        public void OnEnter(StateId state, float time, string reason) { }

        public void OnExit(StateId state, float time, string reason) { }

        public void OnTransition(StateId from, StateId to, float time, string reason) { }

        public void OnYield(StateId state, float time, object yielded)
        {
            if (yielded is DecisionReport report)
            {
                FirstDecision ??= report;
                LastDecision = report;
            }
        }
    }
}
