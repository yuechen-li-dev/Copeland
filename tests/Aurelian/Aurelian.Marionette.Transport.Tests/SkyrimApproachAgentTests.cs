using System.Runtime.CompilerServices;
using Aurelian.Actuation.Host;
using Xunit;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class SkyrimApproachAgentTests
{
    [Fact]
    public void UnboundAgent_RequestsBindingThenCompletesOwnedLifecycle()
    {
        (SkyrimBodyAgentRuntime runtime, LifecycleBackend backend) = CreateRuntime();

        Assert.Equal(SkyrimBodyAgentState.Unbound, runtime.State);
        runtime.Tick();
        runtime.Tick();
        Assert.Equal(SkyrimBodyAgentState.RequestBinding, runtime.State);

        SkyrimBodyAgentState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimBodyAgentState.Completed, terminal);
        Assert.Equal(BodyBindingState.Released, runtime.Binding!.State);
        Assert.Equal("MoveToward", runtime.SelectedOption);
        Assert.Equal(
            [HostCommandKind.BeginHostSession, HostCommandKind.MoveToward, HostCommandKind.EndHostSession],
            backend.Commands);
    }

    [Fact]
    public void FailedBinding_ReturnsAuthoredFailureWithoutMovement()
    {
        (SkyrimBodyAgentRuntime runtime, LifecycleBackend backend) = CreateRuntime();
        backend.BindingState = HostActionState.Failed;

        SkyrimBodyAgentState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimBodyAgentState.Failed, terminal);
        Assert.Equal(HostActionState.Failed, runtime.BindingResult!.State);
        Assert.DoesNotContain(HostCommandKind.MoveToward, backend.Commands);
    }

    [Fact]
    public void MovementFailure_TriggersReleaseThenAuthoredFailure()
    {
        (SkyrimBodyAgentRuntime runtime, LifecycleBackend backend) = CreateRuntime();
        backend.MovementState = HostActionState.Blocked;

        SkyrimBodyAgentState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimBodyAgentState.Failed, terminal);
        Assert.Equal(HostActionState.Blocked, runtime.MovementResult!.State);
        Assert.Equal(HostActionState.Completed, runtime.ReleaseResult!.State);
        Assert.Contains(HostCommandKind.EndHostSession, backend.Commands);
    }

    [Fact]
    public void LostBody_StillRestoresSessionBeforeFailure()
    {
        (SkyrimBodyAgentRuntime runtime, LifecycleBackend backend) = CreateRuntime();
        backend.MovementState = HostActionState.ActorUnloaded;

        SkyrimBodyAgentState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimBodyAgentState.Failed, terminal);
        Assert.Equal(BodyBindingState.Released, runtime.Binding!.State);
        Assert.Equal(HostCommandKind.EndHostSession, backend.Commands[^1]);
    }

    [Fact]
    public void ReleaseFailure_ReportsRestoreRequired()
    {
        (SkyrimBodyAgentRuntime runtime, LifecycleBackend backend) = CreateRuntime();
        backend.ReleaseState = HostActionState.EngineRefused;

        SkyrimBodyAgentState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimBodyAgentState.RestoreRequired, terminal);
        Assert.Equal(BodyBindingState.RestoreRequired, runtime.Binding!.State);
    }

    [Fact]
    public void GeneratedFlow_HasStableAuthoredIdsAndNoHiddenStates()
    {
        (SkyrimBodyAgentRuntime runtime, _) = CreateRuntime();

        var inspection = runtime.FlowInspection;
        string[] ids = inspection.States.Select(state => state.Id.Value).ToArray();

        Assert.Equal(10, ids.Length);
        Assert.Equal(10, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith("aurelian.skyrim.body-agent.", id));
        Assert.Empty(inspection.GeneratedArtifacts);
    }

    [Fact]
    public void OperationSites_AreExplicitAndPatchStable()
    {
        Assert.Equal(3, SkyrimBodyAgentRuntime.OperationSites.Count);
        Assert.All(SkyrimBodyAgentRuntime.OperationSites, site => Assert.True(site.IsPatchStable));
        Assert.All(SkyrimBodyAgentRuntime.OperationSites, site => Assert.Equal(0, site.GeneratedStateCount));
    }

    private static (SkyrimBodyAgentRuntime Runtime, LifecycleBackend Backend) CreateRuntime()
    {
        HostActorObservation actor = CreateActor();
        var lowLevel = new LifecycleBackend(actor);
        var backend = new BodyBindingHostBackend(lowLevel);
        var bodyId = new BodyId("fixture-body");
        backend.RegisterCandidate(bodyId, actor.ActorId);
        SkyrimBodyAgentDefinition definition = SkyrimAgent.Define(
            new AurelianAgentId(Guid.Parse("1076e9bb-ab10-4562-a357-0a51d9984631")),
            bodyId,
            actor.ActorId.Generation,
            new ReachTargetGoal(new HostPosition3(128.0f, 0.0f, 0.0f), 16.0f),
            new ApproachTargetOption(64.0f, HostMovementSpeedPolicy.Walk));
        return (definition.CreateRuntime(backend), lowLevel);
    }

    private static HostActorObservation CreateActor() => new(
        new HostActorId(0xE5F74, 7),
        new HostPosition3(0.0f, 0.0f, 0.0f),
        HeadingRadians: 0.0f,
        Velocity: null,
        HostActorLifeState.Alive,
        HostActorMovementState.Idle,
        Loaded: true,
        CurrentCellFormId: 0x18AA2,
        CurrentTarget: null,
        DistanceToGoal: 128.0f,
        HostActionState.None,
        new HostCapabilitySnapshot(
            HostCapabilitySupport.Supported,
            HostCapabilitySupport.Unsupported,
            HostCapabilitySupport.Experimental,
            HostCapabilitySupport.Supported,
            HostCapabilitySupport.Unsupported,
            HostCapabilitySupport.Unsupported,
            HostCapabilitySupport.Unsupported,
            HostCapabilitySupport.Unsupported),
        Sequence: 11);

    private sealed class LifecycleBackend : IHostPresenterBackend
    {
        private readonly Queue<HostRuntimeObservation> observations = new();
        private HostActorObservation actor;
        private ulong sequence;

        public LifecycleBackend(HostActorObservation actor)
        {
            this.actor = actor;
            sequence = actor.Sequence;
        }

        public HostActionState BindingState { get; set; } = HostActionState.Completed;

        public HostActionState MovementState { get; set; } = HostActionState.Completed;

        public HostActionState ReleaseState { get; set; } = HostActionState.Completed;

        public List<HostCommandKind> Commands { get; } = [];

        public ValueTask<HostCommandReceipt> SubmitAsync(
            HostCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(request.Kind);
            HostActionState state = request.Kind switch
            {
                HostCommandKind.BeginHostSession => BindingState,
                HostCommandKind.MoveToward => MovementState,
                HostCommandKind.EndHostSession => ReleaseState,
                _ => HostActionState.Unsupported,
            };
            if (request.Kind == HostCommandKind.MoveToward
                && state == HostActionState.Completed
                && request.Arguments is MoveTowardArguments move)
            {
                actor = actor with
                {
                    Position = new HostPosition3(
                        move.TargetPosition.X - move.StoppingDistance,
                        move.TargetPosition.Y,
                        move.TargetPosition.Z),
                    Sequence = ++sequence,
                };
            }

            string? reason = state == HostActionState.Completed ? null : "injected_failure";
            var action = new HostActionResult(request.RequestId, state, reason, actor);
            observations.Enqueue(new HostRuntimeObservation(
                ++sequence,
                ActiveHost: null,
                new PlayerAnchorObservation(0x14, 0, 0, 0),
                new CameraObservation(actor.ActorId.FormId, HostCameraMode.ThirdPerson),
                new CrosshairObservation(0),
                new MovementObservation(false, false),
                action,
                actor));
            return ValueTask.FromResult(new HostCommandReceipt(
                request.RequestId,
                Accepted: true,
                sequence,
                FailureReason: null));
        }

        public async IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (observations.TryDequeue(out HostRuntimeObservation? observation))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return observation;
                await Task.Yield();
            }
        }
    }
}
