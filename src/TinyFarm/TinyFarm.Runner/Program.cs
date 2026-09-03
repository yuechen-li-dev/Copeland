using TinyFarm.Core;
using System.Diagnostics;

if (args.Contains("--m17", StringComparer.Ordinal))
{
    string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "tiny-farm-m17");
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
    }
    TinyFarmM17Scenario.WriteArtifacts(directory);
    TinyFarmM17Evidence evidence = TinyFarmM17Scenario.Prove();
    Console.WriteLine(TinyFarmM17Scenario.WriteJson(evidence.Proof));
    return;
}

if (args.Contains("--m17-control", StringComparer.Ordinal))
{
    TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
    var host = new TinyFarmSimulationHost(
        new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions),
        definitions,
        TinyFarmSimulationMode.Playing);
    Console.WriteLine("TinyFarm M17 control ready. Commands: inspect, select-slot <1-8>, pickup, interact, use-selected, quit.");
    while (Console.ReadLine() is string command)
    {
        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        IReadOnlyList<IntentResult> results = [];
        if (command.StartsWith("select-slot ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(command["select-slot ".Length..], out int slot))
        {
            results = host.ExecuteIntent(new SelectHotbarSlotIntent(new HotbarSlotId(slot))).Results;
        }
        else if (command.Equals("pickup", StringComparison.OrdinalIgnoreCase)
            || command.Equals("interact", StringComparison.OrdinalIgnoreCase))
        {
            results = host.ExecuteIntent(new InteractIntent()).Results;
        }
        else if (command.Equals("use-selected", StringComparison.OrdinalIgnoreCase))
        {
            results = host.ExecuteIntent(new UseSelectedIntent()).Results;
        }
        else if (!command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Expected inspect, select-slot <1-8>, pickup, interact, use-selected, or quit.");
        }

        Console.WriteLine(TinyFarmM17Scenario.WriteJson(new
        {
            simulation = host.Snapshot(),
            playerUi = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions),
            frame = TinyFarmFrameProjector.Project(host.Session.State, definitions),
            results
        }));
    }
    return;
}

if (args.Contains("--m16", StringComparer.Ordinal))
{
    string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "tiny-farm-m16");
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
    }
    TinyFarmM16Scenario.WriteArtifacts(directory);
    TinyFarmM16Evidence evidence = TinyFarmM16Scenario.Prove();
    Console.WriteLine(TinyFarmM16Scenario.WriteJson(evidence.Proof));
    return;
}

if (args.Contains("--m16-control", StringComparer.Ordinal))
{
    TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
    var host = new TinyFarmSimulationHost(
        new TinyFarmSession(TinyFarmM16ControlStates.Create(definitions), definitions),
        definitions,
        TinyFarmSimulationMode.Playing);
    Console.WriteLine(TinyFarmM16Scenario.WriteJson(new
    {
        simulation = host.Snapshot(),
        selectedHotbarSlot = host.Session.State.SelectedHotbarSlot,
        playerUi = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions)
    }));
    while (Console.ReadLine() is string command)
    {
        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }
        if (command.StartsWith("select-slot ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(command["select-slot ".Length..], out int slot))
        {
            host.ExecuteIntent(new SelectHotbarSlotIntent(new HotbarSlotId(slot)));
        }
        else if (!command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Expected inspect, select-slot <1-8>, or quit.");
        }
        Console.WriteLine(TinyFarmM16Scenario.WriteJson(new
        {
            simulation = host.Snapshot(),
            selectedHotbarSlot = host.Session.State.SelectedHotbarSlot,
            playerUi = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions)
        }));
    }
    return;
}

if (args.Contains("--m15-profile", StringComparer.Ordinal))
{
    if (args.Contains("--profile-wait", StringComparer.Ordinal))
    {
        Console.WriteLine($"M15_PROFILE_READY pid={Environment.ProcessId}");
        Thread.Sleep(TimeSpan.FromSeconds(5));
    }
    TinyFarmM15MovementMeasurement measurement =
        TinyFarmM15Scenario.MeasureAuthoritativeLocomotion();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        measurement,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Contains("--m15", StringComparer.Ordinal))
{
    string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "tiny-farm-m15");
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
    }
    TinyFarmM15Scenario.WriteArtifacts(directory);
    TinyFarmM15Evidence evidence = TinyFarmM15Scenario.Prove();
    Console.WriteLine(TinyFarmM15Scenario.WriteJson(evidence.Proof));
    return;
}

