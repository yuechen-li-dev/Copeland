using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM5Proof(
    string Milestone,
    string Outcome,
    string FinalStateHash,
    string ResultHash,
    string EventHash,
    string NavigationHash,
    string InteractionTargetHash,
    string ProjectionHash,
    string M1Hash,
    string M2Hash,
    bool MidTileSaveLoadExact,
    bool NpcMovedContinuously,
    bool PlayerSubTileMovement,
    bool DialogueTargeted,
    bool FarmTargeted,
    bool Headless,
    double NavigationBuildMilliseconds,
    double NavigationQueryMilliseconds,
    double NpcMovementUpdateMilliseconds);

public sealed record TinyFarmM5Evidence(
    TinyFarmM5Proof Proof,
    object Navigation,
    object Interaction,
    TinyFarmFrame Projection);

public static class TinyFarmContinuousScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM5Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        var planner = new DotRecastNavigationPlanner();
        var session = new TinyFarmSession(TinyFarmContent.CreateContinuousSceneState(definitions), definitions);
        var results = new List<string>();
        var events = new List<string>();
        var targets = new List<string>();

        ScenePosition initial = session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        Apply(session, new SpatialMoveIntent(0, 1, 128), results, events);
        bool subTile = session.State.ActorScene(TinyFarmIds.Player).WorldPosition.YUnits == initial.YUnits + 128;
        WalkTo(session, planner, At(5, 7), results, events);
        Face(session, -1, 0, results, events);
        targets.Add(TargetSignature(session));
        Apply(session, new InteractIntent(), results, events);
        bool talkedToElias = events.Any(item => item.Contains("Conversation:player:elias", StringComparison.Ordinal));
        Apply(session, new WaitIntent(60), results, events);

        WalkTo(session, planner, At(17, 6), results, events);
        Apply(session, new InteractIntent(), results, events);
        WalkTo(session, planner, At(11, 5), results, events);
        Apply(session, new InteractIntent(), results, events);
        WalkTo(session, planner, At(11, 7), results, events);
        Face(session, 1, 0, results, events);
        targets.Add(TargetSignature(session));
        Apply(session, new InteractIntent(), results, events);
        bool talkedToMara = events.Any(item => item.Contains("Conversation:player:mara", StringComparison.Ordinal));

        WalkTo(session, planner, At(17, 4), results, events);
        Apply(session, new InteractIntent(), results, events);
        WalkTo(session, planner, At(5, 4), results, events);
        Face(session, 0, -1, results, events);
        Apply(session, new BuyProductIntent(TinyFarmIds.TurnipSeed), results, events);
        WalkTo(session, planner, At(5, 7), results, events);
        Apply(session, new InteractIntent(), results, events);
        WalkTo(session, planner, At(10, 13), results, events);
        Apply(session, new InteractIntent(), results, events);
        WalkTo(session, planner, At(2, 7), results, events);
        Apply(session, new InteractIntent(), results, events);

        WalkTo(session, planner, At(6, 5), results, events);
        Face(session, 1, 0, results, events);
        targets.Add(TargetSignature(session));
        Apply(session, new InteractIntent(), results, events);
        Apply(session, new InteractIntent(), results, events);
        bool farmTargeted = session.State.FarmPlots.Single(item => item.Id == TinyFarmIds.PlotOne) is
        { Crop: not null, WateredToday: true };

        Apply(session, new SpatialMoveIntent(0, 1, 333), results, events);
        ActorSceneState savedPlacement = session.State.ActorScene(TinyFarmIds.Player);
        string savedHash = TinyFarmSemanticHash.Compute(session.State);
        byte[] save = session.CaptureWeekSave();
        session = TinyFarmChunkedSaveCodec.Read(save, definitions);
        bool saveExact = session.State.ActorScene(TinyFarmIds.Player) == savedPlacement
            && TinyFarmSemanticHash.Compute(session.State) == savedHash;
        Apply(session, new SpatialMoveIntent(0, -1, 64), results, events);

        TinyFarmState npcState = TinyFarmContent.CreateContinuousSceneState(definitions);
        SetNpcForVisibleWalk(npcState);
        var npcSession = new TinyFarmSession(npcState, definitions);
        ScenePosition npcBefore = npcSession.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        var npcStopwatch = Stopwatch.StartNew();
        TinyFarmStepResult npcStep = npcSession.Step(new LookIntent());
        npcStopwatch.Stop();
        ScenePosition npcAfter = npcStep.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        bool npcMovedContinuously = npcAfter != npcBefore
            && Math.Abs(npcAfter.XUnits - npcBefore.XUnits) <= ScenePosition.UnitsPerTile / 8
            && Math.Abs(npcAfter.YUnits - npcBefore.YUnits) <= ScenePosition.UnitsPerTile / 8;

        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        NavigationPath navigation = planner.FindPath(farm, At(11, 4), At(13, 4));
        TinyFarmFrame projection = TinyFarmFrameProjector.Project(session.State, definitions);
        string navigationSignature = string.Join(';', navigation.Waypoints.Select(item => $"{item.XUnits},{item.YUnits}"));
        string interactionSignature = string.Join(';', targets);
        string m1Hash = TinyFarmCanonicalScenario.Prove().FinalHash;
        string m2Hash = TinyFarmWeekScenario.Prove().FinalHash;
        bool success = subTile
            && talkedToElias
            && talkedToMara
            && farmTargeted
            && saveExact
            && npcMovedContinuously
            && navigation.Succeeded
            && m1Hash == ExpectedM1Hash
            && m2Hash == ExpectedM2Hash;
        var proof = new TinyFarmM5Proof(
            "TINY-FARM-M5",
            success ? "A" : "B",
            TinyFarmSemanticHash.Compute(session.State),
            Hash(results),
            Hash(events),
            Hash([navigationSignature]),
            Hash(targets),
            TinyFarmFrameProjector.ComputeHash(projection),
            m1Hash,
            m2Hash,
            saveExact,
            npcMovedContinuously,
            subTile,
            talkedToElias && talkedToMara,
            farmTargeted,
            true,
            navigation.BuildMilliseconds,
            navigation.QueryMilliseconds,
            npcStopwatch.Elapsed.TotalMilliseconds);
        object navigationEvidence = new
        {
            backend = "DotRecast Recast + Detour 2026.3.1",
            sourceScene = farm.Id.Value,
            geometry = "scene bounds plus blocking layout rows",
            navigation.Failure,
            navigation.Waypoints,
            navigation.BuildMilliseconds,
            navigation.QueryMilliseconds,
            staticCache = "per SceneId; not persisted"
        };
        object interactionEvidence = new
        {
            selectionLaw = "kind priority, squared distance, ordinal stable semantic identity",
            rangeUnits = TinyFarmSpatialQueries.InteractionRangeUnits,
            targets
        };
        return new TinyFarmM5Evidence(proof, navigationEvidence, interactionEvidence, projection);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static void WalkTo(
        TinyFarmSession session,
        INavigationPlanner planner,
        ScenePosition goal,
        ICollection<string> results,
        ICollection<string> events)
    {
        ActorSceneState actor = session.State.ActorScene(TinyFarmIds.Player);
        NavigationPath path = planner.FindPath(session.SceneCatalog.Get(actor.Scene), actor.WorldPosition, goal);
        if (!path.Succeeded)
        {
            throw new InvalidOperationException($"M5 path failed in '{actor.Scene}': {path.Failure}/{path.FailureDetail}");
        }
        foreach (ScenePosition waypoint in path.Waypoints.Skip(1))
        {
            int guard = 0;
            while (session.State.ActorScene(TinyFarmIds.Player).WorldPosition != waypoint)
            {
                if (guard++ > 4096)
                {
                    throw new InvalidOperationException("M5 locomotion did not converge on its DotRecast waypoint.");
                }
                ScenePosition current = session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
                int deltaX = waypoint.XUnits - current.XUnits;
                int deltaY = waypoint.YUnits - current.YUnits;
                int directionX = Math.Abs(deltaX) >= Math.Abs(deltaY) ? Math.Sign(deltaX) : 0;
                int directionY = directionX == 0 ? Math.Sign(deltaY) : 0;
                int remaining = directionX == 0 ? Math.Abs(deltaY) : Math.Abs(deltaX);
                Apply(
                    session,
                    new SpatialMoveIntent(directionX, directionY, Math.Min(128, remaining)),
                    results,
                    events);
            }
        }
    }

    private static void Face(
        TinyFarmSession session,
        int deltaX,
        int deltaY,
        ICollection<string> results,
        ICollection<string> events)
    {
        Apply(session, new SpatialMoveIntent(-deltaX, -deltaY, 1), results, events);
        Apply(session, new SpatialMoveIntent(deltaX, deltaY, 1), results, events);
    }

    private static void Apply(
        TinyFarmSession session,
        GameIntent intent,
        ICollection<string> results,
        ICollection<string> events)
    {
        TinyFarmStepResult step = session.Step(intent);
        IntentResult human = step.Results.Single(item => item.Envelope.Source == IntentSourceKind.Human);
        if (human.Status != IntentResultStatus.Accepted)
        {
            throw new InvalidOperationException($"M5 intent '{intent}' failed: {human.Status}/{human.Reason}.");
        }
        foreach (IntentResult result in step.Results)
        {
            results.Add($"{result.Envelope.Actor}:{result.Envelope.Intent}:{result.Status}:{result.Reason}");
            foreach (GameEvent gameEvent in result.Events)
            {
                events.Add($"{gameEvent.Kind}:{gameEvent.Actor}:{gameEvent.Target}:{gameEvent.Scene}:{gameEvent.Route}");
            }
        }
    }

    private static string TargetSignature(TinyFarmSession session)
    {
        return TinyFarmSpatialQueries.SelectInteractionTarget(
            session.State,
            TinyFarmIds.Player,
            session.SceneCatalog)?.StableId ?? "none";
    }

    private static ScenePosition At(int x, int y)
    {
        return ScenePosition.FromGrid(new GridPosition(x, y));
    }

    private static string Hash(IEnumerable<string> lines)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    }

    private static void SetNpcForVisibleWalk(TinyFarmState state)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Elias);
        state.MutableActorScenes[placementIndex] = new ActorSceneState(
            TinyFarmIds.Elias,
            TinyFarmSceneIds.Farm,
            At(9, 7),
            ActorFacing.Left);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == TinyFarmIds.Elias);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = TinyFarmIds.Farmhouse };
    }
}
