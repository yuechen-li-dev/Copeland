using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public static class TinyFarmM14ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, string phase)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 480;
        SceneAnchorDefinition playerStart = definitions.Scenes.GetAnchor(new SceneAnchorId("farm.start"));
        SetPlacement(state, TinyFarmIds.Player, playerStart.Scene, playerStart.Position);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);

        switch (phase.ToLowerInvariant())
        {
            case "wander":
                SetEnergy(state, TinyFarmIds.Elias, 9_000, false);
                return state;
            case "low-energy":
                SetEnergy(state, TinyFarmIds.Elias, 1_000, false);
                return state;
            default:
                throw new FormatException("Unknown M14 scenario phase. Use wander or low-energy.");
        }
    }

    private static void SetEnergy(TinyFarmState state, ActorId actor, int energy, bool resting)
    {
        int index = state.MutableActorEnergy.FindIndex(item => item.Actor == actor);
        state.MutableActorEnergy[index] = new ActorEnergyState(actor, energy, resting);
    }

    private static void SetPlacement(TinyFarmState state, ActorId actor, SceneId scene, ScenePosition position)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[index] = new ActorSceneState(actor, scene, position);
    }

    private static void SetLocation(TinyFarmState state, ActorId actor, LocationId location)
    {
        int index = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[index] = state.MutableActors[index] with { Location = location };
    }
}

public sealed record TinyFarmM14Evidence(
    object Proof,
    object Locomotion,
    object Wander,
    object Cadence,
    object Manifest);

