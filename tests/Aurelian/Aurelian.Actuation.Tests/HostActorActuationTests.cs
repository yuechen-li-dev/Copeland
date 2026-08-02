using System.Runtime.CompilerServices;
using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class HostActorActuationTests
{
    [Fact]
    public void ActorObservation_IsImmutableValueEqual()
    {
        HostActorObservation left = CreateObservation();
        HostActorObservation right = CreateObservation();

        Assert.Equal(left, right);
        Assert.NotSame(left, right);
    }

    [Fact]
    public void MoveToward_CommandRequiresMatchingGenerationAndBoundedValues()
    {
        HostCommandRequest valid = CreateRequest(CreateObservation());
        HostCommandRequest wrongGeneration = valid with { ExpectedHostGeneration = 8 };
        HostCommandRequest unbounded = valid with
        {
            Arguments = ((MoveTowardArguments)valid.Arguments) with { MaximumDistance = 65.0f },
        };

        Assert.True(valid.Validate().IsValid);
        Assert.Equal("actor_generation_mismatch", wrongGeneration.Validate().FailureReason);
        Assert.Equal("move_toward_distance_out_of_range", unbounded.Validate().FailureReason);
    }

    [Fact]
    public async Task FakeBackend_SameCommandProducesDeterministicResultAndLifecycle()
    {
        HostActorObservation initial = CreateObservation();
        HostCommandRequest request = CreateRequest(initial);
        var first = new DeterministicHostPresenterBackend(initial);
        var second = new DeterministicHostPresenterBackend(initial);

        HostCommandReceipt firstReceipt = await first.SubmitAsync(request, CancellationToken.None);
        HostCommandReceipt secondReceipt = await second.SubmitAsync(request, CancellationToken.None);
        HostRuntimeObservation[] firstObservations = await ReadAllAsync(first);
        HostRuntimeObservation[] secondObservations = await ReadAllAsync(second);

        Assert.True(firstReceipt.Accepted);
        Assert.Equal(firstReceipt, secondReceipt);
        Assert.Equal(firstObservations, secondObservations);
        Assert.Equal(
            [HostActionState.Accepted, HostActionState.Running, HostActionState.Completed],
            firstObservations.Select(item => item.Action!.State));
        Assert.Equal(new HostPosition3(64.0f, 0.0f, 0.0f), first.CurrentActor.Position);
    }

    [Fact]
    public async Task FakeBackend_BlockedResultIsExplicit()
    {
        HostActorObservation initial = CreateObservation();
        var backend = new DeterministicHostPresenterBackend(initial)
        {
            InjectedTerminalState = HostActionState.Blocked,
        };

        HostActionResult result = await HostActionRunner.ExecuteAsync(
            backend,
            CreateRequest(initial),
            CancellationToken.None);

        Assert.Equal(HostActionState.Blocked, result.State);
        Assert.Equal("movement_blocked", result.FailureReason);
        Assert.Equal(initial.Position, backend.CurrentActor.Position);
    }

    [Fact]
    public async Task FakeBackend_UnsupportedCapabilityPreventsMovement()
    {
        HostActorObservation initial = CreateObservation() with
        {
            Capabilities = CreateCapabilities() with
            {
                GoalDirectedMovement = HostCapabilitySupport.Unsupported,
            },
        };
        var backend = new DeterministicHostPresenterBackend(initial);

        HostActionResult result = await HostActionRunner.ExecuteAsync(
            backend,
            CreateRequest(initial),
            CancellationToken.None);

        Assert.Equal(HostActionState.Unsupported, result.State);
        Assert.Equal(initial.Position, backend.CurrentActor.Position);
    }

    [Fact]
    public async Task FakeBackend_RejectsStaleObservationSequence()
    {
        HostActorObservation initial = CreateObservation();
        HostCommandRequest request = CreateRequest(initial) with
        {
            Arguments = ((MoveTowardArguments)CreateRequest(initial).Arguments) with
            {
                ExpectedObservationSequence = initial.Sequence - 1,
            },
        };

        HostActionResult result = await HostActionRunner.ExecuteAsync(
            new DeterministicHostPresenterBackend(initial),
            request,
            CancellationToken.None);

        Assert.Equal(HostActionState.Rejected, result.State);
        Assert.Equal("stale_observation_sequence", result.FailureReason);
    }

    [Fact]
    public async Task FakeBackend_RejectsWrongActorGeneration()
    {
        HostActorObservation initial = CreateObservation();
        HostCommandRequest request = CreateRequest(initial) with
        {
            ExpectedHostGeneration = 8,
        };

        HostActionResult result = await HostActionRunner.ExecuteAsync(
            new DeterministicHostPresenterBackend(initial),
            request,
            CancellationToken.None);

        Assert.Equal(HostActionState.Rejected, result.State);
        Assert.Equal("actor_generation_mismatch", result.FailureReason);
    }

    [Fact]
    public async Task ReplayBackend_ReplaysCapturedLifecycleDeterministically()
    {
        HostActorObservation initial = CreateObservation();
        HostCommandRequest request = CreateRequest(initial);
        var capture = new DeterministicHostPresenterBackend(initial);
        await capture.SubmitAsync(request, CancellationToken.None);
        HostRuntimeObservation[] recorded = await ReadAllAsync(capture);

        var firstReplay = new ReplayHostPresenterBackend(recorded);
        var secondReplay = new ReplayHostPresenterBackend(recorded);
        HostActionResult first = await HostActionRunner.ExecuteAsync(firstReplay, request, CancellationToken.None);
        HostActionResult second = await HostActionRunner.ExecuteAsync(secondReplay, request, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(HostActionState.Completed, first.State);
        Assert.Equal(new HostPosition3(64.0f, 0.0f, 0.0f), first.Observation!.Position);
    }

    [Fact]
    public async Task ActionRunner_MapsTimeoutAndCancellation()
    {
        HostActorObservation initial = CreateObservation();
        HostCommandRequest shortRequest = CreateRequest(initial) with
        {
            Timeout = TimeSpan.FromMilliseconds(10),
        };
        var timeoutBackend = new NeverCompletingBackend();

        HostActionResult timedOut = await HostActionRunner.ExecuteAsync(
            timeoutBackend,
            shortRequest,
            CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        HostActionResult interrupted = await HostActionRunner.ExecuteAsync(
            timeoutBackend,
            CreateRequest(initial),
            cancellation.Token);

        Assert.Equal(HostActionState.TimedOut, timedOut.State);
        Assert.Equal(HostActionState.Interrupted, interrupted.State);
    }

    private static HostActorObservation CreateObservation() => new(
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
        CreateCapabilities(),
        Sequence: 11);

    private static HostCapabilitySnapshot CreateCapabilities() => new(
        HostCapabilitySupport.Supported,
        HostCapabilitySupport.Unsupported,
        HostCapabilitySupport.Experimental,
        HostCapabilitySupport.Supported,
        HostCapabilitySupport.Unsupported,
        HostCapabilitySupport.Unsupported,
        HostCapabilitySupport.Unsupported,
        HostCapabilitySupport.Unsupported);

    private static HostCommandRequest CreateRequest(HostActorObservation observation) => new(
        Guid.Parse("ba4b101f-b1a8-4ccc-b842-3440dd275ee1"),
        observation.ActorId.Generation,
        HostCommandKind.MoveToward,
        TimeSpan.FromSeconds(1),
        new MoveTowardArguments(
            observation.ActorId,
            new HostPosition3(128.0f, 0.0f, 0.0f),
            StoppingDistance: 16.0f,
            MaximumDistance: 64.0f,
            HostMovementSpeedPolicy.Walk,
            observation.Sequence));

    private static async Task<HostRuntimeObservation[]> ReadAllAsync(IHostPresenterBackend backend)
    {
        var observations = new List<HostRuntimeObservation>();
        await foreach (HostRuntimeObservation observation in backend.ObserveAsync(CancellationToken.None))
        {
            observations.Add(observation);
        }

        return observations.ToArray();
    }

    private sealed class NeverCompletingBackend : IHostPresenterBackend
    {
        public ValueTask<HostCommandReceipt> SubmitAsync(
            HostCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new HostCommandReceipt(
                request.RequestId,
                Accepted: true,
                RuntimeSequence: 1,
                FailureReason: null));
        }

        public async IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }
}
