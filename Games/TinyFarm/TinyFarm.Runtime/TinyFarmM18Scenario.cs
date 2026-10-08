using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM18Evidence(
    object Proof,
    object Forage,
    object Inventory,
    object Replay,
    object Manifest);

public static class TinyFarmM18ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM17ControlStates.Create(definitions);
        ForageNodeDefinition node = definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods);
        ActorState[] actors = baseline.Actors
            .Select(actor => actor.Id == TinyFarmIds.Player
                ? actor with { Location = TinyFarmIds.Riverside, Inventory = actor.Inventory.ToList() }
                : actor with { Inventory = actor.Inventory.ToList() })
            .ToArray();
        ActorSceneState[] placements = baseline.ActorScenes
            .Select(placement => placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = node.Scene,
                    WorldPosition = new ScenePosition(
                        node.Position.XUnits - ScenePosition.UnitsPerTile,
                        node.Position.YUnits),
                    Facing = ActorFacing.Right
                }
                : placement)
            .ToArray();
        ForageNodeState[] forageNodes = definitions.ForageNodes
            .Select(definition => new ForageNodeState(
                definition.Id,
                ForageNodeAvailability.Available))
            .ToArray();

        return new TinyFarmState(
            TinyFarmState.ForageSaveVersion,
            baseline.Minute,
            actors,
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            baseline.SelectedHotbarSlot,
            forageNodes);
    }
}

