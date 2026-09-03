using System.Reflection;
using Aurelian.Composition;
using Xunit;

namespace Aurelian.Composition.Tests;

public sealed class LayerCompositorTests
{
    [Fact]
    public void Frame_UpdatesAndPresentsEnabledLayersInStableOrder()
    {
        var events = new List<string>();
        using var compositor = CreateCompositor();
        compositor.Add(new FakeLayer("top", 100, events));
        compositor.Add(new FakeLayer("bottom-b", 0, events));
        compositor.Add(new FakeLayer("bottom-a", 0, events));
        compositor.Attach();

        IReadOnlyList<LayerPresentationDto> result = compositor.RunFrame(7, TimeSpan.FromMilliseconds(16));

        Assert.Equal(new[] { "bottom-a", "bottom-b", "top" }, result.Select(static item => item.Layer.Value));
        Assert.Equal(
            new[]
            {
                "attach:bottom-a", "attach:bottom-b", "attach:top",
                "update:bottom-a", "update:bottom-b", "update:top",
                "present:bottom-a", "present:bottom-b", "present:top"
            },
            events);
    }

    [Fact]
    public void Pointer_TopConsumesAndBottomDoesNotReceive()
    {
        using var compositor = CreateCompositor();
        var bottom = new FakeLayer("bottom", 0);
        var top = new FakeLayer("top", 100) { InputResponse = new LayerInputResult(true, RequestFocus: true) };
        compositor.Add(bottom);
        compositor.Add(top);
        compositor.Attach();

        LayerInputRoutingResult result = compositor.RouteInput(
            new LayerPointerButtonChanged(new LayerPoint(20, 30), LayerPointerButton.Primary, true));

        Assert.True(result.Consumed);
        Assert.Equal(new LayerId("top"), result.ConsumedBy);
        Assert.Equal(new[] { "top" }, result.VisitedLayers.Select(static id => id.Value));
        Assert.Empty(bottom.Inputs);
    }

    [Fact]
    public void Pointer_UnconsumedFallsThroughAndCoordinatesAreViewportLocal()
    {
        using var compositor = CreateCompositor(scale: 2);
        var bottom = new FakeLayer("bottom", 0) { InputResponse = LayerInputResult.ConsumedOnly };
        var top = new FakeLayer("top", 100, viewport: new LayerViewport(10, 20, 100, 100));
        compositor.Add(bottom);
        compositor.Add(top);
        compositor.Attach();

        LayerInputRoutingResult result = compositor.RouteInput(
            new LayerPointerButtonChanged(new LayerPoint(30, 60), LayerPointerButton.Primary, true));

        Assert.Equal(new[] { "top", "bottom" }, result.VisitedLayers.Select(static id => id.Value));
        LayerPointerButtonChanged routed = Assert.IsType<LayerPointerButtonChanged>(Assert.Single(top.Inputs));
        Assert.Equal(new LayerPoint(10, 20), routed.Position);
    }

    [Fact]
    public void FocusAndCapture_HaveExplicitLayerOwnership()
    {
        using var compositor = CreateCompositor();
        var bottom = new FakeLayer("bottom", 0) { InputResponse = LayerInputResult.ConsumedOnly };
        var top = new FakeLayer("top", 100)
        {
            InputResponse = new LayerInputResult(true, RequestFocus: true, RequestCapture: true)
        };
        compositor.Add(bottom);
        compositor.Add(top);
        compositor.Attach();
        compositor.RouteInput(new LayerPointerButtonChanged(new LayerPoint(1, 1), LayerPointerButton.Primary, true));

        top.InputResponse = new LayerInputResult(false, ReleaseCapture: true);
        LayerInputRoutingResult result = compositor.RouteInput(new LayerKeyChanged(LayerKey.Enter, true));

        Assert.Equal(new LayerId("top"), compositor.FocusOwner);
        Assert.Null(compositor.CaptureOwner);
        Assert.Equal(new[] { "top", "bottom" }, result.VisitedLayers.Select(static id => id.Value));
    }

    [Fact]
    public void ResizeAndVisibility_ArePropagatedWithoutDestroyingLayer()
    {
        using var compositor = CreateCompositor();
        var layer = new FakeLayer("layer", 0);
        compositor.Add(layer);
        compositor.Attach();

        compositor.Resize(new LayerSurfaceDescriptor(1280, 720, 1.25));
        compositor.SetEnabled(new LayerId("layer"), false);
        IReadOnlyList<LayerPresentationDto> result = compositor.RunFrame(1, TimeSpan.Zero);

        Assert.Equal(1280, Assert.Single(layer.Resizes).Width);
        Assert.Empty(result);
    }

    [Fact]
    public void ExplicitZOrderMutation_ChangesCompositionWithoutListAccess()
    {
        using var compositor = CreateCompositor();
        compositor.Add(new FakeLayer("first", 0));
        compositor.Add(new FakeLayer("second", 100));
        compositor.Attach();

        compositor.SetZOrder(new LayerId("first"), 200);
        IReadOnlyList<LayerPresentationDto> result = compositor.RunFrame(1, TimeSpan.Zero);

        Assert.Equal(new[] { "second", "first" }, result.Select(static item => item.Layer.Value));
    }