if (args.Contains("--m11-open-profile", StringComparer.Ordinal))
{
    TinyFarmDefinitions profileDefinitions = TinyFarmDefinitionLoader.Load();
    for (int index = 0; index < 10_000; index++)
    {
        _ = TinyFarmNpcSchedule.Decide(
            profileDefinitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare);
    }

    if (args.Contains("--profile-wait", StringComparer.Ordinal))
    {
        Console.WriteLine("M11_PROFILE_READY");
        Thread.Sleep(TimeSpan.FromSeconds(5));
    }

    const int profileDecisionCount = 100_000;
    long profileAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var profileStopwatch = Stopwatch.StartNew();
    for (int index = 0; index < profileDecisionCount; index++)
    {
        _ = TinyFarmNpcSchedule.Decide(
            profileDefinitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare);
    }
    profileStopwatch.Stop();
    long profileAllocated = GC.GetAllocatedBytesForCurrentThread() - profileAllocatedBefore;
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        workload = "TinyFarm warmed Open schedule decision",
        decisionCount = profileDecisionCount,
        elapsedMilliseconds = profileStopwatch.Elapsed.TotalMilliseconds,
        nanosecondsPerDecision = profileStopwatch.Elapsed.TotalNanoseconds / profileDecisionCount,
        allocatedBytes = profileAllocated,
        bytesPerDecision = profileAllocated / (double)profileDecisionCount
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Contains("--infra-m10a-benchmark", StringComparer.Ordinal))
{
    TinyFarmDefinitions benchmarkDefinitions = TinyFarmDefinitionLoader.Load();
    ActorId[] actors = [TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela];
    for (int index = 0; index < 1_000; index++)
    {
        _ = TinyFarmNpcSchedule.Decide(
            benchmarkDefinitions.Schedules,
            actors[index % actors.Length],
            index % 1440);
    }

    const int decisionCount = 100_000;
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    int gen0Before = GC.CollectionCount(0);
    var stopwatch = Stopwatch.StartNew();
    for (int index = 0; index < decisionCount; index++)
    {
        _ = TinyFarmNpcSchedule.Decide(
            benchmarkDefinitions.Schedules,
            actors[index % actors.Length],
            index % 1440);
    }
    stopwatch.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        workload = "TinyFarm repeated five-option schedule decision",
        decisionCount,
        elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
        nanosecondsPerDecision = stopwatch.Elapsed.TotalNanoseconds / decisionCount,
        decisionsPerSecond = decisionCount / stopwatch.Elapsed.TotalSeconds,
        allocatedBytes = allocated,
        bytesPerDecision = allocated / (double)decisionCount,
        gen0Collections = GC.CollectionCount(0) - gen0Before
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Contains("--repl", StringComparer.Ordinal))
{
    RunRepl();
    return;
}

if (args.Contains("--m12-control", StringComparer.Ordinal))
{
    RunM12Control();
    return;
}

if (args.Contains("--m13-control", StringComparer.Ordinal))
{
    RunM13Control();
    return;
}

if (args.Contains("--m13", StringComparer.Ordinal))
{
    TinyFarmM13Evidence evidence = TinyFarmSimulationScenario.Prove();
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmSimulationScenario.WriteJson(evidence.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "timing.json"), TinyFarmSimulationScenario.WriteJson(evidence.Timing) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "rates.json"), TinyFarmSimulationScenario.WriteJson(evidence.Rates) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "simulation-dto.tson"), evidence.SimulationDtoTson);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmSimulationScenario.WriteJson(evidence.Manifest) + Environment.NewLine);
    }
    Console.WriteLine(TinyFarmSimulationScenario.WriteJson(evidence.Proof));
    Environment.ExitCode = evidence.Proof.Outcome == "A" ? 0 : 1;
    return;
}

if (args.Contains("--m14", StringComparer.Ordinal))
{
    string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "tiny-farm-m14");
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
    }
    TinyFarmM14Scenario.WriteArtifacts(directory);
    TinyFarmM14Evidence evidence = TinyFarmM14Scenario.Prove();
    Console.WriteLine(TinyFarmM14Scenario.WriteJson(evidence.Proof));
    return;
}

