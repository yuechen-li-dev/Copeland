using Aurelian.Composition;
using System.Diagnostics;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Core.Styling;
using Machina.Core.Nodes;
using Machina.Presentation;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Resolving;
using Machina.Runtime.Input;
using TinyFarm.Presentation;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmCompositorM0Tests
{
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void MachinaProjection_LaysOutHudHotbarAndInventoryInsideViewport(int width, int height)
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(width, height);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        layer.Receive(Message(CreateSnapshot(inventoryOpen: true)));
        layer.Attach(surface);

        Rect hud = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hud.anchor")].Rect;
        Rect firstSlot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.1")].Rect;
        Rect lastSlot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.8")].Rect;
        Rect inventory = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.inventory.panel.anchor")].Rect;

        AssertInside(hud, width, height);
        AssertInside(firstSlot, width, height);
        AssertInside(lastSlot, width, height);
        AssertInside(inventory, width, height);
        Assert.True(firstSlot.X < lastSlot.X);
        Assert.True(inventory.Y < firstSlot.Y);
    }

    [Theory]
    [InlineData(2560, 1440, 1328, 112, 2402, 1336, 112, 72)]
    [InlineData(1280, 720, 644, 76, 1122, 652, 112, 60)]
    public void MachinaProjection_PreservesHandRolledMonoGameToolbarContract(
        int width,
        int height,
        int hudTop,
        int hudHeight,
        int toggleLeft,
        int toggleTop,
        int slotWidth,
        int slotHeight)
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(width, height);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        layer.Receive(Message(CreateSnapshot(inventoryOpen: false)));
        layer.Attach(surface);

        Rect hud = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hud.anchor")].Rect;
        Rect toggle = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.inventory.button.anchor")].Rect;
        Rect firstSlot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.1")].Rect;
        UiStyle hudStyle = layer.Prepared.Lowering.Styles[new NodeId("tiny-farm.hud.background")];
        UiStyle toggleStyle = layer.Prepared.Lowering.Styles[new NodeId("tiny-farm.inventory.button")];
        PositionedTextOperation heading = layer.Prepared.PresentationFrame.Operations
            .OfType<PositionedTextOperation>()
            .Single(operation => operation.SourceId == "tiny-farm.hud.heading.text");

        Assert.Equal(new Rect(0, hudTop, width, hudHeight), hud);
        Assert.Equal(new Rect(toggleLeft, toggleTop, 140, 32), toggle);
        Assert.Equal(slotWidth, firstSlot.Width);
        Assert.Equal(slotHeight, firstSlot.Height);
        Assert.Equal(ColorToken.Hex(0x131B19FF), hudStyle.Background);
        Assert.Equal(ColorToken.Hex(0x263531FF), toggleStyle.Background);
        Assert.Equal(ColorToken.Hex(0x7E9185FF), toggleStyle.BorderColor);
        Assert.Equal(2, toggleStyle.BorderThickness);
        Assert.Equal(18, heading.Rect.X);
        Assert.Equal(hudTop + 10, heading.Rect.Y);
        Assert.Equal(height >= 900 ? TextSize.H1 : TextSize.Md, heading.Style.Size);
        Assert.Equal(ColorToken.White, heading.Color);
        Assert.Contains("DAY 1", heading.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PointerActivation_UsesMachinaHitTestAndReturnsSemanticDto()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        compositor.Attach();
        Rect slot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.3")].Rect;

        LayerInputRoutingResult result = compositor.RouteInput(new LayerPointerButtonChanged(
            new LayerPoint(slot.X + (slot.Width / 2), slot.Y + (slot.Height / 2)),
            LayerPointerButton.Primary,
            true));

        Assert.True(result.Consumed);
        TinyFarmUiCommandDto command = Assert.Single(sink.Commands);
        Assert.Equal(TinyFarmUiCommandKind.SelectHotbarSlot, command.Kind);
        Assert.Equal(3, command.HotbarSlot);
    }

    [Fact]
    public void EmptyUiSpace_FallsThroughWhenInventoryClosed()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        var world = new ConsumingWorldLayer(surface);
        compositor.Add(world);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        compositor.Attach();

        LayerInputRoutingResult result = compositor.RouteInput(new LayerPointerButtonChanged(
            new LayerPoint(10, 10),
            LayerPointerButton.Primary,
            true));

        Assert.Equal(new LayerId("world"), result.ConsumedBy);
        Assert.Equal(new[] { TinyFarmMachinaUiLayer.Id, new LayerId("world") }, result.VisitedLayers);
    }

    [Fact]
    public void InventoryOpen_OwnsLayerFocusAndSuppressesWorldMovementWithoutStoppingSimulation()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        var world = new ConsumingWorldLayer(surface);
        compositor.Add(world);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: true)));
        compositor.Attach();

        LayerInputRoutingResult result = compositor.RouteInput(new LayerKeyChanged(LayerKey.ArrowLeft, true));

        Assert.Equal(TinyFarmMachinaUiLayer.Id, result.ConsumedBy);
        Assert.Equal(TinyFarmMachinaUiLayer.Id, result.FocusOwner);
        Assert.Empty(world.Inputs);
        Assert.Empty(sink.Commands);
    }

    [Fact]
    public void HotbarKeyboardAndPointerProduceSameSemanticCommand()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        compositor.Attach();
        Rect slot = layer.Prepared.Resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.2")].Rect;

        compositor.RouteInput(new LayerKeyChanged(LayerKey.Number2, true));
        compositor.RouteInput(new LayerPointerButtonChanged(
            new LayerPoint(slot.X + 1, slot.Y + 1),
            LayerPointerButton.Primary,
            true));

        Assert.Equal(2, sink.Commands.Count);
        Assert.All(sink.Commands, command =>
        {
            Assert.Equal(TinyFarmUiCommandKind.SelectHotbarSlot, command.Kind);
            Assert.Equal(2, command.HotbarSlot);
        });
    }

    [Fact]
    public void SimulationKeys_ReturnSemanticCommandsWithoutMutatingSimulation()
    {
        var sink = new RecordingSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        compositor.Add(layer);
        compositor.SendToLayer(Message(CreateSnapshot(inventoryOpen: false)));
        compositor.Attach();

        compositor.RouteInput(new LayerKeyChanged(LayerKey.Space, true));
        compositor.RouteInput(new LayerKeyChanged(LayerKey.F, true));

        Assert.Collection(
            sink.Commands,
            command => Assert.Equal(TinyFarmUiCommandKind.TogglePausePlay, command.Kind),
            command => Assert.Equal(TinyFarmUiCommandKind.ToggleFastForward, command.Kind));
    }

    [Fact]
    public void HeadlessCoreAndRuntimeRemainCompositorIndependent()
    {
        string[] coreReferences = typeof(TinyFarmState).Assembly.GetReferencedAssemblies()
            .Select(static value => value.Name ?? string.Empty)
            .ToArray();
        string[] runtimeReferences = typeof(TinyFarmSimulationHost).Assembly.GetReferencedAssemblies()
            .Select(static value => value.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Aurelian.Composition", coreReferences);
        Assert.DoesNotContain("Aurelian.Composition", runtimeReferences);
        Assert.DoesNotContain(coreReferences, name => name.StartsWith("Machina.", StringComparison.Ordinal));
        Assert.DoesNotContain(runtimeReferences, name => name.StartsWith("Machina.", StringComparison.Ordinal));
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

    private static void AssertInside(Rect rectangle, int width, int height)
    {
        Assert.True(rectangle.X >= 0);
        Assert.True(rectangle.Y >= 0);
        Assert.True(rectangle.X + rectangle.Width <= width);
        Assert.True(rectangle.Y + rectangle.Height <= height);
    }

    private sealed class RecordingSink : ILayerApplicationMessageSink
    {
        public List<TinyFarmUiCommandDto> Commands { get; } = [];

        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
            Commands.Add(Assert.IsType<TinyFarmUiCommandDto>(message.Payload));
        }
    }

    private sealed class ConsumingWorldLayer : IAurelianLayer
    {
        private readonly LayerSurfaceDescriptor surface;

        public ConsumingWorldLayer(LayerSurfaceDescriptor surface)
        {
            this.surface = surface;
        }

        public List<LayerInputEvent> Inputs { get; } = [];

        public LayerDescriptor Describe() => new(
            new LayerId("world"),
            0,
            true,
            surface.FullViewport,
            LayerPresentationMode.DirectHostPass,
            LayerInputPolicy.HitTest);

        public void Attach(LayerSurfaceDescriptor attachedSurface)
        {
        }

        public void Resize(LayerSurfaceDescriptor resizedSurface)
        {
        }

        public void Update(LayerUpdateContext context)
        {
        }

        public LayerPresentationDto Present(LayerPresentationContext context) => new(
            new LayerId("world"), surface.FullViewport, true, surface.Kind);

        public LayerInputResult HandleInput(LayerInputEvent input)
        {
            Inputs.Add(input);
            return LayerInputResult.ConsumedOnly;
        }

        public void Detach()
        {
        }
    }
}