    [Fact]
    public void TypedApplicationMessage_IsDeliveredWithoutSerializationOrReflectionBus()
    {
        using var compositor = CreateCompositor();
        var layer = new MessageLayer("ui", 100);
        compositor.Add(layer);
        compositor.SendToLayer(new LayerMessage<CounterDto>(new LayerId("application"), new LayerId("ui"), new CounterDto(9)));

        Assert.Equal(new CounterDto(9), layer.LastMessage?.Payload);
    }

    [Fact]
    public void TypedLayerMessage_ReachesApplicationSink()
    {
        var sink = new RecordingApplicationSink();
        using var compositor = CreateCompositor();
        compositor.Add(new PublishingLayer("ui", 100, sink));
        compositor.Attach();

        compositor.RouteInput(new LayerKeyChanged(LayerKey.Enter, true));

        Assert.Equal(new CounterDto(4), Assert.Single(sink.Messages).Payload);
    }

    [Fact]
    public void PresentationFailure_IdentifiesExactLayerAndFrame()
    {
        using var compositor = CreateCompositor();
        compositor.Add(new FakeLayer("broken", 0) { FailPresentation = true });
        compositor.Attach();

        AurelianLayerPresentationException error = Assert.Throws<AurelianLayerPresentationException>(
            () => compositor.RunFrame(42, TimeSpan.Zero));

        Assert.Equal(new LayerId("broken"), error.Layer);
        Assert.Equal((ulong)42, error.FrameId);
    }

    [Fact]
    public void Contracts_HaveNoBackendOrGameAssemblyReferences()
    {
        string[] references = typeof(AurelianLayerCompositor).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("TinyFarm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("Machina", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
    }

    private static AurelianLayerCompositor CreateCompositor(double scale = 1)
    {
        return new AurelianLayerCompositor(new LayerSurfaceDescriptor(2560, 1440, scale));
    }

    private sealed record CounterDto(int Value);

    private sealed class RecordingApplicationSink : ILayerApplicationMessageSink
    {
        public List<LayerMessage<CounterDto>> Messages { get; } = [];

        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
            Messages.Add(Assert.IsType<LayerMessage<CounterDto>>(message));
        }
    }

    private class FakeLayer : IAurelianLayer
    {
        private readonly List<string> events;
        private LayerDescriptor descriptor;

        public FakeLayer(string id, int zOrder, List<string>? events = null, LayerViewport? viewport = null)
        {
            this.events = events ?? [];
            descriptor = new LayerDescriptor(
                new LayerId(id),
                zOrder,
                true,
                viewport ?? new LayerViewport(0, 0, 2560, 1440),
                LayerPresentationMode.DirectHostPass,
                LayerInputPolicy.HitTest);
        }

        public List<LayerInputEvent> Inputs { get; } = [];
        public List<LayerSurfaceDescriptor> Resizes { get; } = [];
        public LayerInputResult InputResponse { get; set; } = LayerInputResult.Unconsumed;
        public bool FailPresentation { get; set; }

        public LayerDescriptor Describe() => descriptor;

        public void Attach(LayerSurfaceDescriptor surface) => events.Add($"attach:{descriptor.Id}");

        public void Resize(LayerSurfaceDescriptor surface)
        {
            Resizes.Add(surface);
            descriptor = descriptor with { Viewport = surface.FullViewport };
        }

        public void Update(LayerUpdateContext context) => events.Add($"update:{descriptor.Id}");

        public LayerPresentationDto Present(LayerPresentationContext context)
        {
            events.Add($"present:{descriptor.Id}");
            if (FailPresentation)
            {
                throw new InvalidOperationException("fake failure");
            }
            return new LayerPresentationDto(descriptor.Id, descriptor.Viewport, true, context.Surface.Kind);
        }

        public virtual LayerInputResult HandleInput(LayerInputEvent input)
        {
            Inputs.Add(input);
            return InputResponse;
        }

        public void Detach() => events.Add($"detach:{descriptor.Id}");
    }

    private sealed class MessageLayer : FakeLayer, IAurelianLayerMessageReceiver<CounterDto>
    {
        public MessageLayer(string id, int zOrder)
            : base(id, zOrder)
        {
        }

        public LayerMessage<CounterDto>? LastMessage { get; private set; }

        public void Receive(LayerMessage<CounterDto> message)
        {
            LastMessage = message;
        }
    }

    private sealed class PublishingLayer : FakeLayer
    {
        private readonly RecordingApplicationSink sink;

        public PublishingLayer(string id, int zOrder, RecordingApplicationSink sink)
            : base(id, zOrder)
        {
            this.sink = sink;
        }

        public override LayerInputResult HandleInput(LayerInputEvent input)
        {
            sink.Publish(new LayerMessage<CounterDto>(new LayerId("ui"), null, new CounterDto(4)));
            return LayerInputResult.ConsumedOnly;
        }
    }
}