bool runM1 = args.Contains("--m1", StringComparer.Ordinal);
bool runM3 = args.Contains("--m3", StringComparer.Ordinal);
bool runM4 = args.Contains("--m4", StringComparer.Ordinal);
bool runM5 = args.Contains("--m5", StringComparer.Ordinal);
bool runM6 = args.Contains("--m6", StringComparer.Ordinal);
bool runM7 = args.Contains("--m7", StringComparer.Ordinal);
bool runM8 = args.Contains("--m8", StringComparer.Ordinal);
bool runM9 = args.Contains("--m9", StringComparer.Ordinal);
bool runM10 = args.Contains("--m10", StringComparer.Ordinal);
bool runM11 = args.Contains("--m11", StringComparer.Ordinal);
bool runM12 = args.Contains("--m12", StringComparer.Ordinal);
if (args.Contains("--single-week", StringComparer.Ordinal))
{
    TinyFarmDefinitions singleDefinitions = TinyFarmDefinitionLoader.Load();
    TinyFarmWeekRun single = TinyFarmWeekScenario.Run(singleDefinitions, null);
    Console.WriteLine($"{single.FinalHash} {single.FinalState.Day} {single.ElapsedMicroseconds}");
    return;
}
TinyFarmM7Evidence? m7Evidence = runM7 ? TinyFarmTsonSceneScenario.Prove() : null;
TinyFarmM8Evidence? m8Evidence = runM8 ? TinyFarmScheduleScenario.Prove() : null;
TinyFarmM9Evidence? m9Evidence = runM9 ? TinyFarmTsonScheduleScenario.Prove() : null;
TinyFarmM10Evidence? m10Evidence = runM10 ? TinyFarmHybridScheduleScenario.Prove() : null;
TinyFarmM11Evidence? m11Evidence = runM11 ? TinyFarmPersistentActionScenario.Prove() : null;
TinyFarmM12Evidence? m12Evidence = runM12 ? TinyFarmEnergyScenario.Prove() : null;
object proof = runM1
    ? TinyFarmCanonicalScenario.Prove()
    : runM12
        ? m12Evidence!.Proof
    : runM11
        ? m11Evidence!.Proof
    : runM10
        ? m10Evidence!.Proof
    : runM9
        ? m9Evidence!.Proof
    : runM8
        ? m8Evidence!.Proof
    : runM7
        ? m7Evidence!.Proof
    : runM6
        ? TinyFarmAnchorHandoffScenario.Prove().Proof
    : runM5
        ? TinyFarmContinuousScenario.Prove().Proof
    : runM4
        ? TinyFarmSceneScenario.Prove().Proof
        : runM3
        ? TinyFarmGraphicalScenario.Prove().Proof
        : TinyFarmWeekScenario.Prove();
string json = runM1
    ? TinyFarmCanonicalScenario.WriteProofJson((TinyFarmM1Proof)proof)
    : runM12
        ? TinyFarmEnergyScenario.WriteJson(proof)
    : runM11
        ? TinyFarmPersistentActionScenario.WriteJson(proof)
    : runM10
        ? TinyFarmHybridScheduleScenario.WriteJson(proof)
    : runM9
        ? TinyFarmTsonScheduleScenario.WriteJson(proof)
    : runM8
        ? TinyFarmScheduleScenario.WriteJson(proof)
    : runM7
        ? TinyFarmTsonSceneScenario.WriteJson(proof)
    : runM6
        ? TinyFarmAnchorHandoffScenario.WriteJson((TinyFarmM6Proof)proof)
    : runM5
        ? TinyFarmContinuousScenario.WriteJson((TinyFarmM5Proof)proof)
    : runM4
        ? TinyFarmSceneScenario.WriteProofJson((TinyFarmM4Proof)proof)
        : runM3
        ? TinyFarmGraphicalScenario.WriteProofJson((TinyFarmM3Proof)proof)
        : TinyFarmWeekScenario.WriteProofJson((TinyFarmM2Proof)proof);

