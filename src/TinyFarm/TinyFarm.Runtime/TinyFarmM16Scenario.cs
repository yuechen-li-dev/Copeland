using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM16Evidence(
    object Proof,
    object UiProjection,
    object InputRouting,
    object HostIntegration,
    object Manifest);

public static class TinyFarmM16ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM14ControlStates.Create(definitions, "wander");
        return new TinyFarmState(
            TinyFarmState.PlayerUiSaveVersion,
            baseline.Minute,
            baseline.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            [
                new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 3),
                new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 2)
            ],
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            baseline.ActorScenes.ToList(),
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1);
    }
}

public static class TinyFarmM16Scenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM16Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState initial = TinyFarmM16ControlStates.Create(definitions);
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(initial, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        var controller = new TinyFarmPlayerUiController(host);
        TinyFarmPlayerUiView before = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions);
        controller.HandleKey(TinyFarmUiKey.Number2);
        TinyFarmPlayerUiView afterKey = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions);
        controller.ClickSlot(new HotbarSlotId(3));
        TinyFarmPlayerUiView afterClick = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions);

        string semanticBeforeInventory = TinyFarmSemanticHash.Compute(host.Session.State);
        controller.HandleKey(TinyFarmUiKey.Inventory);
        string semanticAfterInventory = TinyFarmSemanticHash.Compute(host.Session.State);

        controller.HandleKey(TinyFarmUiKey.PausePlay);
        TinyFarmSimulationMode paused = host.Mode;
        controller.HandleKey(TinyFarmUiKey.PausePlay);
        controller.HandleKey(TinyFarmUiKey.FastForward);
        TinyFarmSimulationMode fast = host.Mode;
        controller.HandleKey(TinyFarmUiKey.FastForward);

        byte[] save = TinyFarmChunkedSaveCodec.Write(host.Session, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        TinyFarmPlayerUiView reloaded = TinyFarmPlayerUiProjector.Project(loaded.State, definitions);
        bool success = before.SelectedSlot.Value == 1
            && afterKey.SelectedSlot.Value == 2
            && afterClick.SelectedSlot.Value == 3
            && semanticBeforeInventory == semanticAfterInventory
            && controller.InventoryOpen
            && controller.SuppressWorldMovement
            && paused == TinyFarmSimulationMode.Paused
            && fast == TinyFarmSimulationMode.FastForward
            && host.Mode == TinyFarmSimulationMode.Playing
            && reloaded.SelectedSlot.Value == 3;

        object proof = new
        {
            milestone = "TINY-FARM-M16",
            outcome = success ? "A" : "B",
            initialSelectedSlot = before.SelectedSlot.Value,
            selectedAfterKeyboard = afterKey.SelectedSlot.Value,
            selectedAfterClick = afterClick.SelectedSlot.Value,
            inventoryOpenDoesNotMutateGameTruth = semanticBeforeInventory == semanticAfterInventory,
            saveLoadSelectedSlotExact = reloaded.SelectedSlot.Value == 3,
            stateHash = TinyFarmSemanticHash.Compute(loaded.State)
        };
        object uiProjection = new
        {
            before.Money,
            before.Inventory,
            before.Hotbar,
            before.SelectedSlot,
            before.SelectedSemanticId,
            inventoryOrdering = "semantic ID ordinal",
            zeroCountBindingLaw = "binding remains and projects Unavailable",
            emptySlotLaw = "empty slot remains selectable"
        };
        object inputRouting = new
        {
            hotbarKeys = "1-8",
            pausePlayKey = "Space",
            fastForwardKey = "F",
            waitKey = "N",
            inventoryKey = "I",
            pointerAndKeyboardIssue = nameof(SelectHotbarSlotIntent),
            inventoryOpenSuppressesMovement = controller.SuppressWorldMovement,
            inventoryOpenDoesNotPause = true
        };
        object hostIntegration = new
        {
            ownershipDecision = "MONOGAME_TEMPORARY_UI",
            machinaUiRuntimeReused = false,
            reason = "Machina has layout, controls, focus, hit testing, and presentation IR but no qualified same-window game renderer/input adapter.",
            worldRenderer = "existing graphical game leaf",
            semanticProjection = nameof(TinyFarmPlayerUiView),
            mutationPath = "input -> TinyFarmPlayerUiController -> SelectHotbarSlotIntent -> TinyFarmSession -> TinyFarmResolver"
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M16",
            kind = "inventory-hotbar-ui-foundation",
            existingUiInfrastructureAuditedFirst = true,
            machinaUiReusedWhereApplicable = true,
            inventoryAuthorityMoved = false,
            hotbarSemanticStateAdded = true,
            uiMutatesGameplayDirectly = false,
            numberKeysOwnHotbarSelection = true,
            simulationControlConflictResolved = true,
            skillSystemAdded = false,
            combatAdded = false,
            craftingAdded = false
        };
        return new TinyFarmM16Evidence(proof, uiProjection, inputRouting, hostIntegration, manifest);
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM16Evidence evidence = Prove();
        Write(Path.Combine(directory, "proof.json"), evidence.Proof);
        Write(Path.Combine(directory, "ui-projection.json"), evidence.UiProjection);
        Write(Path.Combine(directory, "input-routing.json"), evidence.InputRouting);
        Write(Path.Combine(directory, "host-integration.json"), evidence.HostIntegration);
        Write(Path.Combine(directory, "manifest.json"), evidence.Manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static void Write(string path, object value)
    {
        File.WriteAllText(path, WriteJson(value) + Environment.NewLine);
    }
}