public static class TinyFarmM18Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM18Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM18();
        RunResult canonical = Run(definitions, IntentSourceKind.Human);
        RunResult repeat = Run(definitions, IntentSourceKind.Human);
        RunResult replay = Run(definitions, IntentSourceKind.Replay);

        bool replayExact = canonical.StateHash == replay.StateHash
            && canonical.ResultsHash == replay.ResultsHash
            && canonical.EventsHash == replay.EventsHash;
        bool repeatExact = canonical.StateHash == repeat.StateHash
            && canonical.ResultsHash == repeat.ResultsHash
            && canonical.EventsHash == repeat.EventsHash;
        bool success = canonical.First.Status == IntentResultStatus.Accepted
            && canonical.First.Events.Single().Kind == GameEventKind.ForageGathered
            && canonical.Second.Reason == IntentReason.AlreadyDepleted
            && canonical.Final.ForageNode(TinyFarmIds.RiversideHenOfTheWoods).Availability
                == ForageNodeAvailability.Depleted
            && canonical.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods) == 1
            && canonical.Final.SelectedHotbarSlot == canonical.InitialSelectedSlot
            && canonical.AfterFrame.SceneObjects!.All(sceneObject => sceneObject.Kind != SceneObjectKind.Forage)
            && canonical.AfterUi.Inventory.Any(item =>
                item.SemanticId == TinyFarmIds.HenOfTheWoods.Value && item.Count == 1)
            && canonical.StateHash == canonical.LoadedHash
            && repeatExact
            && replayExact;

        object hashes = new
        {
            state = canonical.StateHash,
            results = canonical.ResultsHash,
            events = canonical.EventsHash,
            forage = canonical.ForageHash,
            inventory = canonical.InventoryHash,
            definitions = canonical.DefinitionsHash,
            projection = canonical.ProjectionHash,
            dto = canonical.DtoHash,
            replay = replay.StateHash
        };
        object proof = new
        {
            milestone = "TINY-FARM-M18",
            outcome = success ? "A" : "B",
            forageUsesResolver = true,
            stackableProductProducedFromWorld = true,
            nodeDepletionAuthoritative = true,
            saveLoadExact = canonical.StateHash == canonical.LoadedHash,
            headlessRepeatExact = repeatExact,
            replayExact,
            hashes
        };
        object forage = new
        {
            node = TinyFarmIds.RiversideHenOfTheWoods.Value,
            scene = TinyFarmSceneIds.Riverside.Value,
            product = TinyFarmIds.HenOfTheWoods.Value,
            yieldCount = 1,
            lowering = $"{nameof(InteractIntent)} -> {nameof(GatherIntent)}",
            priority = "actor, portal, ground item, forage node, plot, shop; then squared distance; then ordinal stable ID",
            first = canonical.First.Status,
            second = canonical.Second.Reason,
            finalState = canonical.Final.ForageNode(TinyFarmIds.RiversideHenOfTheWoods).Availability
        };
        object inventory = new
        {
            product = TinyFarmIds.HenOfTheWoods.Value,
            count = canonical.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods),
            canonical.AfterUi.Inventory,
            selectedHotbarSlotBefore = canonical.InitialSelectedSlot,
            selectedHotbarSlotAfter = canonical.Final.SelectedHotbarSlot
        };
        object replayEvidence = new
        {
            humanState = canonical.StateHash,
            replayState = replay.StateHash,
            humanResults = canonical.ResultsHash,
            replayResults = replay.ResultsHash,
            humanEvents = canonical.EventsHash,
            replayEvents = replay.EventsHash,
            exact = replayExact
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M18",
            kind = "hen-of-the-woods-foraging-stackable-product",
            forageProduct = TinyFarmIds.HenOfTheWoods.Value,
            forageUsesResolver = true,
            stackableProductProducedFromWorld = true,
            nodeDepletionAuthoritative = true,
            inventoryAuthorityMoved = false,
            rendererMutatesForage = false,
            randomSpawningAdded = false,
            resourceFrameworkAdded = false,
            craftingAdded = false,
            toolSystemAdded = false,
            skillSystemAdded = false
        };
        return new TinyFarmM18Evidence(proof, forage, inventory, replayEvidence, manifest);
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM18Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "forage.json"), evidence.Forage);
        Write(Path.Combine(directory, "inventory.json"), evidence.Inventory);
        Write(Path.Combine(directory, "replay.json"), evidence.Replay);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static RunResult Run(TinyFarmDefinitions definitions, IntentSourceKind source)
    {
        TinyFarmState initial = TinyFarmM18ControlStates.Create(definitions);
        int initialSelectedSlot = initial.SelectedHotbarSlot;
        TinyFarmFrame beforeFrame = TinyFarmFrameProjector.Project(initial, definitions);
        var resolver = new TinyFarmResolver(definitions);
        IntentEnvelope firstEnvelope = new(
            TinyFarmIds.Player,
            new InteractIntent(),
            initial.Minute,
            0,
            source);
        ResolutionBatchResult firstBatch = resolver.Resolve(initial, [firstEnvelope]);
        IntentResult first = firstBatch.Results.Single();
        IntentEnvelope secondEnvelope = new(
            TinyFarmIds.Player,
            new GatherIntent(TinyFarmIds.RiversideHenOfTheWoods),
            firstBatch.State.Minute,
            1,
            source);
        ResolutionBatchResult secondBatch = resolver.Resolve(firstBatch.State, [secondEnvelope]);
        IntentResult second = secondBatch.Results.Single();
        TinyFarmState final = secondBatch.State;
        TinyFarmFrame afterFrame = TinyFarmFrameProjector.Project(final, definitions);
        TinyFarmPlayerUiView afterUi = TinyFarmPlayerUiProjector.Project(final, definitions);
        var session = new TinyFarmSession(final, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, definitions),
            definitions);
        var host = new TinyFarmSimulationHost(session, definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        IntentResult[] results = [first, second];
        GameEvent[] events = results.SelectMany(result => result.Events).ToArray();

        return new RunResult(
            first,
            second,
            final,
            beforeFrame,
            afterFrame,
            afterUi,
            initialSelectedSlot,
            TinyFarmSemanticHash.Compute(final),
            TinyFarmSemanticHash.Compute(loaded.State),
            Hash(results.Select(result => new
            {
                intent = result.Envelope.Intent.GetType().Name,
                result.Status,
                result.Reason
            })),
            Hash(events),
            Hash(final.ForageNodes),
            Hash(final.InventoryStacks),
            Hash(new
            {
                definitions.Identity,
                definitions.Items,
                definitions.ForageNodes
            }),
            TinyFarmFrameProjector.ComputeHash(afterFrame),
            TinyFarmSimulationSnapshotProjector.ComputeTsonHash(snapshot));
    }

    private static string Hash(object value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static void Write(string path, object value)
    {
        File.WriteAllText(path, WriteJson(value) + Environment.NewLine);
    }

    private sealed record RunResult(
        IntentResult First,
        IntentResult Second,
        TinyFarmState Final,
        TinyFarmFrame BeforeFrame,
        TinyFarmFrame AfterFrame,
        TinyFarmPlayerUiView AfterUi,
        int InitialSelectedSlot,
        string StateHash,
        string LoadedHash,
        string ResultsHash,
        string EventsHash,
        string ForageHash,
        string InventoryHash,
        string DefinitionsHash,
        string ProjectionHash,
        string DtoHash);
}
