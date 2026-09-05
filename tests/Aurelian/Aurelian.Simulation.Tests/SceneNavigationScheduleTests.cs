using Aurelian.Simulation;
using Xunit;

namespace Aurelian.Simulation.Tests;

public sealed class SceneNavigationScheduleTests
{
    [Fact]
    public void CatalogValidatesRoutesAndLooksUpAnchors()
    {
        SceneCatalog catalog = CreateCatalog();
        SimulationAnchor anchor = catalog.GetAnchor(new SimulationAnchorId("b.entry"));
        SceneTransition transition = new SceneTransitionBridge(catalog)
            .Propose(new SimulationSceneId("a"), new SimulationRouteId("a-b"));
        Assert.Equal(new SimulationSceneId("b"), anchor.Scene);
        Assert.Equal(new SimulationAnchorId("b.entry"), transition.DestinationAnchor);
    }

    [Fact]
    public void TransitionProposalDoesNotChangeActiveScene()
    {
        SceneCatalog catalog = CreateCatalog();
        SimulationSceneId authoritative = new("a");
        SceneTransition proposal = new SceneTransitionBridge(catalog)
            .Propose(authoritative, new SimulationRouteId("a-b"));
        Assert.Equal(new SimulationSceneId("a"), authoritative);
        Assert.Equal(new SimulationSceneId("b"), proposal.Destination);
    }

    [Fact]
    public void AcceptedTransitionOrdersResourceHandoffBeforeCameraCue()
    {
        var events = new List<string>();
        SceneCatalog catalog = CreateCatalog();
        var bridge = new SceneTransitionBridge(catalog);
        SceneTransition proposal = bridge.Propose(new SimulationSceneId("a"), new SimulationRouteId("a-b"));
        SceneActivationFact activation = bridge.CompleteAccepted(
            proposal,
            SceneSimulationDetail.Coarse,
            new RecordingResources(events),
            new RecordingPresentation(events));
        Assert.Equal(["leave:a", "enter:b", "camera:b:b.entry"], events);
        Assert.Equal(SceneSimulationDetail.Coarse, activation.Detail);
    }

    [Fact]
    public void ActivationAndNavigationFactsCarryMechanismWithoutPolicy()
    {
        var activation = new SceneActivationFact(null, new SimulationSceneId("a"), SceneSimulationDetail.Detailed);
        var goal = new NavigationGoal(new NavigationRequestId(NavigationRequestKind.Goal, "npc-7:goal-3"), activation.Current, new SimulationAnchorId("a.goal"));
        var blocked = new NavigationFact(goal.Request, NavigationOutcome.ReplanRequested, "resolver rejected three proposals");
        Assert.Equal(SceneSimulationDetail.Detailed, activation.Detail);
        Assert.Equal(NavigationOutcome.ReplanRequested, blocked.Outcome);
    }

    [Fact]
    public void NavigationCoordinatorReportsArrivalBlockedReplanAndInterruption()
    {
        var request = new NavigationRequestId(NavigationRequestKind.Goal, "npc-7:goal-3");
        var scene = new SimulationSceneId("a");
        var goal = new NavigationGoal(request, scene, new SimulationAnchorId("a.goal"));
        var destination = new SimulationAnchor(goal.Anchor, scene, new SimulationPoint(50, 50), 2);
        Assert.Equal(
            NavigationOutcome.Arrived,
            NavigationCoordinator.ObservePosition(goal, new SimulationPoint(51, 50), destination).Outcome);
        Assert.Equal(NavigationOutcome.Blocked, NavigationCoordinator.MovementRejected(goal, 1, 3).Outcome);
        Assert.Equal(NavigationOutcome.ReplanRequested, NavigationCoordinator.MovementRejected(goal, 3, 3).Outcome);
        var changed = destination with { Id = new SimulationAnchorId("a.other") };
        Assert.Equal(
            NavigationOutcome.Interrupted,
            NavigationCoordinator.ObservePosition(goal, new SimulationPoint(50, 50), changed).Outcome);
    }

    [Fact]
    public void ScheduleMatchingIsDeterministicAndRejectsPriorityTies()
    {
        ScheduleWindow<string>[] windows =
        [
            new("ambient", 0, 100, 1, "patrol"),
            new("alarm", 20, 40, 2, "shelter")
        ];
        ScheduleMatch<string>? first = DeterministicSchedule.Match(windows, 25);
        ScheduleMatch<string>? second = DeterministicSchedule.Match(windows.Reverse(), 25);
        Assert.Equal(first, second);
        Assert.Equal("shelter", first?.Goal);

        ScheduleWindow<string>[] tied =
        [
            new("left", 0, 10, 1, "x"),
            new("right", 0, 10, 1, "y")
        ];
        Assert.Throws<InvalidOperationException>(() => DeterministicSchedule.Match(tied, 5));
    }

    private static SceneCatalog CreateCatalog()
    {
        var a = new SimulationSceneId("a");
        var b = new SimulationSceneId("b");
        return new SceneCatalog(
        [
            new SimulationScene(
                a,
                new SimulationBounds(100, 100),
                [new SimulationAnchor(new SimulationAnchorId("a.goal"), a, new SimulationPoint(50, 50), 2)],
                [new SimulationRoute(new SimulationRouteId("a-b"), a, b, new SimulationAnchorId("b.entry"))]),
            new SimulationScene(
                b,
                new SimulationBounds(80, 80),
                [new SimulationAnchor(new SimulationAnchorId("b.entry"), b, new SimulationPoint(5, 5), 2)],
                [])
        ]);
    }

    private sealed class RecordingResources(List<string> events) : ISceneResourceScopeHandoff
    {
        public void Leave(SimulationSceneId scene) => events.Add($"leave:{scene.Value}");
        public void Enter(SimulationSceneId scene) => events.Add($"enter:{scene.Value}");
    }

    private sealed class RecordingPresentation(List<string> events) : ISceneTransitionPresentation
    {
        public void CameraSnap(SimulationSceneId scene, SimulationAnchorId anchor)
        {
            events.Add($"camera:{scene.Value}:{anchor.Value}");
        }
    }
}
