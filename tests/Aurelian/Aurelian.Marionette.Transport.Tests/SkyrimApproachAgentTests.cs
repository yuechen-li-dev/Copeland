using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class SkyrimApproachAgentTests
{
    [Fact]
    public void SameObservation_SelectsSameUtilityOptionAndCommand()
    {
        HostActorObservation observation = CreateObservation();
        SkyrimApproachAgentDefinition definition = CreateDefinition(observation);
        Guid requestId = Guid.Parse("61ca6a06-f728-454d-8e5d-889838ce8d70");
        SkyrimApproachAgentRuntime first = definition.CreateRuntime(
            new DeterministicHostPresenterBackend(observation),
            observation,
            requestId);
        SkyrimApproachAgentRuntime second = definition.CreateRuntime(
            new DeterministicHostPresenterBackend(observation),
            observation,
            requestId);

        SkyrimApproachTransition firstTransition = first.RunUntilTerminal();
        SkyrimApproachTransition secondTransition = second.RunUntilTerminal();

        Assert.Equal("MoveToward", first.SelectedOption);
        Assert.Equal(first.SelectedOption, second.SelectedOption);
        Assert.Equal(first.CreateCommand(), second.CreateCommand());
        Assert.Equal(SkyrimApproachTransition.Completed, firstTransition);
        Assert.Equal(firstTransition, secondTransition);
    }

    [Fact]
    public void CompletionResult_DrivesCompletedTransition()
    {
        HostActorObservation observation = CreateObservation();
        SkyrimApproachAgentRuntime runtime = CreateDefinition(observation).CreateRuntime(
            new DeterministicHostPresenterBackend(observation),
            observation,
            Guid.NewGuid());

        SkyrimApproachTransition transition = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimApproachTransition.Completed, transition);
        Assert.Equal(HostActionState.Completed, runtime.LastResult!.State);
        Assert.Equal("MoveToward", runtime.Decision!.BestId);
    }

    [Fact]
    public void BlockedResult_RetriesOnceThenTransitionsBlocked()
    {
        HostActorObservation observation = CreateObservation();
        var backend = new DeterministicHostPresenterBackend(observation)
        {
            InjectedTerminalState = HostActionState.Blocked,
        };
        SkyrimApproachAgentRuntime runtime = CreateDefinition(observation).CreateRuntime(
            backend,
            observation,
            Guid.NewGuid());

        SkyrimApproachTransition transition = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimApproachTransition.Blocked, transition);
        Assert.Equal(1, runtime.RetryCount);
        Assert.Equal(2, backend.SubmittedCommandCount);
    }

    [Fact]
    public void UnsupportedCapability_PreventsCommandSelection()
    {
        HostActorObservation observation = CreateObservation() with
        {
            Capabilities = CreateCapabilities() with
            {
                GoalDirectedMovement = HostCapabilitySupport.Unsupported,
            },
        };
        var backend = new DeterministicHostPresenterBackend(observation);
        SkyrimApproachAgentRuntime runtime = CreateDefinition(observation).CreateRuntime(
            backend,
            observation,
            Guid.NewGuid());

        SkyrimApproachTransition transition = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimApproachTransition.Unsupported, transition);
        Assert.Equal("Unsupported", runtime.SelectedOption);
        Assert.Equal(0, backend.SubmittedCommandCount);
    }

    [Fact]
    public void TargetAlreadyReached_RemainsIdleWithoutCommand()
    {
        HostActorObservation observation = CreateObservation() with
        {
            Position = new HostPosition3(120.0f, 0.0f, 0.0f),
        };
        var backend = new DeterministicHostPresenterBackend(observation);
        SkyrimApproachAgentRuntime runtime = CreateDefinition(observation).CreateRuntime(
            backend,
            observation,
            Guid.NewGuid());

        SkyrimApproachTransition transition = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimApproachTransition.AlreadyReached, transition);
        Assert.Equal("AlreadyReached", runtime.SelectedOption);
        Assert.Equal(0, backend.SubmittedCommandCount);
    }

    [Theory]
    [InlineData(false, HostActorLifeState.Alive)]
    [InlineData(true, HostActorLifeState.Dead)]
    public void UnavailableActor_TransitionsFailed(bool loaded, HostActorLifeState lifeState)
    {
        HostActorObservation observation = CreateObservation() with
        {
            Loaded = loaded,
            LifeState = lifeState,
        };
        var backend = new DeterministicHostPresenterBackend(observation);
        SkyrimApproachAgentRuntime runtime = CreateDefinition(observation).CreateRuntime(
            backend,
            observation,
            Guid.NewGuid());

        SkyrimApproachTransition transition = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimApproachTransition.Failed, transition);
        Assert.Equal("TargetInvalid", runtime.SelectedOption);
        Assert.Equal(0, backend.SubmittedCommandCount);
    }

    private static SkyrimApproachAgentDefinition CreateDefinition(HostActorObservation observation)
    {
        return SkyrimAgent.Define(
            id: "skyrim-approach-spike",
            binding: new SkyrimActorBinding(
                observation.ActorId.FormId,
                observation.ActorId.Generation),
            goal: new ReachTargetGoal(
                new HostPosition3(128.0f, 0.0f, 0.0f),
                StoppingDistance: 16.0f),
            option: new ApproachTargetOption(
                MaximumDistance: 64.0f,
                HostMovementSpeedPolicy.Walk,
                MaximumRetries: 1));
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
}
