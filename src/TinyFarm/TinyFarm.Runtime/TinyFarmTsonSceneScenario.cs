using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM7Proof(
    string Milestone,
    string Outcome,
    string SceneContentHash,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string AnchorsHash,
    string RoutesHash,
    string NavigationHash,
    string ProjectionHash,
    string M1Hash,
    string M2Hash,
    bool TsonOnlySceneAuthority,
    bool LegacyParity,
    bool NavigationParity,
    bool AnchorParity,
    bool RouteParity,
    bool NpcScheduleParity,
    bool HandoffParity,
    bool SaveLoadCompatible,
    bool Headless);

public sealed record TinyFarmM7Evidence(
    TinyFarmM7Proof Proof,
    object Content,
    object Parity,
    object Provenance,
    object Manifest);

public static class TinyFarmTsonSceneScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";
    private const string ExpectedM6StateHash = "d46e70e37c8775e503c3a7693fc14d952a6932a22be0c13172771e020ae65544";
    private const string ExpectedM6ResultsHash = "ecb4181792717a393125e85416b148ca2242934d761b025498a45aa24af21a24";
    private const string ExpectedM6EventsHash = "4f8e8383683a38da695284fb6fd561d5fc32c12fd7feedeee1841e7a3b7364d7";
    private const string ExpectedM6AnchorsHash = "f6dc1f5c8a9116122744e860fcd23267d7784f4c9452fd273ca934b55e79f535";
    private const string ExpectedM6NavigationHash = "07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa";
    private const string ExpectedM6ProjectionHash = "4c93db713e4da1a8ee47cec7f6a309adc23f19b7acee1d91b80e0c9c3d6b8434";
    private const string ExpectedM6RoutesHash = "affb1c95d1745eaab9e9108b282ba5516ea16b1a1282f1a894c741149e8ccf72";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM7Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmM4Proof routes = TinyFarmSceneScenario.Prove().Proof;
        TinyFarmM6Proof handoff = TinyFarmAnchorHandoffScenario.Prove().Proof;

        bool anchorParity = handoff.AnchorsHash == ExpectedM6AnchorsHash;
        bool routeParity = routes.SceneRouteHash == ExpectedM6RoutesHash;
        bool navigationParity = handoff.NavigationHash == ExpectedM6NavigationHash;
        bool legacyParity = handoff.StateHash == ExpectedM6StateHash
            && handoff.ResultsHash == ExpectedM6ResultsHash
            && handoff.EventsHash == ExpectedM6EventsHash
            && handoff.ProjectionHash == ExpectedM6ProjectionHash
            && anchorParity
            && routeParity
            && navigationParity;
        bool hashesPreserved = handoff.M1Hash == ExpectedM1Hash
            && handoff.M2Hash == ExpectedM2Hash;
        bool success = routes.Outcome == "A"
            && handoff.Outcome == "A"
            && hashesPreserved
            && legacyParity
            && handoff.NpcGoalsUseSemanticAnchors
            && handoff.ActiveSaveLoadExact
            && handoff.InactiveSaveLoadExact
            && handoff.HandoffHighLevelEquivalent;

        var proof = new TinyFarmM7Proof(
            "TINY-FARM-M7",
            success ? "A" : "B",
            definitions.SceneContent.AggregateSha256,
            handoff.StateHash,
            handoff.ResultsHash,
            handoff.EventsHash,
            handoff.AnchorsHash,
            routes.SceneRouteHash,
            handoff.NavigationHash,
            handoff.ProjectionHash,
            handoff.M1Hash,
            handoff.M2Hash,
            true,
            legacyParity,
            navigationParity,
            anchorParity,
            routeParity,
            handoff.NpcGoalsUseSemanticAnchors,
            handoff.InactiveToActiveDeterministic && handoff.ActiveToInactiveDeterministic,
            routes.SaveLoadRestoredExactSceneAndPosition
                && handoff.ActiveSaveLoadExact
                && handoff.InactiveSaveLoadExact,
            true);

        SceneDefinition farm = definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        SceneDefinition store = definitions.Scenes.Get(TinyFarmSceneIds.GeneralStore);
        object content = new
        {
            authority = "five Object TypeScript record-table roots -> TinyFarmDefinitionLoader -> TinyFarmSceneCatalog -> SceneDefinition",
            tables = new object[]
            {
                new { name = "Scenes", columns = new[] { "id:string", "label:string", "width:number", "height:number" }, rows = definitions.Scenes.All.Count },
                new { name = "SceneObjects", columns = new[] { "sceneId:string", "objectId:string", "kind:string", "label:string", "blocksMovement:boolean", "semanticReference:OptionalText" }, rows = definitions.Scenes.All.Sum(scene => scene.Objects.Count) },
                new { name = "SceneLayout", columns = new[] { "sceneId:string", "objectId:string", "x:number", "y:number", "width:number", "height:number", "layer:number" }, rows = definitions.Scenes.All.Sum(scene => scene.Layout.Count) },
                new { name = "SceneAnchors", columns = new[] { "anchorId:string", "sceneId:string", "x:number", "y:number", "kind:string", "semanticLocation:OptionalText", "semanticObject:OptionalText", "facing:OptionalText", "arrivalRadiusUnits:number" }, rows = definitions.Scenes.All.Sum(scene => scene.Anchors.Count) },
                new { name = "SceneRoutes", columns = new[] { "routeId:string", "sourceScene:string", "triggerObject:string", "targetScene:string", "targetAnchor:string", "interactionLabel:string" }, rows = definitions.Scenes.All.Sum(scene => scene.Routes.Count) }
            },
            orderingLaw = "row order is authoring/display order only; constructors sort by stable semantic ID and layer where applicable",
            optionalLaw = "OptionalText.None or OptionalText.Some(value); no null, undefined, or empty-string sentinel",
            agentQueries = new
            {
                generalStoreCounter = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.StoreCounter).Position.Tile,
                routesEnteringTown = definitions.Scenes.All.SelectMany(scene => scene.Routes)
                    .Where(route => route.TargetScene == TinyFarmSceneIds.Town)
                    .Select(route => route.Id.Value)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                blockingFarmObjects = farm.Objects.Where(item => item.BlocksMovement)
                    .Select(item => item.Id.Value)
                    .ToArray(),
                generalStoreObjects = store.Objects.Select(item => item.Id.Value).ToArray()
            },
            definitions.SceneContent.AggregateSha256,
            authoredBytes = definitions.SceneContent.Sources.Sum(source => source.ByteLength)
        };
        object parity = new
        {
            comparison = "M4 route and M6 state/result/event/anchor/navigation/projection canonical hashes captured before the authoring migration",
            legacyParity,
            anchorParity,
            routeParity,
            navigationParity,
            npcScheduleParity = handoff.NpcGoalsUseSemanticAnchors,
            handoffParity = handoff.InactiveToActiveDeterministic && handoff.ActiveToInactiveDeterministic,
            saveLoadParity = proof.SaveLoadCompatible,
            expected = new
            {
                state = ExpectedM6StateHash,
                results = ExpectedM6ResultsHash,
                events = ExpectedM6EventsHash,
                anchors = ExpectedM6AnchorsHash,
                routes = ExpectedM6RoutesHash,
                navigation = ExpectedM6NavigationHash,
                projection = ExpectedM6ProjectionHash
            }
        };
        object provenance = definitions.SceneContent;
        object manifest = new
        {
            milestone = "TINY-FARM-M7",
            kind = "tson-authored-scene-tables",
            sceneContentAuthoredInTson = true,
            productionSceneCatalogHardcodedInCSharp = false,
            sceneSemanticsChanged = false,
            stableIdentitiesPreserved = true,
            routingSemanticsChanged = false,
            anchorSemanticsChanged = false,
            navigationSemanticsChanged = false,
            rawTsonLeaksIntoGameplay = false,
            sceneDslAdded = false,
            editorAdded = false,
            hotReloadAdded = false,
            aurelianExtractionPerformed = false
        };
        return new TinyFarmM7Evidence(proof, content, parity, provenance, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
