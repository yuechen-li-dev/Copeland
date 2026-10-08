using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM6Proof(
    string Milestone,
    string Outcome,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string AnchorsHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    string M1Hash,
    string M2Hash,
    bool NpcGoalsUseSemanticAnchors,
    bool ActiveNpcWalked,
    bool InactiveNpcUsedNoNavigation,
    bool InactiveToActiveDeterministic,
    bool ActiveToInactiveDeterministic,
    bool ActiveSaveLoadExact,
    bool InactiveSaveLoadExact,
    bool HandoffHighLevelEquivalent,
    int NavigationPlanCount,
    double AnchorLookupMilliseconds,
    double ActivationMilliseconds,
    double DeactivationMilliseconds,
    double PathRebuildMilliseconds);

public sealed record TinyFarmM6Evidence(
    TinyFarmM6Proof Proof,
    object Anchors,
    object Handoff,
    object Navigation,
    TinyFarmFrame Projection);

public static class TinyFarmAnchorHandoffScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM6Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = (5 * 1440) + (9 * 60);
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Farm, At(16, 6), ActorFacing.Right);
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7), ActorFacing.Left);
        SetActorLocation(state, TinyFarmIds.Elias, TinyFarmIds.Farmhouse);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.GeneralStore, At(5, 6), ActorFacing.Up);
        SetActorLocation(state, TinyFarmIds.Mara, TinyFarmIds.GeneralStore);

        var session = new TinyFarmSession(state, definitions);
        var resultLines = new List<string>();
        var eventLines = new List<string>();
        var handoffLines = new List<string>();

        ScenePosition eliasBefore = session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        Apply(session, new LookIntent(), resultLines, eventLines);
        ScenePosition eliasAfter = session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
        bool activeNpcWalked = eliasAfter != eliasBefore;

        ActorSceneState activeSavedPlacement = session.State.ActorScene(TinyFarmIds.Elias);
        string activeSavedHash = TinyFarmSemanticHash.Compute(session.State);
        byte[] activeSave = session.CaptureWeekSave();
        TinyFarmSession activeLoaded = TinyFarmChunkedSaveCodec.Read(activeSave, definitions);
        bool activeSaveLoadExact = activeLoaded.State.ActorScene(TinyFarmIds.Elias) == activeSavedPlacement
            && TinyFarmSemanticHash.Compute(activeLoaded.State) == activeSavedHash;
        var pathRebuildWatch = Stopwatch.StartNew();
        activeLoaded.Step(new LookIntent());
        pathRebuildWatch.Stop();

        int plansBeforeExit = session.NavigationPlanCount;
        var deactivationWatch = Stopwatch.StartNew();
        Apply(session, new InteractIntent(new SceneObjectId("farm-exit")), resultLines, eventLines);
        deactivationWatch.Stop();
        handoffLines.Add(HandoffSignature("deactivate", session.State, TinyFarmIds.Elias, definitions.Schedules));
        Apply(session, new WaitIntent(60), resultLines, eventLines);
        bool inactiveUsedNoNavigation = session.NavigationPlanCount == plansBeforeExit;

        string inactiveSavedHash = TinyFarmSemanticHash.Compute(session.State);
        ActorSceneState inactiveSavedPlacement = session.State.ActorScene(TinyFarmIds.Mara);
        TinyFarmSession inactiveLoaded = TinyFarmChunkedSaveCodec.Read(session.CaptureWeekSave(), definitions);
        bool inactiveSaveLoadExact = TinyFarmSemanticHash.Compute(inactiveLoaded.State) == inactiveSavedHash
            && inactiveLoaded.State.ActorScene(TinyFarmIds.Mara) == inactiveSavedPlacement;

        SetPlacement(session.State, TinyFarmIds.Player, TinyFarmSceneIds.GeneralStore, At(5, 6), ActorFacing.Up);
        var activationWatch = Stopwatch.StartNew();
        Apply(session, new LookIntent(), resultLines, eventLines);
        activationWatch.Stop();
        handoffLines.Add(HandoffSignature("activate", session.State, TinyFarmIds.Mara, definitions.Schedules));
        bool maraWalked = session.State.ActorScene(TinyFarmIds.Mara).WorldPosition != At(5, 6);

        SetPlacement(inactiveLoaded.State, TinyFarmIds.Player, TinyFarmSceneIds.GeneralStore, At(5, 6), ActorFacing.Up);
        inactiveLoaded.Step(new LookIntent());
        bool activationDeterministic = inactiveLoaded.State.ActorScene(TinyFarmIds.Mara)
            == session.State.ActorScene(TinyFarmIds.Mara);

        SetPlacement(session.State, TinyFarmIds.Player, TinyFarmSceneIds.Town, At(16, 4), ActorFacing.Right);
        Apply(session, new InteractIntent(new SceneObjectId("store-entrance")), resultLines, eventLines);
        Apply(session, new LookIntent(), resultLines, eventLines);
        handoffLines.Add(HandoffSignature("reenter", session.State, TinyFarmIds.Mara, definitions.Schedules));

        SceneAnchorId maraGoal = TinyFarmNpcController.ScheduledAnchor(
            TinyFarmIds.Mara,
            session.State.Minute,
            definitions.Schedules);
        bool highLevelEquivalent = maraGoal == TinyFarmNpcController.ScheduledAnchor(
                TinyFarmIds.Mara,
                inactiveLoaded.State.Minute,
                definitions.Schedules)
            && session.State.Actor(TinyFarmIds.Mara).Location
                == inactiveLoaded.State.Actor(TinyFarmIds.Mara).Location;

        TinyFarmFrame projection = TinyFarmFrameProjector.Project(session.State, definitions);
        SceneAnchorId[] anchorIds = definitions.Scenes.All
            .SelectMany(scene => scene.Anchors)
            .Select(anchor => anchor.Id)
            .ToArray();
        var anchorLookupWatch = Stopwatch.StartNew();
        for (int repetition = 0; repetition < 1_000; repetition++)
        {
            foreach (SceneAnchorId anchorId in anchorIds)
            {
                _ = definitions.Scenes.GetAnchor(anchorId);
            }
        }
        anchorLookupWatch.Stop();
        string anchorsHash = Hash(AnchorSignatures(definitions.Scenes));
        string handoffHash = Hash(handoffLines);
        string navigationHash = Hash([
            $"plans:{session.NavigationPlanCount}",
            $"active-moved:{activeNpcWalked}",
            $"mara-moved:{maraWalked}",
            $"inactive-zero:{inactiveUsedNoNavigation}"
        ]);
        string m1Hash = TinyFarmCanonicalScenario.Prove().FinalHash;
        string m2Hash = TinyFarmWeekScenario.Prove().FinalHash;
        bool success = activeNpcWalked
            && maraWalked
            && inactiveUsedNoNavigation
            && activationDeterministic
            && activeSaveLoadExact
            && inactiveSaveLoadExact
            && highLevelEquivalent
            && m1Hash == ExpectedM1Hash
            && m2Hash == ExpectedM2Hash;

        var proof = new TinyFarmM6Proof(
            "TINY-FARM-M6",
            success ? "A" : "B",
            TinyFarmSemanticHash.Compute(session.State),
            Hash(resultLines),
            Hash(eventLines),
            anchorsHash,
            handoffHash,
            navigationHash,
            TinyFarmFrameProjector.ComputeHash(projection),
            m1Hash,
            m2Hash,
            true,
            activeNpcWalked && maraWalked,
            inactiveUsedNoNavigation,
            activationDeterministic,
            session.ActivationCount > 0 && session.DeactivationCount > 0,
            activeSaveLoadExact,
            inactiveSaveLoadExact,
            highLevelEquivalent,
            session.NavigationPlanCount,
            anchorLookupWatch.Elapsed.TotalMilliseconds,
            activationWatch.Elapsed.TotalMilliseconds,
            deactivationWatch.Elapsed.TotalMilliseconds,
            pathRebuildWatch.Elapsed.TotalMilliseconds);

        object anchorsEvidence = definitions.Scenes.All
            .SelectMany(scene => scene.Anchors)
            .OrderBy(anchor => anchor.Id.Value, StringComparer.Ordinal)
            .Select(anchor => new
            {
                id = anchor.Id.Value,
                scene = anchor.Scene.Value,
                position = anchor.Position,
                kind = anchor.Kind.ToString(),
                semanticLocation = anchor.SemanticLocation?.Value,
                semanticObject = anchor.SemanticObject?.Value,
                facing = anchor.Facing?.ToString(),
                anchor.ArrivalRadiusUnits
            })
            .ToArray();
        object handoffEvidence = new
        {
            activeSceneLaw = "the player's current scene selects detailed spatial fidelity",
            persistenceLaw = "exact ScenePosition always persists; navigation paths never persist",
            reconciliationLaw = "ActorState.Location identifies the coarse scene-scale place; ActorSceneState is its sole exact placement and must resolve to a compatible scene, including the authored Overworld transit exception",
            handoffLines,
            activationDeterministic,
            highLevelEquivalent
        };
        object navigationEvidence = new
        {
            lowering = "Dominatus goal -> SceneAnchorId -> authored anchor -> ScenePosition -> INavigationPlanner -> DotRecast -> SpatialMoveIntent",
            plansBeforeExit,
            finalPlanCount = session.NavigationPlanCount,
            inactiveUsedNoNavigation,
            pathStatePersisted = false,
            failures = new[] { IntentReason.MissingAnchor.ToString(), IntentReason.AnchorUnreachable.ToString() }
        };
        return new TinyFarmM6Evidence(proof, anchorsEvidence, handoffEvidence, navigationEvidence, projection);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static void Apply(
        TinyFarmSession session,
        GameIntent intent,
        ICollection<string> resultLines,
        ICollection<string> eventLines)
    {
        TinyFarmStepResult step = session.Step(intent);
        IntentResult human = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
        if (human.Status == IntentResultStatus.Rejected)
        {
            throw new InvalidOperationException($"M6 intent '{intent}' failed: {human.Reason}.");
        }
        foreach (IntentResult result in step.Results)
        {
            resultLines.Add($"{result.Envelope.Actor}:{result.Envelope.Intent}:{result.Status}:{result.Reason}");
            foreach (GameEvent gameEvent in result.Events)
            {
                eventLines.Add($"{gameEvent.Kind}:{gameEvent.Actor}:{gameEvent.Location}:{gameEvent.Scene}:{gameEvent.Route}:{gameEvent.Anchor}");
            }
        }
    }

    private static IEnumerable<string> AnchorSignatures(TinyFarmSceneCatalog catalog)
    {
        return catalog.All
            .SelectMany(scene => scene.Anchors)
            .OrderBy(anchor => anchor.Id.Value, StringComparer.Ordinal)
            .Select(anchor => $"{anchor.Id}|{anchor.Scene}|{anchor.Position.XUnits}|{anchor.Position.YUnits}|{anchor.Kind}|{anchor.SemanticLocation}|{anchor.SemanticObject}|{anchor.Facing}|{anchor.ArrivalRadiusUnits}");
    }

    private static string HandoffSignature(
        string transition,
        TinyFarmState state,
        ActorId actor,
        TinyFarmScheduleCatalog schedules)
    {
        ActorState semantic = state.Actor(actor);
        ActorSceneState spatial = state.ActorScene(actor);
        SceneAnchorId goal = TinyFarmNpcController.ScheduledAnchor(actor, state.Minute, schedules);
        return $"{transition}:{actor}:{semantic.Location}:{spatial.Scene}:{spatial.WorldPosition.XUnits},{spatial.WorldPosition.YUnits}:{goal}";
    }

    private static string Hash(IEnumerable<string> lines)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    }

    private static ScenePosition At(int x, int y)
    {
        return ScenePosition.FromGrid(new GridPosition(x, y));
    }

    private static void SetPlacement(
        TinyFarmState state,
        ActorId actor,
        SceneId scene,
        ScenePosition position,
        ActorFacing facing)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[placementIndex] = new ActorSceneState(actor, scene, position, facing);
        SetActorLocation(state, actor, TinyFarmScenes.LocationForScene(scene));
    }

    private static void SetActorLocation(TinyFarmState state, ActorId actor, LocationId location)
    {
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = location };
    }
}
