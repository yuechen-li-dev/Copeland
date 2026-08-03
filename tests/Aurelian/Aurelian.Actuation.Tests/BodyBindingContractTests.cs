using System.Runtime.CompilerServices;
using System.Text.Json;
using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class BodyBindingContractTests
{
    private static readonly AgentId FirstAgent = new(
        Guid.Parse("52f83c72-af51-4a40-8050-0f19fc37ed85"));
    private static readonly AgentId SecondAgent = new(
        Guid.Parse("0dba08d1-e5a2-4c54-98f1-63843866ab84"));
    private static readonly BodyId FirstBody = new("fixture-body-one");
    private static readonly BodyId SecondBody = new("fixture-body-two");

    [Fact]
    public void AgentId_IsDeterministicAndIndependentFromBackendIdentity()
    {
        var same = new AgentId(FirstAgent.Value);

        Assert.Equal(FirstAgent, same);
        Assert.Equal(typeof(Guid), typeof(AgentId).GetProperty(nameof(AgentId.Value))!.PropertyType);
        Assert.DoesNotContain(typeof(AgentId).GetProperties(), property => property.Name.Contains("Form", StringComparison.Ordinal));
    }

    [Fact]
    public void PortableIdentities_RoundTripWithSystemTextJson()
    {
        string agentJson = JsonSerializer.Serialize(FirstAgent);
        string bodyJson = JsonSerializer.Serialize(FirstBody);

        Assert.Equal(FirstAgent, JsonSerializer.Deserialize<AgentId>(agentJson));
        Assert.Equal(FirstBody, JsonSerializer.Deserialize<BodyId>(bodyJson));
    }

    [Fact]
    public void BodyId_IsOpaquePortableString()
    {
        Assert.Equal(typeof(string), typeof(BodyId).GetProperty(nameof(BodyId.Value))!.PropertyType);
        Assert.DoesNotContain(typeof(BodyId).GetProperties(), property => property.Name.Contains("Form", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_EnforcesOneExclusiveBodyPerAgentAndOneAgentPerBody()
    {
        var registry = new BodyBindingRegistry();

        Assert.True(registry.BeginBinding(FirstAgent, FirstBody, BodyBindingKind.ExclusiveControl, 7).Accepted);
        Assert.Equal(
            "agent_already_exclusively_bound",
            registry.BeginBinding(FirstAgent, SecondBody, BodyBindingKind.ExclusiveControl, 8).FailureReason);
        Assert.Equal(
            "body_already_exclusively_bound",
            registry.BeginBinding(SecondAgent, FirstBody, BodyBindingKind.ExclusiveControl, 7).FailureReason);
    }

    [Fact]
    public void Registry_RejectsDuplicateAndStaleCommands()
    {
        BodyBindingRegistry registry = CreateBoundRegistry();

        Assert.Equal(
            "agent_already_exclusively_bound",
            registry.BeginBinding(FirstAgent, FirstBody, BodyBindingKind.ExclusiveControl, 7).FailureReason);
        Assert.Equal(
            "stale_body_generation",
            registry.AuthorizeExclusiveCommand(FirstAgent, FirstBody, 6).FailureReason);
    }

    [Fact]
    public void Registry_FailedBindingLeavesNoPartialOwnership()
    {
        var registry = new BodyBindingRegistry();
        registry.BeginBinding(FirstAgent, FirstBody, BodyBindingKind.ExclusiveControl, 7);

        BodyBindingRegistryResult failed = registry.FailBinding(
            FirstAgent,
            FirstBody,
            restoreRequired: false);
        BodyBindingRegistryResult replacement = registry.BeginBinding(
            SecondAgent,
            FirstBody,
            BodyBindingKind.ExclusiveControl,
            7);

        Assert.Equal(BodyBindingState.Failed, failed.Binding!.State);
        Assert.True(replacement.Accepted);
    }

    [Fact]
    public void Registry_ReleaseIsIdempotentAndReleasedBodyCannotBeCommanded()
    {
        BodyBindingRegistry registry = CreateBoundRegistry();

        Assert.True(registry.BeginRelease(FirstAgent, FirstBody, 7).Accepted);
        Assert.True(registry.CompleteRelease(FirstAgent, FirstBody, 7).Accepted);
        Assert.True(registry.BeginRelease(FirstAgent, FirstBody, 7).Accepted);
        Assert.Equal(
            "agent_not_bound",
            registry.AuthorizeExclusiveCommand(FirstAgent, FirstBody, 7).FailureReason);
        Assert.Equal(BodyBindingState.Released, registry.Query(FirstAgent)!.State);
    }

    [Fact]
    public void Registry_LostBodyIsExplicitAndAgentIdentitySurvivesRelease()
    {
        BodyBindingRegistry registry = CreateBoundRegistry();

        BodyBindingRegistryResult result = registry.MarkLost(FirstAgent, FirstBody, 7);

        Assert.True(result.Accepted);
        Assert.Equal(BodyBindingState.Lost, registry.Query(FirstAgent)!.State);
        Assert.Equal(FirstAgent, registry.Query(FirstAgent)!.Agent);
    }

    [Fact]
    public async Task BindingBackend_LowersBindMoveReleaseAndEnforcesOwner()
    {
        HostActorObservation actor = CreateActor();
        var lowLevel = new RecordingBackend(actor);
        var backend = new BodyBindingHostBackend(lowLevel);
        backend.RegisterCandidate(FirstBody, actor.ActorId);

        HostActionResult bound = await Execute(backend, BindRequest(FirstAgent, actor.ActorId.Generation));
        HostActionResult wrongOwner = await Execute(backend, MoveRequest(SecondAgent, actor));
        HostActionResult moved = await Execute(backend, MoveRequest(FirstAgent, actor));
        HostActionResult released = await Execute(backend, ReleaseRequest(FirstAgent, actor.ActorId.Generation));
        HostActionResult afterRelease = await Execute(backend, MoveRequest(FirstAgent, actor));

        Assert.Equal(BodyBindingState.Bound, bound.BodyResult!.Binding!.Binding.State);
        Assert.Equal("agent_not_bound", wrongOwner.FailureReason);
        Assert.Equal(HostActionState.Completed, moved.State);
        Assert.Equal(BodyBindingState.Released, released.BodyResult!.Binding!.Binding.State);
        Assert.Equal("agent_not_bound", afterRelease.FailureReason);
        Assert.Equal(
            [HostCommandKind.BeginHostSession, HostCommandKind.MoveToward, HostCommandKind.EndHostSession],
            lowLevel.Commands);
    }

    [Fact]
    public async Task BindingBackend_RejectsStaleCandidateBeforeLowering()
    {
        HostActorObservation actor = CreateActor();
        var lowLevel = new RecordingBackend(actor);
        var backend = new BodyBindingHostBackend(lowLevel);
        backend.RegisterCandidate(FirstBody, actor.ActorId);

        HostActionResult result = await Execute(backend, BindRequest(FirstAgent, actor.ActorId.Generation - 1));

        Assert.Equal(HostActionState.Rejected, result.State);
        Assert.Equal("stale_body_generation", result.FailureReason);
        Assert.Empty(lowLevel.Commands);
    }

    [Fact]
    public async Task BindingBackend_UnloadedBodyBecomesLost()
    {
        HostActorObservation actor = CreateActor();
        var lowLevel = new RecordingBackend(actor);
        var backend = new BodyBindingHostBackend(lowLevel);
        backend.RegisterCandidate(FirstBody, actor.ActorId);
        await Execute(backend, BindRequest(FirstAgent, actor.ActorId.Generation));
        backend.ObserveMaterialization(FirstBody, actor with { Loaded = false });

        HostActionResult result = await Execute(backend, MoveRequest(FirstAgent, actor));

        Assert.Equal(HostActionState.ActorUnloaded, result.State);
        Assert.Equal(BodyBindingState.Lost, backend.Registry.Query(FirstAgent)!.State);
        Assert.DoesNotContain(lowLevel.Commands.Skip(1), command => command == HostCommandKind.MoveToward);
    }

    private static BodyBindingRegistry CreateBoundRegistry()
    {
        var registry = new BodyBindingRegistry();
        registry.BeginBinding(FirstAgent, FirstBody, BodyBindingKind.ExclusiveControl, 7);
        registry.CompleteBinding(FirstAgent, FirstBody, 7);
        return registry;
    }

    private static HostCommandRequest BindRequest(AgentId agent, ulong generation) => new(
        Guid.NewGuid(),
        generation,
        HostCommandKind.BindBody,
        TimeSpan.FromSeconds(1),
        new BindBodyArguments(agent, FirstBody, BodyBindingKind.ExclusiveControl, generation));

    private static HostCommandRequest MoveRequest(AgentId agent, HostActorObservation actor) => new(
        Guid.NewGuid(),
        actor.ActorId.Generation,
        HostCommandKind.MoveBodyToward,
        TimeSpan.FromSeconds(1),
        new MoveBodyTowardArguments(
            agent,
            FirstBody,
            new HostPosition3(64, 0, 0),
            1,
            64,
            HostMovementSpeedPolicy.Walk,
            actor.ActorId.Generation,
            actor.Sequence));

    private static HostCommandRequest ReleaseRequest(AgentId agent, ulong generation) => new(
        Guid.NewGuid(),
        generation,
        HostCommandKind.ReleaseBody,
        TimeSpan.FromSeconds(1),
        new ReleaseBodyArguments(agent, FirstBody, generation));

    private static async Task<HostActionResult> Execute(
        IHostPresenterBackend backend,
        HostCommandRequest request)
    {
        return await HostActionRunner.ExecuteAsync(backend, request, CancellationToken.None);
    }

    private static HostActorObservation CreateActor() => new(
        new HostActorId(0x1234, 7),
        new HostPosition3(0, 0, 0),
        0,
        null,
        HostActorLifeState.Alive,
        HostActorMovementState.Idle,
        true,
        0x4567,
        null,
        64,
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
        11);

    private sealed class RecordingBackend : IHostPresenterBackend
    {
        private readonly Queue<HostRuntimeObservation> observations = new();
        private HostActorObservation actor;
        private ulong sequence;

        public RecordingBackend(HostActorObservation actor)
        {
            this.actor = actor;
            sequence = actor.Sequence;
        }

        public List<HostCommandKind> Commands { get; } = [];

        public ValueTask<HostCommandReceipt> SubmitAsync(
            HostCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(request.Kind);
            if (request.Kind == HostCommandKind.MoveToward
                && request.Arguments is MoveTowardArguments move)
            {
                actor = actor with
                {
                    Position = move.TargetPosition,
                    Sequence = ++sequence,
                };
            }

            var action = new HostActionResult(
                request.RequestId,
                HostActionState.Completed,
                null,
                actor);
            observations.Enqueue(new HostRuntimeObservation(
                ++sequence,
                null,
                new PlayerAnchorObservation(0x14, 0, 0, 0),
                new CameraObservation(actor.ActorId.FormId, HostCameraMode.ThirdPerson),
                new CrosshairObservation(0),
                new MovementObservation(false, false),
                action,
                actor));
            return ValueTask.FromResult(new HostCommandReceipt(request.RequestId, true, sequence, null));
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
