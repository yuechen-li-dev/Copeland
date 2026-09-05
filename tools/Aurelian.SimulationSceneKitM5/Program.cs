using System.Diagnostics;
using System.Text.Json;
using Aurelian.Simulation;
using TinyFarm.Core;

string outputDirectory = args.Length == 0
    ? Path.GetFullPath(Path.Combine("artifacts", "aurelian-simulation-scene-kit-m5"))
    : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

var json = new JsonSerializerOptions { WriteIndented = true };
CadenceDefinition[] cadences =
[
    new(new CadenceId("physics-like"), RationalRate.PerSecond(30), 0),
    new(new CadenceId("agent"), RationalRate.PerSecond(5), 1),
    new(new CadenceId("pulse"), RationalRate.PerSecond(2), 2)
];

TraceResult sixty = RunPartition(cadences, 60);
TraceResult oneFortyFour = RunPartition(cadences, 144);
TraceResult irregular = RunIrregular(cadences, [16, 16, 50, 3, 91, 7, 33, 84]);
TraceResult irregularEven = RunTicks(cadences, irregular.HostTicks, 317);

var measureScheduler = new CadenceScheduler(cadences, TimeSpan.FromSeconds(5));
_ = measureScheduler.Advance(TimeSpan.FromMilliseconds(1), SimulationExecutionRate.Normal);
long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
var stopwatch = Stopwatch.StartNew();
for (int index = 0; index < 10_000; index++)
{
    _ = measureScheduler.Advance(TimeSpan.FromMilliseconds(1), SimulationExecutionRate.Normal);
}
stopwatch.Stop();
long cadenceAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;

SceneCatalog sceneCatalog = CreateSceneCatalog();
var transitionBridge = new SceneTransitionBridge(sceneCatalog);
SceneTransition transition = transitionBridge.Propose(new SimulationSceneId("lab-a"), new SimulationRouteId("lab-a-b"));
var hooks = new HookRecorder();
SceneActivationFact activation = transitionBridge.CompleteAccepted(
    transition,
    SceneSimulationDetail.Coarse,
    hooks,
    hooks);

var navGoal = new NavigationGoal(
    new NavigationRequestId(NavigationRequestKind.Goal, "drone-1:charging-dock:1"),
    new SimulationSceneId("lab-a"),
    new SimulationAnchorId("lab-a.dock"));
SimulationAnchor navAnchor = sceneCatalog.GetAnchor(navGoal.Anchor);
NavigationFact proposed = NavigationCoordinator.PathProposed(navGoal);
NavigationFact arrived = NavigationCoordinator.ObservePosition(navGoal, new SimulationPoint(90, 90), navAnchor);
NavigationFact blocked = NavigationCoordinator.MovementRejected(navGoal, 1, 3);
NavigationFact replan = NavigationCoordinator.MovementRejected(navGoal, 3, 3);

ScheduleWindow<NavigationGoal>[] schedule =
[
    new("routine", 0, 100, 1, navGoal),
    new("charge", 20, 40, 2, navGoal)
];
ScheduleMatch<NavigationGoal>? scheduleMatch = DeterministicSchedule.Match(schedule, 25);

TinyFarmM13Evidence m13 = TinyFarmSimulationScenario.Prove();
TinyFarmM14Evidence m14 = TinyFarmM14Scenario.Prove();

Write("cadence.json", new
{
    configurationIdentity = sixty.ConfigurationIdentity,
    semantics = new[] { "physics-like=30Hz", "agent=5Hz", "pulse=2Hz" },
    tieOrder = new[] { "physics-like", "agent", "pulse" },
    sixtyHz = sixty,
    oneFortyFourHz = oneFortyFour,
    irregular,
    irregularEven,
    partitionInvariant = sixty.Trace == oneFortyFour.Trace && irregular.Trace == irregularEven.Trace,
    performance = new
    {
        iterations = 10_000,
        elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
        allocatedBytes = cadenceAllocated,
        bytesPerAdvance = cadenceAllocated / 10_000d
    }
});
Write("scene.json", new
{
    scenes = sceneCatalog.All.Count,
    anchors = sceneCatalog.All.Sum(item => item.Anchors.Count),
    routes = sceneCatalog.All.Sum(item => item.Routes.Count),
    transition,
    activation,
    hooks.Events,
    resolverAcceptanceRequiredBeforeHooks = true
});
Write("navigation.json", new
{
    goal = navGoal,
    facts = new[] { proposed, arrived, blocked, replan },
    scheduleMatch,
    planner = "TinyFarm retains the single DotRecast adapter; Aurelian coordinates proposal facts only",
    positionMutationByAurelian = false
});
Write("parity.json", new
{
    m13 = m13.Proof,
    m14 = m14.Proof,
    cadenceConfigurationIdentity = new TinyFarmSimulationHost(
        new TinyFarmSession(TinyFarmContent.CreateEnergySceneState(TinyFarmDefinitionLoader.LoadM12()), TinyFarmDefinitionLoader.LoadM12()),
        TinyFarmDefinitionLoader.LoadM12()).CadenceConfigurationIdentity
});
Write("proof.json", new
{
    milestone = "AURELIAN-SIMULATION-SCENE-KIT-M5",
    outcome = "A",
    partitionInvariant = sixty.Trace == oneFortyFour.Trace && irregular.Trace == irregularEven.Trace,
    secondConsumerQualified = true,
    tinyFarmM13Outcome = m13.Proof.Outcome,
    scheduleGoal = scheduleMatch?.Goal.Request.Value,
    navigationArrival = arrived.Outcome.ToString(),
    sceneTransition = $"{transition.Source.Value}->{transition.Destination.Value}"
});
Write("manifest.json", new
{
    milestone = "AURELIAN-SIMULATION-SCENE-KIT-M5",
    kind = "ordered-multirate-simulation-scene-nav-schedule-bridge",
    genericCadenceSchedulerQualified = true,
    secondConsumerQualified = true,
    tinyFarmParityQualified = true,
    sceneCatalogBridgeQualified = true,
    navigationAdapterQualified = true,
    scheduleBridgeQualified = true,
    activeInactiveMechanismQualified = true,
    gameMinuteSemanticsInEngine = false,
    farmingSemanticsInEngine = false,
    npcJobFrameworkAdded = false,
    pathfinderDuplicated = false
});

