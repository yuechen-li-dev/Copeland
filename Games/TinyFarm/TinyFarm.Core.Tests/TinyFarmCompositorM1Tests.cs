using Aurelian.Composition;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Presentation;
using TinyFarm.Core;
using TinyFarm.Presentation;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmCompositorM1Tests
{
    [Fact]
    public void StableTopology_ReusesAllPreparedWorkFor120Frames()
    {
        var layer = CreateLayer(out LayerSurfaceDescriptor surface);
        TinyFarmPresentationSnapshot snapshot = CreateSnapshot(inventoryOpen: false);

        layer.Receive(Message(snapshot));
        layer.Attach(surface);
        for (int frame = 1; frame < 120; frame++)
        {
            layer.Receive(Message(snapshot));
        }

        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
        Assert.Equal(1, layer.CacheMetrics.LayoutBuildCount);
        Assert.Equal(1, layer.CacheMetrics.PresentationLowerCount);
        Assert.Equal(1, layer.CacheMetrics.HitTestBuildCount);
        Assert.Equal(0, layer.CacheMetrics.DynamicUpdateCount);
    }

    [Fact]
    public void LayerVisibilityDoesNotDiscardPreparedMachinaState()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        compositor.Attach();
        Machina.Pipeline.MachinaPreparedPresentation prepared = layer.Prepared;

        compositor.SetEnabled(TinyFarmMachinaUiLayer.Id, false);
        compositor.RunFrame(1, TimeSpan.FromMilliseconds(16));
        compositor.SetEnabled(TinyFarmMachinaUiLayer.Id, true);
        compositor.RunFrame(2, TimeSpan.FromMilliseconds(16));

        Assert.Same(prepared, layer.Prepared);
        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
    }

    [Fact]
    public void MoneyAndClockChanges_UpdateHudWithoutStructuralWork()
    {
        var layer = CreateLayer(out _);
        TinyFarmPresentationSnapshot before = CreateSnapshot(inventoryOpen: false);
        layer.Receive(Message(before));
        TinyFarmPresentationSnapshot after = before with
        {
            Day = 2,
            Time = "07:10",
            PlayerUi = before.PlayerUi with { Money = before.PlayerUi.Money + 25 }
        };

        layer.Receive(Message(after));

        PositionedTextOperation heading = Text(layer, "tiny-farm.hud.heading.text");
        Assert.Contains("DAY 2", heading.Text, StringComparison.Ordinal);
        Assert.Contains("07:10", heading.Text, StringComparison.Ordinal);
        Assert.Contains($"{after.PlayerUi.Money}G", heading.Text, StringComparison.Ordinal);
        AssertValueUpdateOnly(layer);
    }

    [Fact]
    public void HotbarSelectionAndCount_UpdateWithoutTopologyOrHitTestRebuild()
    {
        var layer = CreateLayer(out _);
        TinyFarmPresentationSnapshot before = CreateSnapshot(inventoryOpen: false);
        layer.Receive(Message(before));
        TinyFarmHotbarSlotView[] hotbar = before.PlayerUi.Hotbar
            .Select(slot => slot with
            {
                IsSelected = slot.Slot.Value == 2,
                Count = slot.Slot.Value == 2 ? slot.Count + 3 : slot.Count
            })
            .ToArray();
        TinyFarmPresentationSnapshot after = before with
        {
            PlayerUi = before.PlayerUi with
            {
                Hotbar = hotbar,
                SelectedSlot = new HotbarSlotId(2)
            }
        };

        layer.Receive(Message(after));

        StrokeRectangleOperation selected = layer.Prepared.PresentationFrame.Operations
            .OfType<StrokeRectangleOperation>()
            .Single(operation => operation.SourceId == "tiny-farm.hotbar.button.2");
        Assert.Equal(4, selected.Thickness);
        Assert.Contains($"X{hotbar[1].Count}", Text(layer, "tiny-farm.hotbar.label.2.text").Text, StringComparison.Ordinal);
        AssertValueUpdateOnly(layer);
    }

    [Fact]
    public void InventoryCountUpdatesButRowInsertionAndRemovalRebuildTopology()
    {
        var layer = CreateLayer(out _);
        TinyFarmPresentationSnapshot initial = WithInventory(
            CreateSnapshot(inventoryOpen: true),
            new TinyFarmPlayerInventoryView("product.hen-of-the-woods", "Hen-of-the-Woods", 1));
        layer.Receive(Message(initial));

        TinyFarmPresentationSnapshot countChanged = WithInventory(
            initial,
            new TinyFarmPlayerInventoryView("product.hen-of-the-woods", "Hen-of-the-Woods", 2));
        layer.Receive(Message(countChanged));
        Assert.Contains("X2", Text(layer, "tiny-farm.inventory.row.product.hen-of-the-woods.text").Text, StringComparison.Ordinal);
        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
        Assert.Equal(1, layer.CacheMetrics.DynamicUpdateCount);

        TinyFarmPresentationSnapshot inserted = WithInventory(
            countChanged,
            new TinyFarmPlayerInventoryView("product.hen-of-the-woods", "Hen-of-the-Woods", 1),
            new TinyFarmPlayerInventoryView("product.cooked-mushroom", "Cooked Mushroom", 1));
        layer.Receive(Message(inserted));
        Assert.Equal(2, layer.CacheMetrics.TopologyBuildCount);
        Assert.Contains(layer.Prepared.PresentationFrame.Operations, operation =>
            operation is PositionedTextOperation text
                && text.SourceId == "tiny-farm.inventory.row.product.cooked-mushroom.text");

        TinyFarmPresentationSnapshot removed = WithInventory(
            inserted,
            new TinyFarmPlayerInventoryView("product.cooked-mushroom", "Cooked Mushroom", 1));
        layer.Receive(Message(removed));
        Assert.Equal(3, layer.CacheMetrics.TopologyBuildCount);
        Assert.DoesNotContain(layer.Prepared.PresentationFrame.Operations, operation =>
            operation is PositionedTextOperation text
                && text.SourceId == "tiny-farm.inventory.row.product.hen-of-the-woods.text");
    }

    [Fact]
    public void ResizeAndScaleInvalidateLayoutAndHitTestingWithoutChangingTopology()
    {
        var layer = CreateLayer(out _);
        layer.Receive(Message(CreateSnapshot(inventoryOpen: false)));

        layer.Resize(new LayerSurfaceDescriptor(2560, 1440));
        Rect largeSlot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.1")].Rect;
        layer.Resize(new LayerSurfaceDescriptor(2560, 1440, scale: 2));

        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
        Assert.Equal(3, layer.CacheMetrics.LayoutBuildCount);
        Assert.Equal(3, layer.CacheMetrics.HitTestBuildCount);
        Assert.Equal(112, largeSlot.Width);
    }

    [Fact]
    public void HintUpdatesDoNotLeaveStaleCombatText()
    {
        var layer = CreateLayer(out _);
        TinyFarmPresentationSnapshot before = CreateSnapshot(inventoryOpen: false) with
        {
            InteractionHints = ["A", "B", "C", "D", "ENTER ATTACK SLIME"]
        };
        layer.Receive(Message(before));
        Assert.Contains("ATTACK SLIME", Text(layer, "tiny-farm.hud.controls.text").Text, StringComparison.Ordinal);

        layer.Receive(Message(before with { InteractionHints = ["A", "B", "C", "D"] }));

        Assert.DoesNotContain("ATTACK SLIME", Text(layer, "tiny-farm.hud.controls.text").Text, StringComparison.Ordinal);
        AssertValueUpdateOnly(layer);
    }

    [Fact]
    public void ClosingInventoryRemovesItsControlsAndDoesNotRetainCapture()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        var world = new ConsumingWorldLayer(surface);
        compositor.Add(world);
        compositor.Add(layer);
        compositor.SendToLayer(Message(WithInventory(
            CreateSnapshot(inventoryOpen: true),
            new TinyFarmPlayerInventoryView("product.hen-of-the-woods", "Hen-of-the-Woods", 1))));
        compositor.Attach();
        Rect toggle = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.inventory.button.anchor")].Rect;
        var point = new LayerPoint(toggle.X + 1, toggle.Y + 1);
        compositor.RouteInput(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
        compositor.RouteInput(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, false));
        Assert.Null(compositor.CaptureOwner);

        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        Assert.DoesNotContain(layer.Prepared.Resolved.Nodes.Keys, id =>
            id.Value.StartsWith("tiny-farm.inventory.row.", StringComparison.Ordinal));
        LayerInputRoutingResult result = compositor.RouteInput(new LayerPointerButtonChanged(
            new LayerPoint(10, 10),
            LayerPointerButton.Primary,
            true));

        Assert.Equal(new LayerId("world"), result.ConsumedBy);
        Assert.Null(result.CaptureOwner);
    }

    private static TinyFarmMachinaUiLayer CreateLayer(out LayerSurfaceDescriptor surface)
    {
        surface = new LayerSurfaceDescriptor(1280, 720);
        return new TinyFarmMachinaUiLayer(new RecordingSink(), surface);
    }

    private static TinyFarmPresentationSnapshot WithInventory(
        TinyFarmPresentationSnapshot snapshot,
        params TinyFarmPlayerInventoryView[] items)
    {
        return snapshot with { PlayerUi = snapshot.PlayerUi with { Inventory = items } };
    }

    private static PositionedTextOperation Text(TinyFarmMachinaUiLayer layer, string sourceId)
    {
        return layer.Prepared.PresentationFrame.Operations
            .OfType<PositionedTextOperation>()
            .Single(operation => operation.SourceId == sourceId);
    }

    private static void AssertValueUpdateOnly(TinyFarmMachinaUiLayer layer)
    {
        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);
        Assert.Equal(1, layer.CacheMetrics.LayoutBuildCount);
        Assert.Equal(1, layer.CacheMetrics.PresentationLowerCount);
        Assert.Equal(1, layer.CacheMetrics.HitTestBuildCount);
        Assert.Equal(1, layer.CacheMetrics.DynamicUpdateCount);
    }

    private static LayerMessage<TinyFarmPresentationSnapshot> Message(TinyFarmPresentationSnapshot snapshot)
    {
        return new LayerMessage<TinyFarmPresentationSnapshot>(
            TinyFarmMachinaUiLayer.ApplicationId,
            TinyFarmMachinaUiLayer.Id,
            snapshot);
    }

    private static TinyFarmPresentationSnapshot CreateSnapshot(bool inventoryOpen)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);
        return new TinyFarmPresentationSnapshot(
            TinyFarmPlayerUiProjector.Project(state, definitions),
            frame.Day,
            frame.Time,
            frame.CurrentLocationName,
            TinyFarmSimulationMode.Playing,
            inventoryOpen,
            "Ready",
            frame.InteractionHints,
            frame.Narrative);
    }

    private sealed class RecordingSink : ILayerApplicationMessageSink
    {
        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
        }
    }

    private sealed class ConsumingWorldLayer(LayerSurfaceDescriptor surface) : IAurelianLayer
    {
        public LayerDescriptor Describe() => new(
            new LayerId("world"),
            0,
            true,
            surface.FullViewport,
            LayerPresentationMode.DirectHostPass,
            LayerInputPolicy.HitTest);

        public void Attach(LayerSurfaceDescriptor attachedSurface) { }

        public void Resize(LayerSurfaceDescriptor resizedSurface) { }

        public void Update(LayerUpdateContext context) { }

        public LayerPresentationDto Present(LayerPresentationContext context) => new(
            new LayerId("world"), surface.FullViewport, true, surface.Kind);

        public LayerInputResult HandleInput(LayerInputEvent input) => LayerInputResult.ConsumedOnly;

        public void Detach() { }
    }
}