public sealed class TinyFarmCompositorAllocationTests
{
    [Fact]
    public void MachinaUiAdapter_SteadyStateCacheMeetsAllocationTarget()
    {
        var sink = new AllocationSink();
        var surface = new LayerSurfaceDescriptor(1280, 720);
        var layer = new TinyFarmMachinaUiLayer(sink, surface);
        TinyFarmPresentationSnapshot snapshot = CreateAllocationSnapshot();
        LayerMessage<TinyFarmPresentationSnapshot> message = new(
            TinyFarmMachinaUiLayer.ApplicationId,
            TinyFarmMachinaUiLayer.Id,
            snapshot);

        for (int index = 0; index < 10; index++)
        {
            layer.Receive(message);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 100; index++)
        {
            layer.Receive(message);
        }
        stopwatch.Stop();
        long bytesPerRecomposition = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        double microsecondsPerRecomposition = stopwatch.Elapsed.TotalMicroseconds / 100;

        Console.WriteLine($"AURELIAN-COMPOSITOR-M1 ui adapter allocation: {bytesPerRecomposition} B/recomposition");
        Console.WriteLine($"AURELIAN-COMPOSITOR-M1 ui adapter time: {microsecondsPerRecomposition:0.00} us/recomposition");
        Assert.True(bytesPerRecomposition < 32 * 1024);
        Assert.Equal(1, layer.CacheMetrics.TopologyBuildCount);

        TinyFarmPresentationSnapshot changed = snapshot with
        {
            PlayerUi = snapshot.PlayerUi with { Money = snapshot.PlayerUi.Money + 1 }
        };
        for (int index = 0; index < 10; index++)
        {
            layer.Receive(index % 2 == 0 ? message : message with { Payload = changed });
        }
        before = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Restart();
        for (int index = 0; index < 100; index++)
        {
            layer.Receive(index % 2 == 0 ? message : message with { Payload = changed });
        }
        stopwatch.Stop();
        long bytesPerValueUpdate = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        double microsecondsPerValueUpdate = stopwatch.Elapsed.TotalMicroseconds / 100;
        Console.WriteLine($"AURELIAN-COMPOSITOR-M1 dynamic value allocation: {bytesPerValueUpdate} B/update");
        Console.WriteLine($"AURELIAN-COMPOSITOR-M1 dynamic value time: {microsecondsPerValueUpdate:0.00} us/update");
        Assert.True(bytesPerValueUpdate < 58_316);
    }

