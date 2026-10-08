using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmRouteReductionEvidence(
    SceneRouteId Route,
    SceneId BeforeScene,
    GridPosition BeforePosition,
    SceneId AfterScene,
    GridPosition AfterPosition);

public sealed record TinyFarmM4Proof(
    string Milestone,
    string Outcome,
    string FinalStateHash,
    string IntentResultHash,
    string EventHash,
    string SceneRouteHash,
    string ProjectionHash,
    string M1Hash,
    string M2Hash,
    bool M1HashPreserved,
    bool M2HashPreserved,
    bool SaveLoadRestoredExactSceneAndPosition,
    bool SeedPurchased,
    bool SeedPlanted,
    bool NpcCrossedScene,
    bool RendererOwnsGameplayState,
    int SceneCount,
    int ObjectRows,
    int LayoutRows,
    int RouteRows,
    IReadOnlyList<TinyFarmRouteReductionEvidence> RouteReductions);

public sealed record TinyFarmM4Evidence(TinyFarmM4Proof Proof, TinyFarmFrame FinalProjection);

public static class TinyFarmSceneScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM4Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        var session = new TinyFarmSession(TinyFarmContent.CreateSceneState(definitions), definitions);
        var results = new List<string>();
        var events = new List<string>();
        var reductions = new List<TinyFarmRouteReductionEvidence>();
        var npcScenes = session.State.ActorScenes
            .Where(item => item.Actor != TinyFarmIds.Player)
            .ToDictionary(item => item.Actor, item => item.Scene);

        Apply(session, new WaitIntent(240), results, events, reductions);
        Move(session, 0, 1, 1, results, events, reductions);
        Move(session, 1, 0, 11, results, events, reductions);
        Move(session, 0, -1, 1, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 1, 0, 8, results, events, reductions);
        Move(session, 0, -1, 2, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 1, 0, 6, results, events, reductions);
        Move(session, 0, -1, 8, results, events, reductions);

        ActorSceneState savedPlacement = session.State.ActorScene(TinyFarmIds.Player);
        string savedHash = TinyFarmSemanticHash.Compute(session.State);
        byte[] save = session.CaptureWeekSave();
        Move(session, 1, 0, 1, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        session = TinyFarmChunkedSaveCodec.Read(save, definitions);
        bool saveRestored = session.State.ActorScene(TinyFarmIds.Player) == savedPlacement
            && TinyFarmSemanticHash.Compute(session.State) == savedHash;

        Move(session, 1, 0, 1, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 0, -1, 2, results, events, reductions);
        Apply(session, new BuyProductIntent(TinyFarmIds.TurnipSeed), results, events, reductions);
        Move(session, 0, 1, 3, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 0, 1, 8, results, events, reductions);
        Move(session, -1, 0, 6, results, events, reductions);
        Move(session, 0, 1, 1, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 0, 1, 2, results, events, reductions);
        Move(session, -1, 0, 8, results, events, reductions);
        Apply(session, new InteractIntent(), results, events, reductions);
        Move(session, 0, 1, 1, results, events, reductions);
        Move(session, -1, 0, 9, results, events, reductions);
        Move(session, 0, -1, 1, results, events, reductions);
        Apply(session, new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop), results, events, reductions);

        TinyFarmFrame projection = TinyFarmFrameProjector.Project(session.State, definitions);
        string projectionHash = TinyFarmFrameProjector.ComputeHash(projection);
        string repeatProjectionHash = TinyFarmFrameProjector.ComputeHash(
            TinyFarmFrameProjector.Project(session.State.DeepCopy(), definitions));
        string m1Hash = TinyFarmCanonicalScenario.Prove().FinalHash;
        string m2Hash = TinyFarmWeekScenario.Prove().FinalHash;
        bool seedPurchased = events.Any(item => item.Contains("ItemBought", StringComparison.Ordinal));
        bool seedPlanted = session.State.FarmPlots.Single(item => item.Id == TinyFarmIds.PlotOne).Crop == TinyFarmIds.TurnipCrop;
        bool npcCrossedScene = session.State.ActorScenes
            .Where(item => item.Actor != TinyFarmIds.Player)
            .Any(item => npcScenes[item.Actor] != item.Scene);
        bool success = m1Hash == ExpectedM1Hash
            && m2Hash == ExpectedM2Hash
            && saveRestored
            && seedPurchased
            && seedPlanted
            && npcCrossedScene
            && projectionHash == repeatProjectionHash
            && reductions.Select(item => item.Route.Value).Contains("overworld-town", StringComparer.Ordinal)
            && reductions.Select(item => item.Route.Value).Contains("town-store", StringComparer.Ordinal)
            && reductions.Select(item => item.Route.Value).Contains("store-town", StringComparer.Ordinal)
            && reductions.Select(item => item.Route.Value).Contains("town-overworld", StringComparer.Ordinal);

        SceneRoute[] routes = definitions.Scenes.All.SelectMany(scene => scene.Routes).ToArray();
        var proof = new TinyFarmM4Proof(
            "TINY-FARM-M4",
            success ? "A" : "B",
            TinyFarmSemanticHash.Compute(session.State),
            HashLines(results),
            HashLines(events),
            HashLines(routes.Select(RouteSignature)),
            projectionHash,
            m1Hash,
            m2Hash,
            m1Hash == ExpectedM1Hash,
            m2Hash == ExpectedM2Hash,
            saveRestored,
            seedPurchased,
            seedPlanted,
            npcCrossedScene,
            false,
            definitions.Scenes.All.Count,
            definitions.Scenes.All.Sum(scene => scene.Objects.Count),
            definitions.Scenes.All.Sum(scene => scene.Layout.Count),
            routes.Length,
            reductions);
        return new TinyFarmM4Evidence(proof, projection);
    }

    public static string WriteProofJson(TinyFarmM4Proof proof)
    {
        return JsonSerializer.Serialize(proof, JsonOptions);
    }

    public static string WriteScenesJson()
    {
        TinyFarmSceneCatalog catalog = TinyFarmDefinitionLoader.Load().Scenes;
        object[] scenes = catalog.All.Select(scene => new
        {
            id = scene.Id.Value,
            scene.Name,
            scene.Width,
            scene.Height,
            objects = scene.Objects.Select(item => new
            {
                id = item.Id.Value,
                kind = item.Kind.ToString(),
                item.Label,
                item.BlocksMovement,
                item.SemanticReference
            }),
            layout = scene.Layout.Select(item => new
            {
                objectId = item.ObjectId.Value,
                item.X,
                item.Y,
                item.Width,
                item.Height,
                item.Layer
            }),
            anchors = scene.Anchors.Select(item => new
            {
                id = item.Id.Value,
                scene = item.Scene.Value,
                xUnits = item.Position.XUnits,
                yUnits = item.Position.YUnits,
                kind = item.Kind.ToString(),
                semanticLocation = item.SemanticLocation?.Value,
                semanticObject = item.SemanticObject?.Value,
                facing = item.Facing?.ToString(),
                item.ArrivalRadiusUnits
            })
        }).ToArray();
        return JsonSerializer.Serialize(scenes, JsonOptions);
    }

    public static string WriteRoutesJson()
    {
        TinyFarmSceneCatalog catalog = TinyFarmDefinitionLoader.Load().Scenes;
        object[] routes = catalog.All
            .SelectMany(scene => scene.Routes)
            .OrderBy(route => route.Id.Value, StringComparer.Ordinal)
            .Select(route => new
            {
                id = route.Id.Value,
                sourceScene = route.SourceScene.Value,
                triggerObject = route.TriggerObject.Value,
                targetScene = route.TargetScene.Value,
                targetAnchor = route.TargetAnchor.Value,
                route.InteractionLabel
            })
            .ToArray();
        return JsonSerializer.Serialize(routes, JsonOptions);
    }

    private static void Move(
        TinyFarmSession session,
        int deltaX,
        int deltaY,
        int count,
        List<string> results,
        List<string> events,
        List<TinyFarmRouteReductionEvidence> reductions)
    {
        for (int index = 0; index < count; index++)
        {
            Apply(session, new SpatialMoveIntent(deltaX, deltaY), results, events, reductions);
        }
    }

    private static void Apply(
        TinyFarmSession session,
        GameIntent intent,
        List<string> results,
        List<string> events,
        List<TinyFarmRouteReductionEvidence> reductions)
    {
        ActorSceneState before = session.State.ActorScene(TinyFarmIds.Player);
        TinyFarmStepResult step = session.Step(intent);
        IntentResult human = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
        if (human.Status != IntentResultStatus.Accepted)
        {
            throw new InvalidOperationException($"M4 canonical intent '{intent}' failed: {human.Status}/{human.Reason}.");
        }

        results.AddRange(step.Results.Select(ResultSignature));
        events.AddRange(step.Results.SelectMany(result => result.Events).Select(EventSignature));
        GameEvent? entered = human.Events.SingleOrDefault(item => item.Kind == GameEventKind.SceneEntered);
        if (entered?.Route is SceneRouteId route)
        {
            ActorSceneState after = session.State.ActorScene(TinyFarmIds.Player);
            reductions.Add(new TinyFarmRouteReductionEvidence(
                route,
                before.Scene,
                before.Position,
                after.Scene,
                after.Position));
        }
    }

    private static string HashLines(IEnumerable<string> lines)
    {
        string value = string.Join('\n', lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string RouteSignature(SceneRoute route)
    {
        return $"{route.Id}|{route.SourceScene}|{route.TriggerObject}|{route.TargetScene}|{route.TargetAnchor}";
    }

    private static string ResultSignature(IntentResult result)
    {
        return $"{result.Envelope.Sequence}|{result.Envelope.Actor}|{result.Envelope.Source}|{result.Envelope.Intent}|{result.Status}|{result.Reason}";
    }

    private static string EventSignature(GameEvent item)
    {
        return $"{item.Kind}:{item.Actor}:{item.Target}:{item.Item}:{item.Product}:{item.Crop}:{item.Plot}:{item.Location}:{item.Scene}:{item.Route}:{item.Amount}:{item.Day}";
    }
}