int outputIndex = Array.IndexOf(args, "--proof-json");
if (outputIndex >= 0)
{
    if (outputIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--proof-json requires an output path.");
    }

    string path = Path.GetFullPath(args[outputIndex + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}

if (runM3 || runM4)
{
    int projectionIndex = Array.IndexOf(args, "--projection-json");
    if (projectionIndex >= 0)
    {
        string path = RequiredOutputPath(args, projectionIndex, "--projection-json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TinyFarmFrame projection = runM4
            ? TinyFarmSceneScenario.Prove().FinalProjection
            : TinyFarmGraphicalScenario.Prove().FinalProjection;
        File.WriteAllText(path, TinyFarmFrameProjector.WriteJson(projection) + Environment.NewLine);
    }
}

if (runM12)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmEnergyScenario.WriteJson(m12Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "energy.json"), TinyFarmEnergyScenario.WriteJson(m12Evidence.Energy) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "behavior.json"), TinyFarmEnergyScenario.WriteJson(m12Evidence.Behavior) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "homes.json"), TinyFarmEnergyScenario.WriteJson(m12Evidence.Homes) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmEnergyScenario.WriteJson(m12Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM7)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmTsonSceneScenario.WriteJson(m7Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "content.json"), TinyFarmTsonSceneScenario.WriteJson(m7Evidence.Content) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "parity.json"), TinyFarmTsonSceneScenario.WriteJson(m7Evidence.Parity) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "provenance.json"), TinyFarmTsonSceneScenario.WriteJson(m7Evidence.Provenance) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmTsonSceneScenario.WriteJson(m7Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM8)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmScheduleScenario.WriteJson(m8Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "schedule-parity.json"), TinyFarmScheduleScenario.WriteJson(m8Evidence.ScheduleParity) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "decisions.json"), TinyFarmScheduleScenario.WriteJson(m8Evidence.Decisions) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "handoff.json"), TinyFarmScheduleScenario.WriteJson(m8Evidence.Handoff) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmScheduleScenario.WriteJson(m8Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM9)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmTsonScheduleScenario.WriteJson(m9Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "schedules.json"), TinyFarmTsonScheduleScenario.WriteJson(m9Evidence.Schedules) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "parity.json"), TinyFarmTsonScheduleScenario.WriteJson(m9Evidence.Parity) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "provenance.json"), TinyFarmTsonScheduleScenario.WriteJson(m9Evidence.Provenance) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmTsonScheduleScenario.WriteJson(m9Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM10)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmHybridScheduleScenario.WriteJson(m10Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "migration-parity.json"), TinyFarmHybridScheduleScenario.WriteJson(m10Evidence.MigrationParity) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "regimes.json"), TinyFarmHybridScheduleScenario.WriteJson(m10Evidence.Regimes) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "utility-decisions.json"), TinyFarmHybridScheduleScenario.WriteJson(m10Evidence.UtilityDecisions) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmHybridScheduleScenario.WriteJson(m10Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM11)
{
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmPersistentActionScenario.WriteJson(m11Evidence!.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "allocations.json"), TinyFarmPersistentActionScenario.WriteJson(m11Evidence.Allocations) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "performance.json"), TinyFarmPersistentActionScenario.WriteJson(m11Evidence.Performance) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "runtime-lifetime.json"), TinyFarmPersistentActionScenario.WriteJson(m11Evidence.RuntimeLifetime) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmPersistentActionScenario.WriteJson(m11Evidence.Manifest) + Environment.NewLine);
    }
}

if (runM6)
{
    TinyFarmM6Evidence evidence = TinyFarmAnchorHandoffScenario.Prove();
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmAnchorHandoffScenario.WriteJson(evidence.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "anchors.json"), TinyFarmAnchorHandoffScenario.WriteJson(evidence.Anchors) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "handoff.json"), TinyFarmAnchorHandoffScenario.WriteJson(evidence.Handoff) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "navigation.json"), TinyFarmAnchorHandoffScenario.WriteJson(evidence.Navigation) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmAnchorHandoffScenario.WriteJson(new
        {
            milestone = "TINY-FARM-M6",
            kind = "semantic-scene-anchors-active-inactive-handoff",
            npcGoalsUseSemanticAnchors = true,
            hardcodedScheduleCoordinatesRemoved = true,
            activeNpcUsesSpatialNavigation = true,
            inactiveNpcUsesCoarseSimulation = true,
            handoffDeterministic = true,
            dotRecastStatePersisted = false,
            rendererOwnsNpcState = false,
            ecsAdded = false,
            streamingSystemAdded = false,
            sceneDslAdded = false
        }) + Environment.NewLine);
    }
}

