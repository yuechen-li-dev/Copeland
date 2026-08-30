using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Machina.Presenter.Sample;
using Machina.Presentation.Input;
using Machina.Runtime.Input;
using Machina.Standard.Theme;
using Xunit;

namespace Aurelian.VisibleTriangle.Tests;

public sealed class VisibleTriangleHostInputCollectorTests
{
    [Fact]
    public void HostIteration_PublishesOneImmutableOrderedBatchAndFansOutLifecycle()
    {
        var collector = new VisibleTriangleHostInputCollector();
        collector.RecordSurfaceResize(640, 480);
        collector.Record(new UiPointerMoved(new PointerPoint(24, 32), null, UiModifiers.None));
        collector.Record(new UiKeyChanged(UiKey.Enter, true, false, UiModifiers.None));
        collector.RecordCloseRequest();

        UiInputBatch batch = collector.Publish();
        UiInputBatch emptyFollowingBatch = collector.Publish();

        Assert.Collection(
            batch.Events,
            inputEvent => Assert.IsType<UiSurfaceResized>(inputEvent),
            inputEvent => Assert.IsType<UiPointerMoved>(inputEvent),
            inputEvent => Assert.IsType<UiKeyChanged>(inputEvent),
            inputEvent => Assert.IsType<UiCloseRequested>(inputEvent));
        Assert.Empty(emptyFollowingBatch.Events);
        Assert.Equal(batch.BatchId + 1, emptyFollowingBatch.BatchId);

        var frontendRouting = MachinaFrontendInputRouter.Route(batch);
        var closeRequest = AurelianHostInputTranslator.Translate(
            frontendRouting.FrontendMessages.OfType<MachinaFrontendCloseRequested>().Single());
        var lifecycle = AurelianHostInputTranslator.TranslateLifecycle(frontendRouting.FrontendMessages);
        Assert.Equal(new Aurelian.Core.Engine.Frames.AurelianHostExtent(640, 480), lifecycle.HostExtent);
        Assert.NotNull(closeRequest);
    }

    [Fact]
    public async Task MixedCollectedHostIteration_UsesOneBatchForRoutersAndStopsAurelianOnClose()
    {
        var collector = new VisibleTriangleHostInputCollector();
        collector.RecordSurfaceResize(800, 600);
        collector.Record(new UiPointerMoved(new PointerPoint(220, 200), null, UiModifiers.None));
        collector.Record(new UiPointerWheel(new PointerPoint(220, 200), 0, 1, UiModifiers.None));
        collector.Record(new UiKeyChanged(UiKey.Enter, true, false, UiModifiers.None));
        collector.RecordCloseRequest();

        UiInputBatch batch = collector.Publish();
        PresenterUiInputRoutingResult presenterRouting = PresenterUiInputRouter.Route(
            CreateRender(),
            batch,
            ScrollbarInteractionState.Default,
            size => CreateRender(size.Width, size.Height, PresenterShellMode.Compact));
        MachinaFrontendInputRoutingResult frontendRouting = MachinaFrontendInputRouter.Route(batch);
        AurelianHostInputTranslation translation = AurelianHostInputTranslator.Translate(frontendRouting.FrontendMessages);

        var engine = new AurelianEngine();
        Assert.True(engine.Start().Success);
        var loop = new AurelianFrameLoop(
            new AurelianFramePump(engine, new CompositorActuationBridge(new NoOpCompositorMechanism())),
            new SingleInputProvider(Input(translation)));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(1));

        Assert.Equal((ulong)0, batch.BatchId);
        Assert.Equal(5, batch.Events.Length);
        Assert.Equal(1, presenterRouting.RecompositionCount);
        Assert.Equal(3, presenterRouting.RoutedEvents.Length);
        Assert.Equal(PresenterNavigationHitKind.ContentViewport, presenterRouting.RoutedEvents[0].HitTarget.Kind);
        Assert.Collection(
            frontendRouting.FrontendMessages,
            message => Assert.IsType<MachinaFrontendSurfaceResized>(message),
            message => Assert.IsType<MachinaFrontendCloseRequested>(message));
        Assert.Equal(new AurelianHostExtent(800, 600), translation.Lifecycle.HostExtent);
        Assert.Single(translation.CloseRequests);
        Assert.Equal(AurelianFrameLoopStopReason.CloseRequested, result.StopReason);
        Assert.Equal(0, result.FramesAttempted);
        Assert.True(engine.CloseRequestAccepted);
        Assert.Equal(AurelianEngineStatus.Stopped, engine.Status);
    }

    private static PresenterNavigationShellRenderResult CreateRender(
        int width = 1280,
        int height = 720,
        PresenterShellMode shellMode = PresenterShellMode.Wide)
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterProofOptions proofOptions = new();
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            model,
            proofOptions,
            PresenterNavigationExportOptions.DefaultShell);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return new PresenterNavigationRenderSession().Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            proofOptions,
            layout);
    }

    private static AurelianFrameInput Input(AurelianHostInputTranslation translation)
    {
        var output = new PlantOutputRef(0, 1, "close");
        var readiness = new PlantOutputReadiness(output, PlantOutputReadinessStatus.Pending, null);
        var target = new PresentationTargetRef(0, 0, 1);
        var facts = new CompositorPolicyFacts(
            new CompositorFrameFacts(1, [readiness], CompositorDiagnostics.Empty),
            new RequiredPlantOutputSet(1, CompositorPolicyKind.Passthrough, [output]),
            target,
            CompositorPolicyKind.Passthrough);
        return new AurelianFrameInput(
            new AurelianFrameId(1),
            facts,
            translation.Lifecycle,
            translation.CloseRequests.Single());
    }

    private sealed class SingleInputProvider : IAurelianFrameInputProvider
    {
        private readonly AurelianFrameInput input;

        public SingleInputProvider(AurelianFrameInput input)
        {
            this.input = input;
        }

        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
            AurelianFrameId frameId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<AurelianFrameInput?>(input);
        }
    }

    private sealed class NoOpCompositorMechanism : ICompositorMechanism
    {
        public Task<CompositorDispatchResult> DispatchAsync(
            CompositorDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompositorDispatchResult(
                CompositorDispatchStatus.Dispatched,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                []));
        }
    }
}