    private static TinyFarmPresentationSnapshot CreateAllocationSnapshot()
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
            false,
            "Ready",
            frame.InteractionHints,
            frame.Narrative);
    }

    private sealed class AllocationSink : ILayerApplicationMessageSink
    {
        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
        }
    }
}

public sealed class TinyFarmCompositorM0AttributionTests
{
    [Fact]
    public void ColdPath_ReportsIndependentlyMeasuredStageCosts()
    {
        TinyFarmPresentationSnapshot snapshot = CreateSnapshot();
        UiNode? document = null;
        UiLoweringResult? lowering = null;
        LayoutDocument? layout = null;
        ResolvedLayoutDocument? resolved = null;
        UiHitTestIndex? hitTest = null;
        MachinaPresentationFrame? frame = null;
        var rootRect = new Rect(0, 0, 1280, 720);

        StageMeasurement construction = Measure(() =>
            document = TinyFarmMachinaView.Build(snapshot, 1280, 720));
        document = TinyFarmMachinaView.Build(snapshot, 1280, 720);
        StageMeasurement loweringStage = Measure(() => lowering = UiLowerer.Lower(document));
        lowering = UiLowerer.Lower(document);
        StageMeasurement layoutConstruction = Measure(() =>
            layout = LayoutCompiler.CompileLayoutRows(lowering.Rows));
        layout = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        StageMeasurement layoutResolution = Measure(() =>
            resolved = LayoutDocumentResolver.ResolveLayoutDocument(layout, rootRect));
        resolved = LayoutDocumentResolver.ResolveLayoutDocument(layout, rootRect);
        StageMeasurement hitTestBuild = Measure(() =>
            hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics));
        StageMeasurement presentation = Measure(() =>
            frame = MachinaPresentationFrameBuilder.Build(
                lowering,
                resolved,
                new MachinaPresentationViewport(1280, 720)));
        hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);
        frame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(1280, 720));
        Rect firstSlot = resolved.Nodes[new NodeId("tiny-farm.hotbar.anchor.1")].Rect;
        StageMeasurement inputRouting = Measure(() =>
            _ = hitTest.HitTest(new PointerPoint(firstSlot.X + 1, firstSlot.Y + 1)));
        long adapterChecksum = 0;
        StageMeasurement adapterDispatch = Measure(() =>
        {
            foreach (MachinaPresentationOperation operation in frame.Operations)
            {
                adapterChecksum += operation switch
                {
                    FillRectangleOperation fill => (long)fill.Rect.Width,
                    StrokeRectangleOperation stroke => (long)stroke.Thickness,
                    PositionedTextOperation text => text.Text.Length,
                    PushRectangularClipOperation => 1,
                    PopClipOperation => -1,
                    _ => 0
                };
            }
        });

        PositionedTextOperation[] textOperations = frame.Operations.OfType<PositionedTextOperation>().ToArray();
        StageMeasurement textMeasurement = Measure(() =>
        {
            foreach (PositionedTextOperation text in textOperations)
            {
                _ = DeterministicTextMeasurer.Instance.MeasureText(text.Text, text.Style);
            }
        });

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            workload = "AURELIAN-COMPOSITOR-M0 cold 1280x720 closed-inventory projection",
            construction,
            lowering = loweringStage,
            layoutTreeConstruction = layoutConstruction,
            layoutResolution,
            presentationLowering = presentation,
            hitTestGeneration = hitTestBuild,
            textGlyphMeasurement = textMeasurement,
            normalizedInputRouting = inputRouting,
            backendNeutralAdapterDispatch = adapterDispatch,
            adapterChecksum
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Assert.NotNull(document);
        Assert.NotNull(frame);
        Assert.True(adapterChecksum > 0);
    }

    private static StageMeasurement Measure(Action action)
    {
        const int iterations = 500;
        for (int index = 0; index < 20; index++)
        {
            action();
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < iterations; index++)
        {
            action();
        }
        stopwatch.Stop();
        return new StageMeasurement(
            stopwatch.Elapsed.TotalNanoseconds / iterations,
            (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / iterations);
    }

    private static TinyFarmPresentationSnapshot CreateSnapshot()
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
            false,
            "Ready",
            frame.InteractionHints,
            frame.Narrative);
    }

    public sealed record StageMeasurement(double NanosecondsPerRecomposition, long BytesPerRecomposition);
}