if (runM5)
{
    TinyFarmM5Evidence evidence = TinyFarmContinuousScenario.Prove();
    int artifactIndex = Array.IndexOf(args, "--artifact-dir");
    if (artifactIndex >= 0)
    {
        string directory = RequiredOutputPath(args, artifactIndex, "--artifact-dir");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proof.json"), TinyFarmContinuousScenario.WriteJson(evidence.Proof) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "navigation.json"), TinyFarmContinuousScenario.WriteJson(evidence.Navigation) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "interaction.json"), TinyFarmContinuousScenario.WriteJson(evidence.Interaction) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "projection.json"), TinyFarmFrameProjector.WriteJson(evidence.Projection) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TinyFarmContinuousScenario.WriteJson(new
        {
            milestone = "TINY-FARM-M5",
            kind = "continuous-locomotion-interaction-navigation",
            continuousAuthoritativePosition = true,
            gridLockedActorMovement = false,
            rendererOwnsMovement = false,
            facingSemantic = true,
            interactionTargetSemantic = true,
            dotRecastUsed = true,
            dotRecastOwnsWorldState = false,
            dotRecastTypesLeakIntoCore = false,
            npcVisibleLocomotion = true,
            sceneGraphNavigationSeparatedFromSpatialNavigation = true,
            ecsAdded = false,
            physicsEngineAdded = false,
            headlessNavigation = true
        }) + Environment.NewLine);
    }
}

if (runM4)
{
    int scenesIndex = Array.IndexOf(args, "--scenes-json");
    if (scenesIndex >= 0)
    {
        string path = RequiredOutputPath(args, scenesIndex, "--scenes-json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, TinyFarmSceneScenario.WriteScenesJson() + Environment.NewLine);
    }

    int routesIndex = Array.IndexOf(args, "--routes-json");
    if (routesIndex >= 0)
    {
        string path = RequiredOutputPath(args, routesIndex, "--routes-json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, TinyFarmSceneScenario.WriteRoutesJson() + Environment.NewLine);
    }
}

if (!runM1 && !runM3 && !runM4 && !runM5 && !runM6 && !runM7 && !runM8 && !runM9 && !runM10 && !runM11)
{
    TinyFarmDefinitions artifactDefinitions = TinyFarmDefinitionLoader.Load();
    TinyFarmWeekRun artifactRun = TinyFarmWeekScenario.Run(artifactDefinitions, null);
    int saveIndex = Array.IndexOf(args, "--save-file");
    if (saveIndex >= 0)
    {
        string path = RequiredOutputPath(args, saveIndex, "--save-file");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TinyFarmWeekScenario.CaptureFinalSave(artifactDefinitions));
    }

    int saveBase64Index = Array.IndexOf(args, "--save-base64");
    if (saveBase64Index >= 0)
    {
        string path = RequiredOutputPath(args, saveBase64Index, "--save-base64");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Convert.ToBase64String(TinyFarmWeekScenario.CaptureFinalSave(artifactDefinitions)) + Environment.NewLine);
    }

    int replayIndex = Array.IndexOf(args, "--replay-json");
    if (replayIndex >= 0)
    {
        string path = RequiredOutputPath(args, replayIndex, "--replay-json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            milestone = "TINY-FARM-M2",
            artifactRun.FinalHash,
            artifactRun.ResultSequence,
            artifactRun.EventSequence
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    int inspectionIndex = Array.IndexOf(args, "--inspection-json");
    if (inspectionIndex >= 0)
    {
        string path = RequiredOutputPath(args, inspectionIndex, "--inspection-json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var finalSession = new TinyFarmSession(artifactRun.FinalState, artifactDefinitions);
        File.WriteAllText(path, TinyFarmInspector.WriteJson(finalSession, []) + Environment.NewLine);
    }
}

Console.WriteLine(json);
string outcome = runM1
    ? ((TinyFarmM1Proof)proof).Outcome
    : runM12
        ? ((TinyFarmM12Proof)proof).Outcome
    : runM11
        ? ((TinyFarmM11Proof)proof).Outcome
    : runM10
        ? ((TinyFarmM10Proof)proof).Outcome
    : runM9
        ? ((TinyFarmM9Proof)proof).Outcome
    : runM8
        ? ((TinyFarmM8Proof)proof).Outcome
    : runM7
        ? ((TinyFarmM7Proof)proof).Outcome
    : runM6
        ? ((TinyFarmM6Proof)proof).Outcome
    : runM5
        ? ((TinyFarmM5Proof)proof).Outcome
    : runM4
        ? ((TinyFarmM4Proof)proof).Outcome
        : runM3
        ? ((TinyFarmM3Proof)proof).Outcome
        : ((TinyFarmM2Proof)proof).Outcome;
Environment.ExitCode = outcome == "A" ? 0 : 1;

static string RequiredOutputPath(string[] arguments, int optionIndex, string option)
{
    if (optionIndex + 1 >= arguments.Length)
    {
        throw new ArgumentException($"{option} requires an output path.");
    }

    return Path.GetFullPath(arguments[optionIndex + 1]);
}

static void RunRepl()
{
    TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
    var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
    IReadOnlyList<IntentResult> lastResults = [];
    Console.WriteLine("TinyFarm M2 — Headless Deterministic Week");

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine(TinyFarmTextProjection.Describe(session.State, definitions));
        Console.Write("\n> ");
        string? command = Console.ReadLine();
        if (command is null || command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(TinyFarmInspector.WriteJson(session, lastResults));
            continue;
        }

        try
        {
            TinyFarmStepResult step = session.Step(TinyFarmCommandParser.Parse(command));
            lastResults = step.Results;
            foreach (IntentResult result in step.Results.Where(result => result.Envelope.Source == IntentSourceKind.Human))
            {
                Console.WriteLine($"{result.Status}: {result.Reason}");
            }

            foreach (NarrativeLine line in step.Narrative)
            {
                Console.WriteLine($"{line.Speaker}: {line.Text}");
            }
        }
        catch (FormatException exception)
        {
            Console.WriteLine(exception.Message);
        }
    }
}

static void RunM12Control()
{
    TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
    var session = new TinyFarmSession(TinyFarmM12ControlStates.Create(definitions, "high-open"), definitions);
    IReadOnlyList<IntentResult> lastResults = [];
    Console.WriteLine("TinyFarm M12 control ready. Commands: scenario <phase>, inspect, frame, wait <minutes>, quit.");

    while (Console.ReadLine() is string line)
    {
        string command = line.Trim();
        if (command.Length == 0)
        {
            continue;
        }
        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (command.StartsWith("scenario ", StringComparison.OrdinalIgnoreCase))
            {
                string phase = command["scenario ".Length..].Trim();
                session = new TinyFarmSession(TinyFarmM12ControlStates.Create(definitions, phase), definitions);
                lastResults = [];
                Console.WriteLine(TinyFarmInspector.WriteJson(session, lastResults));
                continue;
            }
            if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(TinyFarmInspector.WriteJson(session, lastResults));
                continue;
            }
            if (command.Equals("frame", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(TinyFarmFrameProjector.WriteJson(
                    TinyFarmFrameProjector.Project(session.State, definitions)));
                continue;
            }

            TinyFarmStepResult step = session.Step(TinyFarmCommandParser.Parse(command));
            lastResults = step.Results;
            Console.WriteLine(TinyFarmInspector.WriteJson(session, lastResults));
        }
        catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException)
        {
            Console.WriteLine($"ERROR: {exception.Message}");
        }
    }
}