public static class TinyFarmM14Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM14Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        host.Session.Step(new LookIntent());
        ScenePosition initial = host.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        int initialEnergy = host.Session.State.EnergyFor(TinyFarmIds.Elias).Energy;
        long initialDecisions = host.Session.DecisionEvaluationCount;
        int initialQueries = host.Session.NavigationPlanCount;
        host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        ScenePosition beforeMinute = host.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;

        TinyFarmSimulationHost sixty = CreateHost(definitions);
        TinyFarmSimulationHost oneFortyFour = CreateHost(definitions);
        sixty.Session.Step(new LookIntent());
        oneFortyFour.Session.Step(new LookIntent());
        AdvancePartitioned(sixty, TimeSpan.FromSeconds(60), 3_600);
        AdvancePartitioned(oneFortyFour, TimeSpan.FromSeconds(60), 8_640);

        TinyFarmSimulationHost irregular = CreateHost(definitions);
        TinyFarmSimulationHost even = CreateHost(definitions);
        irregular.Session.Step(new LookIntent());
        even.Session.Step(new LookIntent());
        int[] pattern = [16, 16, 50, 3, 91, 7, 33, 84];
        long ticks = 0;
        for (int repeat = 0; repeat < 25; repeat++)
        {
            foreach (int milliseconds in pattern)
            {
                irregular.AdvanceHostTime(TimeSpan.FromMilliseconds(milliseconds));
                ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
            }
        }
        AdvancePartitioned(even, TimeSpan.FromTicks(ticks), 317);

        TinyFarmSimulationHost paused = CreateHost(definitions);
        paused.Session.Step(new LookIntent());
        paused.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        ScenePosition pausePosition = paused.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        paused.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Paused));
        paused.AdvanceHostTime(TimeSpan.FromSeconds(5));
        bool pauseFreezes = pausePosition == paused.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        paused.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        paused.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        bool resumeContinues = pausePosition != paused.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;

        TinyFarmScheduleDecision high = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Elias,
            480,
            TinyFarmAnchorIds.FarmHome,
            includeTrace: true,
            energy: 9_000);
        TinyFarmScheduleDecision low = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Elias,
            480,
            TinyFarmAnchorIds.FarmWanderA,
            includeTrace: true,
            energy: 1_000);
        TinyFarmScheduleDecision bedtime = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Elias,
            1320,
            TinyFarmAnchorIds.FarmWanderA,
            includeTrace: true,
            energy: 10_000);

        bool renderEquivalent = Equivalent(sixty, oneFortyFour);
        bool irregularEquivalent = Equivalent(irregular, even);
        bool movedWithoutMinute = beforeMinute != initial
            && host.WorldMinutesAdvanced == 0
            && host.Session.State.EnergyFor(TinyFarmIds.Elias).Energy == initialEnergy
            && host.Session.DecisionEvaluationCount == initialDecisions
            && host.Session.NavigationPlanCount == initialQueries;
        bool success = movedWithoutMinute
            && renderEquivalent
            && irregularEquivalent
            && pauseFreezes
            && resumeContinues
            && TinyFarmAnchorIds.IsWander(high.SelectedAnchor)
            && low.SelectedAnchor == TinyFarmAnchorIds.EliasHomeBed
            && bedtime.SelectedAnchor == TinyFarmAnchorIds.EliasHomeBed;

        TinyFarmSimulationHost allocationHost = CreateHost(definitions);
        allocationHost.Session.Step(new LookIntent());
        allocationHost.Session.AdvanceActiveNpcLocomotion();
        long reductionsBefore = allocationHost.NpcLocomotionReductions;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 100; step++)
        {
            allocationHost.Session.AdvanceActiveNpcLocomotion();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long measuredReductions = allocationHost.NpcLocomotionReductions - reductionsBefore;
        double npcMovementBytesPerStep = measuredReductions == 0
            ? 0d
            : allocated / (double)measuredReductions;

        object proof = new
        {
            milestone = "TINY-FARM-M14",
            outcome = success ? "A" : "B",
            movedWithoutWorldMinute = movedWithoutMinute,
            sixtyVsOneFortyFourEquivalent = renderEquivalent,
            irregularEquivalent,
            pauseFreezes,
            resumeContinues,
            stateHash = TinyFarmSemanticHash.Compute(sixty.Session.State),
            sceneContentHash = definitions.SceneContent.AggregateSha256,
            scheduleContentHash = definitions.ScheduleContent.AggregateSha256
        };
        object locomotion = new
        {
            speedLaw = "16 integer world units per 60 Hz locomotion opportunity",
            initial,
            beforeMinute,
            sixtyHzPosition = sixty.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition,
            sixtyHzFacing = sixty.Session.State.ActorScene(TinyFarmIds.Elias).Facing,
            waypointIndex = sixty.Session.WaypointIndexFor(TinyFarmIds.Elias),
            npcMovementBytesPerStep,
            allocationBoundary = "full authoritative resolver reduction including result/event projection; path collections are reused"
        };
        object wander = new
        {
            representation = "authored local SceneAnchorKind.Wander anchors",
            anchors = new[] { TinyFarmAnchorIds.FarmWanderA, TinyFarmAnchorIds.FarmWanderB },
            high,
            low,
            bedtime,
            commitment = "retain current local Wander goal until AnchorReached or a non-Wander policy override"
        };
        object cadence = new
        {
            renderObservations = 3_600,
            locomotionOpportunities = sixty.LocomotionStepsAdvanced,
            playerLocomotionReductions = sixty.PlayerLocomotionReductions,
            npcLocomotionReductions = sixty.NpcLocomotionReductions,
            worldMinutes = sixty.WorldMinutesAdvanced,
            npcPolicyEvaluations = sixty.Session.DecisionEvaluationCount,
            dotRecastQueries = sixty.Session.NavigationPlanCount,
            anchorArrivals = sixty.AnchorArrivals
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M14",
            kind = "active-npc-fixed-step-locomotion-local-wander",
            npcLocomotionUsesFixedDomain = true,
            npcPolicyUsesRenderCadence = false,
            npcPolicyUsesLocomotionCadence = false,
            pathPlanningUsesLocomotionCadence = false,
            rendererOwnsNpcMovement = false,
            localWanderAdded = true,
            wanderCrossesScenes = false,
            newNeedAdded = false,
            physicsEngineAdded = false,
            genericSchedulerAdded = false
        };
        return new TinyFarmM14Evidence(proof, locomotion, wander, cadence, manifest);
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM14Evidence evidence = Prove();
        File.WriteAllText(Path.Combine(directory, "proof.json"), WriteJson(evidence.Proof));
        File.WriteAllText(Path.Combine(directory, "locomotion.json"), WriteJson(evidence.Locomotion));
        File.WriteAllText(Path.Combine(directory, "wander.json"), WriteJson(evidence.Wander));
        File.WriteAllText(Path.Combine(directory, "cadence.json"), WriteJson(evidence.Cadence));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), WriteJson(evidence.Manifest));
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
    }

    private static TinyFarmSimulationHost CreateHost(TinyFarmDefinitions definitions)
    {
        return new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM14ControlStates.Create(definitions, "wander"), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
    }

    private static void AdvancePartitioned(TinyFarmSimulationHost host, TimeSpan total, int partitions)
    {
        long quotient = total.Ticks / partitions;
        long remainder = total.Ticks % partitions;
        for (int index = 0; index < partitions; index++)
        {
            host.ObserveRenderFrame();
            host.AdvanceHostTime(TimeSpan.FromTicks(quotient + (index < remainder ? 1 : 0)));
        }
    }

    private static bool Equivalent(TinyFarmSimulationHost left, TinyFarmSimulationHost right)
    {
        ActorSceneState leftActor = left.Session.State.ActorScene(TinyFarmIds.Elias);
        ActorSceneState rightActor = right.Session.State.ActorScene(TinyFarmIds.Elias);
        return TinyFarmSemanticHash.Compute(left.Session.State) == TinyFarmSemanticHash.Compute(right.Session.State)
            && leftActor.WorldPosition == rightActor.WorldPosition
            && leftActor.Facing == rightActor.Facing
            && left.Session.WaypointIndexFor(TinyFarmIds.Elias) == right.Session.WaypointIndexFor(TinyFarmIds.Elias)
            && left.Session.NavigationTargetFor(TinyFarmIds.Elias) == right.Session.NavigationTargetFor(TinyFarmIds.Elias)
            && left.WorldMinutesAdvanced == right.WorldMinutesAdvanced
            && left.LocomotionStepsAdvanced == right.LocomotionStepsAdvanced
            && left.Session.DecisionEvaluationCount == right.Session.DecisionEvaluationCount
            && left.Session.NavigationPlanCount == right.Session.NavigationPlanCount;
    }
}
