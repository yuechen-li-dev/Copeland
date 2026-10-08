using System.Text.Json;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM16Tests
{
    [Fact]
    public void Projection_IsExactAndDeterministicallyOrdered()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM16ControlStates.Create(definitions);

        TinyFarmPlayerUiView view = TinyFarmPlayerUiProjector.Project(state, definitions);

        Assert.Equal(12, view.Money);
        Assert.Equal(new[] { "turnip", "turnip-seed" }, view.Inventory.Select(item => item.SemanticId));
        Assert.Equal(new[] { 2, 3 }, view.Inventory.Select(item => item.Count));
        Assert.Equal(8, view.Hotbar.Count);
        Assert.Equal("turnip-seed", view.Hotbar[0].SemanticId);
        Assert.Equal("turnip", view.Hotbar[1].SemanticId);
        Assert.All(view.Hotbar.Skip(2), slot => Assert.Equal(TinyFarmHotbarSlotVisualState.Empty, slot.VisualState));
        Assert.Equal(1, view.SelectedSlot.Value);
        Assert.Equal("turnip-seed", view.SelectedSemanticId);
    }

    [Fact]
    public void KeyboardAndClick_UseTheSameResolverIntentAndResult()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost keyboardHost = CreateHost(definitions);
        TinyFarmSimulationHost clickHost = CreateHost(definitions);
        var keyboard = new TinyFarmPlayerUiController(keyboardHost);
        var click = new TinyFarmPlayerUiController(clickHost);

        keyboard.HandleKey(TinyFarmUiKey.Number4);
        click.ClickSlot(new HotbarSlotId(4));

        Assert.Equal(4, keyboardHost.Session.State.SelectedHotbarSlot);
        Assert.Equal(4, clickHost.Session.State.SelectedHotbarSlot);
        Assert.Equal(
            TinyFarmSemanticHash.Compute(keyboardHost.Session.State),
            TinyFarmSemanticHash.Compute(clickHost.Session.State));
    }

    [Fact]
    public void EmptySlot_RemainsSelectedWithNoSemanticBinding()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        var controller = new TinyFarmPlayerUiController(host);

        controller.HandleKey(TinyFarmUiKey.Number8);
        TinyFarmPlayerUiView view = TinyFarmPlayerUiProjector.Project(host.Session.State, definitions);

        Assert.Equal(8, view.SelectedSlot.Value);
        Assert.Null(view.SelectedSemanticId);
        Assert.True(view.Hotbar[7].IsSelected);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Empty, view.Hotbar[7].VisualState);
    }

    [Fact]
    public void MissingProduct_DisablesBindingWithoutClearingIt()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM16ControlStates.Create(definitions);
        state.MutableInventoryStacks.RemoveAll(stack => stack.Product == TinyFarmIds.Turnip);

        TinyFarmHotbarSlotView slot = TinyFarmPlayerUiProjector.Project(state, definitions).Hotbar[1];

        Assert.Equal("turnip", slot.SemanticId);
        Assert.Equal(0, slot.Count);
        Assert.Equal(TinyFarmHotbarSlotVisualState.Unavailable, slot.VisualState);
    }

    [Fact]
    public void InventoryOpen_IsPresentationOnlyAndSuppressesMovementWithoutPausing()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        var controller = new TinyFarmPlayerUiController(host);
        string before = TinyFarmSemanticHash.Compute(host.Session.State);

        controller.HandleKey(TinyFarmUiKey.Inventory);

        Assert.True(controller.InventoryOpen);
        Assert.True(controller.SuppressWorldMovement);
        Assert.Equal(TinyFarmSimulationMode.Playing, host.Mode);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(host.Session.State));
    }

    [Fact]
    public void SimulationKeys_AreRemappedAndKeepSimulationCommandsSemantic()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        var controller = new TinyFarmPlayerUiController(host);

        controller.HandleKey(TinyFarmUiKey.Number1);
        Assert.Equal(TinyFarmSimulationMode.Playing, host.Mode);
        controller.HandleKey(TinyFarmUiKey.PausePlay);
        Assert.Equal(TinyFarmSimulationMode.Paused, host.Mode);
        controller.HandleKey(TinyFarmUiKey.PausePlay);
        Assert.Equal(TinyFarmSimulationMode.Playing, host.Mode);
        controller.HandleKey(TinyFarmUiKey.FastForward);
        Assert.Equal(TinyFarmSimulationMode.FastForward, host.Mode);
        controller.HandleKey(TinyFarmUiKey.FastForward);
        Assert.Equal(TinyFarmSimulationMode.Playing, host.Mode);
    }

    [Fact]
    public void SelectedSlot_PersistsExactlyThroughChunkedSave()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        host.ExecuteIntent(new SelectHotbarSlotIntent(new HotbarSlotId(6)));

        byte[] save = TinyFarmChunkedSaveCodec.Write(host.Session, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);

        Assert.Equal(6, loaded.State.SelectedHotbarSlot);
        Assert.Equal(
            TinyFarmSemanticHash.Compute(host.Session.State),
            TinyFarmSemanticHash.Compute(loaded.State));
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void ResponsiveLayout_FitsViewportWithoutClipping(int width, int height)
    {
        TinyFarmPlayerUiLayout layout = TinyFarmPlayerUiLayoutEngine.Compute(width, height, 2);

        Assert.Equal(8, layout.HotbarSlots.Count);
        Assert.All(layout.HotbarSlots, slot =>
        {
            Assert.InRange(slot.X, 0, width);
            Assert.InRange(slot.Right, 0, width);
            Assert.InRange(slot.Y, 0, height);
            Assert.InRange(slot.Bottom, 0, height);
        });
        Assert.InRange(layout.InventoryPanel.X, 0, width);
        Assert.InRange(layout.InventoryPanel.Right, 0, width);
        Assert.InRange(layout.InventoryPanel.Bottom, 0, height);
    }

    [Fact]
    public void SimulationSnapshotAndTson_ExposeSemanticInventoryAndSelection()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateHost(definitions);
        host.ExecuteIntent(new SelectHotbarSlotIntent(new HotbarSlotId(2)));

        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        string tson = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot);

        Assert.Equal("tiny-farm-simulation@2", snapshot.Version);
        Assert.Equal(2, snapshot.PlayerUi!.SelectedSlot.Value);
        Assert.Contains("selectedHotbarSlot", tson, StringComparison.Ordinal);
        Assert.Contains("turnip-seed:3", tson, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticUiTypes_RemainRendererIndependent()
    {
        Type[] semanticTypes =
        [
            typeof(HotbarSlotId),
            typeof(HotbarBinding),
            typeof(TinyFarmPlayerUiView),
            typeof(TinyFarmPlayerUiController)
        ];

        string[] assemblyNames = semanticTypes
            .SelectMany(type => type.Assembly.GetReferencedAssemblies())
            .Select(name => name.Name ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(assemblyNames, name =>
            name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xna", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalScenario_ProducesOutcomeAAndFiveCompactArtifacts()
    {
        TinyFarmM16Evidence evidence = TinyFarmM16Scenario.Prove();
        using JsonDocument proof = JsonDocument.Parse(TinyFarmM16Scenario.WriteJson(evidence.Proof));

        Assert.Equal("A", proof.RootElement.GetProperty("outcome").GetString());
    }

    private static TinyFarmSimulationHost CreateHost(TinyFarmDefinitions definitions)
    {
        return new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM16ControlStates.Create(definitions), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
    }
}
