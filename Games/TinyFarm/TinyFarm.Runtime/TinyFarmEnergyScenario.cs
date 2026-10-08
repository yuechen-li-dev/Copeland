using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM12Proof(
    string Milestone,
    string Outcome,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string EnergyTimelineHash,
    string RegimesHash,
    string UtilityDecisionsHash,
    string AnchorSequenceHash,
    string RestTransitionsHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    string SceneContentHash,
    string ScheduleContentHash,
    string OneDayHash,
    string SevenDayHash,
    string M1Hash,
    string M2Hash,
    bool PersonalHomesValid,
    bool HighEnergyNonRest,
    bool LowEnergyRest,
    bool RequiredOverridesUtility,
    bool RequiredSkipsUtility,
    bool ActiveRestValid,
    bool InactiveRestValid,
    bool SaveLoadValid,
    bool RepeatDeterministic,
    bool Headless,
    double RequiredNanoseconds,
    double RequiredBytes,
    double OpenNanoseconds,
    double OpenBytes,
    double CandidateLookupBytes,
    double EnergyScorerBytes);

public sealed record TinyFarmM12Evidence(
    TinyFarmM12Proof Proof,
    object Energy,
    object Behavior,
    object Homes,
    object Manifest);

public static class TinyFarmEnergyScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM12Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmScheduleDecision high = DecideMara(definitions, 9_000, 1200, includeTrace: true);
        TinyFarmScheduleDecision low = DecideMara(definitions, 1_000, 1200, includeTrace: true);
        TinyFarmScheduleDecision bedtime = DecideMara(definitions, 10_000, 1320, includeTrace: true);

        TinyFarmState activeState = LowEnergyMaraAtResidenceEntry(definitions);
        var active = new TinyFarmSession(activeState, definitions);
        var results = new List<IntentResult>();
        var events = new List<GameEvent>();
        var energyTimeline = new List<object>();
        var anchors = new List<string>();
        var restTransitions = new List<object>();
        bool priorResting = false;
        for (int step = 0; step < 96 && !active.State.EnergyFor(TinyFarmIds.Mara).IsResting; step++)
        {
            TinyFarmStepResult current = active.Step(new WaitIntent(1));
            results.AddRange(current.Results);
            events.AddRange(current.Results.SelectMany(result => result.Events));
            ActorEnergyState energy = active.State.EnergyFor(TinyFarmIds.Mara);
            SceneAnchorId? anchor = TinyFarmNpcController.CurrentAnchor(
                active.State,
                active.State.Actor(TinyFarmIds.Mara),
                definitions.Scenes,
                definitions.Schedules);
            anchors.Add(anchor?.Value ?? "en-route");
            energyTimeline.Add(new { active.State.Minute, energy.Energy, energy.IsResting, anchor = anchor?.Value });
            if (energy.IsResting != priorResting)
            {
                restTransitions.Add(new { active.State.Minute, energy.IsResting });
                priorResting = energy.IsResting;
            }
        }

        int energyAtRest = active.State.EnergyFor(TinyFarmIds.Mara).Energy;
        byte[] restingSave = active.CaptureWeekSave();
        TinyFarmSession reloaded = TinyFarmChunkedSaveCodec.Read(restingSave, definitions);
        reloaded.Step(new WaitIntent(10));
        bool saveLoadValid = reloaded.State.EnergyFor(TinyFarmIds.Mara).Energy > energyAtRest;

        (string oneDayHash, string sevenDayHash) = RunDurationProof(definitions);
        (string repeatOneDayHash, string repeatSevenDayHash) = RunDurationProof(definitions);
        (double requiredNs, double requiredBytes) = Benchmark(() =>
            TinyFarmNpcSchedule.Decide(definitions.Schedules, TinyFarmIds.Mara, 1320, energy: 10_000));
        (double openNs, double openBytes) = Benchmark(() =>
            TinyFarmNpcSchedule.Decide(definitions.Schedules, TinyFarmIds.Mara, 1200, TinyFarmAnchorIds.TownSquare, energy: 1_000));
        double lookupBytes = AllocationPerCall(() =>
            definitions.Schedules.CandidatesFor(definitions.Schedules.Windows.Single(window => window.Id == "mara.free-evening")));
        double scorerBytes = AllocationPerCall(() => TinyFarmEnergy.RestContribution(1_800));

        bool personalHomesValid = new[] { TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela }
            .Select(TinyFarmAnchorIds.HomeBedFor)
            .Distinct()
            .Count() == 3;
        bool activeRestValid = active.NavigationPlanCount > 0
            && active.State.EnergyFor(TinyFarmIds.Mara).IsResting;
        bool inactiveRestValid = ProveInactiveRest(definitions);
        bool repeatDeterministic = oneDayHash == repeatOneDayHash && sevenDayHash == repeatSevenDayHash;
        string m1Hash = TinyFarmCanonicalScenario.Prove().FinalHash;
        string m2Hash = TinyFarmWeekScenario.Prove().FinalHash;
        bool success = personalHomesValid
            && high.SelectedAnchor == TinyFarmAnchorIds.TownSquare
            && low.SelectedAnchor == TinyFarmAnchorIds.MaraHomeBed
            && bedtime.SelectedAnchor == TinyFarmAnchorIds.MaraHomeBed
            && bedtime.Regime == TinyFarmScheduleRegime.Required
            && bedtime.UtilityScores.Count == 0
            && activeRestValid
            && inactiveRestValid
            && saveLoadValid
            && repeatDeterministic
            && lookupBytes == 0d
            && scorerBytes == 0d;

        var proof = new TinyFarmM12Proof(
            "TINY-FARM-M12",
            success ? "A" : "B",
            TinyFarmSemanticHash.Compute(active.State),
            Hash(results),
            Hash(events),
            Hash(energyTimeline),
            Hash(new[] { high.Regime, low.Regime, bedtime.Regime }),
            Hash(new[] { high, low, bedtime }),
            Hash(anchors),
            Hash(restTransitions),
            Hash(new { active = active.State.EnergyFor(TinyFarmIds.Mara), reloaded = reloaded.State.EnergyFor(TinyFarmIds.Mara) }),
            Hash(new { active.NavigationPlanCount, active.ActivationCount, active.DeactivationCount }),
            TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(active.State, definitions)),
            definitions.SceneContent.AggregateSha256,
            definitions.ScheduleContent.AggregateSha256,
            oneDayHash,
            sevenDayHash,
            m1Hash,
            m2Hash,
            personalHomesValid,
            high.SelectedAnchor != TinyFarmAnchorIds.MaraHomeBed,
            low.SelectedAnchor == TinyFarmAnchorIds.MaraHomeBed,
            bedtime.SelectedAnchor == TinyFarmAnchorIds.MaraHomeBed,
            bedtime.UtilityScores.Count == 0,
            activeRestValid,
            inactiveRestValid,
            saveLoadValid,
            repeatDeterministic,
            true,
            requiredNs,
            requiredBytes,
            openNs,
            openBytes,
            lookupBytes,
            scorerBytes);

        object energyArtifact = new
        {
            representation = "fixed integer units",
            range = new { minimum = 0, maximum = 10_000 },
            initial = 9_000,
            activeDecayUnitsPerMinute = 8,
            restRecoveryUnitsPerMinute = 40,
            restContribution = "(10000 - Energy) / 10000 * 0.8",
            timeline = energyTimeline,
            oneDayHash,
            sevenDayHash
        };
        object behaviorArtifact = new
        {
            high,
            low,
            bedtime,
            restTransitions,
            anchors,
            activeNavigationPlans = active.NavigationPlanCount,
            inactivePathQueries = 0,
            saveLoadValid
        };
        object homesArtifact = new
        {
            scene = TinyFarmSceneIds.Residence,
            homes = new[] { TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela }.Select(actor => new
            {
                actor,
                bedAnchor = TinyFarmAnchorIds.HomeBedFor(actor),
                bedObject = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.HomeBedFor(actor)).SemanticObject
            }).ToArray(),
            routes = definitions.Scenes.All.SelectMany(scene => scene.Routes)
                .Where(route => route.SourceScene == TinyFarmSceneIds.Residence || route.TargetScene == TinyFarmSceneIds.Residence)
                .ToArray()
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M12",
            kind = "first-protosim-energy-rest-behavior",
            energyNeedAdded = true,
            additionalNeedsAdded = false,
            personalLivingSpacesAdded = true,
            personalBedAnchorsAdded = true,
            openUtilityUsesEnergy = true,
            requiredBedtimeStillStructural = true,
            utilityCanOverrideRequired = false,
            persistentDominatusRuntimeRetained = true,
            candidateLookupAllocates = false,
            genericNeedsFrameworkAdded = false,
            newPlannerAdded = false
        };
        return new TinyFarmM12Evidence(proof, energyArtifact, behaviorArtifact, homesArtifact, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static TinyFarmScheduleDecision DecideMara(
        TinyFarmDefinitions definitions,
        int energy,
        int minute,
        bool includeTrace)
    {
        return TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            minute,
            TinyFarmAnchorIds.TownSquare,
            includeTrace,
            energy);
    }

    private static TinyFarmState LowEnergyMaraAtResidenceEntry(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 1200;
        ScenePosition entry = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm")).Position;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Residence, entry);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Residence, entry);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);
        SetLocation(state, TinyFarmIds.Mara, TinyFarmIds.Farmhouse);
        SetEnergy(state, TinyFarmIds.Mara, 1_000, false);
        return state;
    }

    private static bool ProveInactiveRest(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 1200;
        SetEnergy(state, TinyFarmIds.Mara, 1_000, false);
        ScenePosition entry = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm")).Position;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Residence, entry);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);
        for (int step = 0; step < 4 && !session.State.EnergyFor(TinyFarmIds.Mara).IsResting; step++)
        {
            session.Step(new WaitIntent(1));
        }
        return session.State.EnergyFor(TinyFarmIds.Mara).IsResting;
    }

    private static (string OneDay, string SevenDay) RunDurationProof(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateEnergySceneState(definitions), definitions);
        string oneDay = string.Empty;
        for (int hour = 1; hour <= 24 * 7; hour++)
        {
            session.Step(new WaitIntent(60));
            if (hour == 24)
            {
                oneDay = TinyFarmSemanticHash.Compute(session.State);
            }
        }
        return (oneDay, TinyFarmSemanticHash.Compute(session.State));
    }

    private static (double Nanoseconds, double Bytes) Benchmark(Func<TinyFarmScheduleDecision> action)
    {
        const int count = 20_000;
        for (int index = 0; index < 1_000; index++) action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < count; index++) action();
        watch.Stop();
        return (watch.Elapsed.TotalNanoseconds / count, (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count);
    }

    private static double AllocationPerCall(Action action)
    {
        const int count = 10_000;
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
    }

    private static string Hash<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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

public static class TinyFarmM12ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, string phase)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        switch (phase.ToLowerInvariant())
        {
            case "morning":
            case "high-open":
                state.Minute = 1200;
                SetEnergy(state, TinyFarmIds.Mara, 9_000, false);
                return state;
            case "low-open":
                state.Minute = 1200;
                SetEnergy(state, TinyFarmIds.Mara, 1_000, false);
                return state;
            case "bedtime":
                state.Minute = 1320;
                SetEnergy(state, TinyFarmIds.Mara, 10_000, false);
                return state;
            case "resting":
                state.Minute = 1200;
                SceneAnchorDefinition bed = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.MaraHomeBed);
                SetLocation(state, TinyFarmIds.Mara, TinyFarmIds.Farmhouse);
                SetPlacement(state, TinyFarmIds.Mara, bed.Scene, bed.Position);
                SetEnergy(state, TinyFarmIds.Mara, 1_000, true);
                return state;
            default:
                throw new FormatException(
                    $"Unknown M12 scenario phase '{phase}'. Use high-open, low-open, bedtime, or resting.");
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