static void RunM13Control()
{
    TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
    var host = new TinyFarmSimulationHost(
        new TinyFarmSession(TinyFarmContent.CreateEnergySceneState(definitions), definitions),
        definitions);
    IReadOnlyList<IntentResult> lastResults = [];
    Console.WriteLine("TinyFarm M13 control ready. Commands: pause, play, fast-forward, advance <minutes>, host-ms <milliseconds>, inspect, snapshot-tson, quit.");

    while (Console.ReadLine() is string line)
    {
        string command = line.Trim();
        if (command.Length == 0)
        {
            continue;
        }
        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(TinyFarmInspector.WriteJson(host.Session, lastResults));
                continue;
            }
            if (command.Equals("snapshot-tson", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write(TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(host.Snapshot()));
                continue;
            }
            if (command.StartsWith("host-ms ", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(command["host-ms ".Length..], out long milliseconds)
                && milliseconds >= 0)
            {
                TinyFarmHostAdvanceResult result = host.AdvanceHostTime(TimeSpan.FromMilliseconds(milliseconds));
                lastResults = result.Results;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    mode = host.Mode,
                    result.WorldMinutesAdvanced,
                    result.LocomotionStepsAdvanced,
                    stateHash = TinyFarmSemanticHash.Compute(host.Session.State)
                }));
                continue;
            }

            host.Execute(TinyFarmSimulationCommandParser.Parse(command));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                mode = host.Mode,
                day = host.Session.State.Day,
                minute = host.Session.State.Minute,
                stateHash = TinyFarmSemanticHash.Compute(host.Session.State)
            }));
        }
        catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            Console.WriteLine($"ERROR: {exception.Message}");
        }
    }
}
