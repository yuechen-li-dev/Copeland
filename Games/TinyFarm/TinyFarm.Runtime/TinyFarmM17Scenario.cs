using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM17Evidence(
    object Proof,
    object Pickup,
    object UseSelected,
    object UiProjection,
    object Manifest);

public static class TinyFarmM17ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM16ControlStates.Create(definitions);
        ItemState[] items = baseline.Items
            .Select(item => item.Id == TinyFarmIds.WildMint
                ? item with
                {
                    GroundLocation = TinyFarmIds.Farmhouse,
                    Owner = null,
                    GroundScene = TinyFarmSceneIds.Farm,
                    GroundPosition = new ScenePosition(
                        7 * ScenePosition.UnitsPerTile,
                        5 * ScenePosition.UnitsPerTile + ScenePosition.UnitsPerTile / 2)
                }
                : item)
            .ToArray();
        ActorSceneState[] placements = baseline.ActorScenes
            .Select(placement => placement.Actor == TinyFarmIds.Player
                ? placement with { Facing = ActorFacing.Right }
                : placement)
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.ItemActionSaveVersion,
            baseline.Minute,
            baseline.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1);
    }
}

public static class TinyFarmM17Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM17Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        RunResult canonical = Run(definitions);
        RunResult repeat = Run(definitions);

        bool success = canonical.Pickup.Status == IntentResultStatus.Accepted
            && canonical.Pickup.Events.Single().Kind == GameEventKind.ItemTaken
            && canonical.AfterPickup.Item(TinyFarmIds.WildMint).Owner == TinyFarmIds.Player
            && canonical.AfterPickup.Actor(TinyFarmIds.Player).Inventory.Contains(TinyFarmIds.WildMint)
            && canonical.AfterPickupFrame.GroundItems.Count == 0
            && canonical.Use.Status == IntentResultStatus.Accepted
            && canonical.Final.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop == TinyFarmIds.TurnipCrop
            && canonical.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed) == 2
            && canonical.Final.SelectedHotbarSlot == 1
            && canonical.PlantParity
            && canonical.FinalHash == canonical.LoadedHash
            && canonical.FinalHash == repeat.FinalHash
            && canonical.ResultsHash == repeat.ResultsHash
            && canonical.EventsHash == repeat.EventsHash;

        object proof = new
        {
            milestone = "TINY-FARM-M17",
            outcome = success ? "A" : "B",
            pickupUsesResolver = true,
            useSelectedLowersToExistingIntent = true,
            canonical.PlantParity,
            saveLoadExact = canonical.FinalHash == canonical.LoadedHash,
            headlessRepeatExact = canonical.FinalHash == repeat.FinalHash
                && canonical.ResultsHash == repeat.ResultsHash
                && canonical.EventsHash == repeat.EventsHash,
            hashes = new
            {
                state = canonical.FinalHash,
                results = canonical.ResultsHash,
                events = canonical.EventsHash,
                pickup = canonical.PickupHash,
                inventory = canonical.InventoryHash,
                hotbar = canonical.HotbarHash,
                useSelected = canonical.UseSelectedHash,
                plantParity = canonical.PlantParityHash,
                projection = canonical.ProjectionHash,
                simulationDto = canonical.SimulationDtoHash
            }
        };
        object pickup = new
        {
            intent = nameof(TakeIntent),
            graphicalLowering = $"{nameof(InteractIntent)} -> targeted ground item -> {nameof(TakeIntent)}",
            itemKind = "identity-bearing ItemId",
            item = TinyFarmIds.WildMint.Value,
            placement = "authoritative SceneId + ScenePosition",
            reachUnits = TinyFarmSpatialQueries.InteractionRangeUnits,
            priority = "actor, portal, ground item, plot, shop; then squared distance; then ordinal stable ID",
            result = canonical.Pickup.Status,
            eventKind = canonical.Pickup.Events.Single().Kind,
            inventoryCount = canonical.AfterPickup.Actor(TinyFarmIds.Player).Inventory.Count,
            groundItemCount = canonical.AfterPickupFrame.GroundItems.Count
        };
        object useSelected = new
        {
            intent = nameof(UseSelectedIntent),
            selectedSlot = canonical.Final.SelectedHotbarSlot,
            selectedBinding = TinyFarmIds.TurnipSeed.Value,
            lowering = $"{nameof(ProductHotbarBinding)}({TinyFarmIds.TurnipSeed.Value}) + plot -> {nameof(PlantIntent)}({TinyFarmIds.PlotOne.Value}, {TinyFarmIds.TurnipCrop.Value})",
            result = canonical.Use.Status,
            reason = canonical.Use.Reason,
            crop = canonical.Final.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).Crop?.Value,
            remainingSeeds = canonical.Final.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed),
            canonical.PlantParity
        };
        object uiProjection = new
        {
            groundBefore = canonical.BeforeFrame.GroundItems.Count,
            groundAfterPickup = canonical.AfterPickupFrame.GroundItems.Count,
            inventoryBefore = canonical.BeforeUi.Inventory,
            inventoryAfterPickup = canonical.AfterPickupUi.Inventory,
            hotbarAfterPlant = canonical.FinalUi.Hotbar,
            interactionHintBefore = canonical.BeforeUi.InteractionHint,
            interactionHintAtPlot = canonical.PlotUi.InteractionHint
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M17",
            kind = "resolver-owned-pickup-use-selected",
            pickupUsesResolver = true,
            worldItemAuthorityMoved = false,
            inventoryAuthorityMoved = false,
            useSelectedAdded = true,
            useSelectedLowersToExistingIntent = true,
            uiMutatesInventoryDirectly = false,
            genericItemFrameworkAdded = false,
            craftingAdded = false,
            combatAdded = false,
            dragDropAdded = false
        };
        return new TinyFarmM17Evidence(proof, pickup, useSelected, uiProjection, manifest);
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM17Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "pickup.json"), evidence.Pickup);
        Write(Path.Combine(directory, "use-selected.json"), evidence.UseSelected);
        Write(Path.Combine(directory, "ui-projection.json"), evidence.UiProjection);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static RunResult Run(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        TinyFarmFrame beforeFrame = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView beforeUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);

        TinyFarmStepResult pickupStep = session.Step(new InteractIntent(), evaluateNpcDecisions: false);
        IntentResult pickup = pickupStep.Results.Single();
        TinyFarmState afterPickup = session.State.DeepCopy();
        TinyFarmFrame afterPickupFrame = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlayerUiView afterPickupUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);

        session.Step(new SelectHotbarSlotIntent(new HotbarSlotId(1)), evaluateNpcDecisions: false);
        TinyFarmPlayerUiView plotUi = TinyFarmPlayerUiProjector.Project(session.State, definitions);
        TinyFarmState parityStart = session.State.DeepCopy();
        TinyFarmStepResult useStep = session.Step(new UseSelectedIntent(), evaluateNpcDecisions: false);
        IntentResult use = useStep.Results.Single();

        var direct = new TinyFarmSession(parityStart, definitions);
        TinyFarmStepResult directStep = direct.Step(
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            evaluateNpcDecisions: false);
        bool plantParity = TinyFarmSemanticHash.Compute(session.State) == TinyFarmSemanticHash.Compute(direct.State)
            && use.Status == directStep.Results.Single().Status
            && use.Reason == directStep.Results.Single().Reason
            && use.Events.SequenceEqual(directStep.Results.Single().Events);

        TinyFarmState final = session.State.DeepCopy();
        TinyFarmFrame finalFrame = TinyFarmFrameProjector.Project(final, definitions);
        TinyFarmPlayerUiView finalUi = TinyFarmPlayerUiProjector.Project(final, definitions);
        var host = new TinyFarmSimulationHost(session, definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        byte[] save = TinyFarmChunkedSaveCodec.Write(session, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        IntentResult[] semanticResults = [pickup, use];
        GameEvent[] events = semanticResults.SelectMany(result => result.Events).ToArray();
        return new RunResult(
            pickup,
            use,
            afterPickup,
            final,
            beforeFrame,
            afterPickupFrame,
            beforeUi,
            afterPickupUi,
            plotUi,
            finalUi,
            plantParity,
            TinyFarmSemanticHash.Compute(final),
            TinyFarmSemanticHash.Compute(loaded.State),
            Hash(semanticResults.Select(result => new { intent = result.Envelope.Intent.GetType().Name, result.Status, result.Reason })),
            Hash(events),
            TinyFarmSemanticHash.Compute(afterPickup),
            Hash(afterPickup.Actor(TinyFarmIds.Player).Inventory.Select(item => item.Value)),
            Hash(finalUi.Hotbar),
            TinyFarmSemanticHash.Compute(final),
            Hash(new { plantParity, direct = TinyFarmSemanticHash.Compute(direct.State) }),
            TinyFarmFrameProjector.ComputeHash(finalFrame),
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
        IntentResult Pickup,
        IntentResult Use,
        TinyFarmState AfterPickup,
        TinyFarmState Final,
        TinyFarmFrame BeforeFrame,
        TinyFarmFrame AfterPickupFrame,
        TinyFarmPlayerUiView BeforeUi,
        TinyFarmPlayerUiView AfterPickupUi,
        TinyFarmPlayerUiView PlotUi,
        TinyFarmPlayerUiView FinalUi,
        bool PlantParity,
        string FinalHash,
        string LoadedHash,
        string ResultsHash,
        string EventsHash,
        string PickupHash,
        string InventoryHash,
        string HotbarHash,
        string UseSelectedHash,
        string PlantParityHash,
        string ProjectionHash,
        string SimulationDtoHash);
}