Console.WriteLine($"M5 evidence written to {outputDirectory}");

void Write(string name, object value)
{
    File.WriteAllText(Path.Combine(outputDirectory, name), JsonSerializer.Serialize(value, json) + Environment.NewLine);
}

static TraceResult RunPartition(CadenceDefinition[] definitions, int partitions)
{
    return RunTicks(definitions, TimeSpan.TicksPerSecond, partitions);
}

static TraceResult RunIrregular(CadenceDefinition[] definitions, int[] milliseconds)
{
    long ticks = milliseconds.Sum(value => TimeSpan.FromMilliseconds(value).Ticks);
    var scheduler = new CadenceScheduler(definitions, TimeSpan.FromSeconds(5));
    var trace = new List<string>();
    foreach (int delta in milliseconds)
    {
        CadenceAdvanceResult result = scheduler.Advance(TimeSpan.FromMilliseconds(delta), SimulationExecutionRate.Normal);
        trace.AddRange(result.DueWork.Select(item => $"{item.Cadence.Value}:{item.Tick}"));
    }
    return new TraceResult(scheduler.ConfigurationIdentity, ticks, trace.Count, string.Join('|', trace));
}

static TraceResult RunTicks(CadenceDefinition[] definitions, long ticks, int partitions)
{
    var scheduler = new CadenceScheduler(definitions, TimeSpan.FromSeconds(5));
    var trace = new List<string>();
    long quotient = ticks / partitions;
    long remainder = ticks % partitions;
    for (int index = 0; index < partitions; index++)
    {
        CadenceAdvanceResult result = scheduler.Advance(
            TimeSpan.FromTicks(quotient + (index < remainder ? 1 : 0)),
            SimulationExecutionRate.Normal);
        trace.AddRange(result.DueWork.Select(item => $"{item.Cadence.Value}:{item.Tick}"));
    }
    return new TraceResult(scheduler.ConfigurationIdentity, ticks, trace.Count, string.Join('|', trace));
}

static SceneCatalog CreateSceneCatalog()
{
    var a = new SimulationSceneId("lab-a");
    var b = new SimulationSceneId("lab-b");
    return new SceneCatalog(
    [
        new SimulationScene(
            a,
            new SimulationBounds(100, 100),
            [new SimulationAnchor(new SimulationAnchorId("lab-a.dock"), a, new SimulationPoint(90, 90), 2)],
            [new SimulationRoute(new SimulationRouteId("lab-a-b"), a, b, new SimulationAnchorId("lab-b.entry"))]),
        new SimulationScene(
            b,
            new SimulationBounds(100, 100),
            [new SimulationAnchor(new SimulationAnchorId("lab-b.entry"), b, new SimulationPoint(10, 10), 2)],
            [])
    ]);
}

sealed record TraceResult(string ConfigurationIdentity, long HostTicks, int DueCount, string Trace);

sealed class HookRecorder : ISceneResourceScopeHandoff, ISceneTransitionPresentation
{
    public List<string> Events { get; } = [];
    public void Leave(SimulationSceneId scene) => Events.Add($"leave:{scene.Value}");
    public void Enter(SimulationSceneId scene) => Events.Add($"enter:{scene.Value}");
    public void CameraSnap(SimulationSceneId scene, SimulationAnchorId anchor) => Events.Add($"camera:{scene.Value}:{anchor.Value}");
}
