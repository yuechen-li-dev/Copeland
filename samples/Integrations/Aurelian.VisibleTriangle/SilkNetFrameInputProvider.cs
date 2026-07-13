using Aurelian.Core.Engine.Commands;
using Aurelian.Core.Engine.Frames;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Machina.Presentation.Input;
using Machina.Runtime.Input;

namespace Aurelian.VisibleTriangle;

internal sealed class SilkNetFrameInputProvider : IAurelianFrameInputProvider
{
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly uint plantId;
    private readonly string outputImageId;
    private readonly Queue<uint> pendingPresentImageIndices;
    private readonly IPresenterBackend presenterBackend;
    private readonly Dictionary<AurelianFrameId, VisibleTriangleFrameState> frames = new();
    private readonly int maxFrames;
    private readonly List<string> diagnostics = [];
    private readonly VisibleTriangleHostInputCollector inputCollector = new();
    private int suppliedFrames;
    private bool closeCallbackRecorded;

    public SilkNetFrameInputProvider(
        AurelianVulkanSwapchain swapchain,
        uint plantId,
        string outputImageId,
        Queue<uint> pendingPresentImageIndices,
        IPresenterBackend presenterBackend,
        int maxFrames)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        ArgumentNullException.ThrowIfNull(outputImageId);
        ArgumentNullException.ThrowIfNull(pendingPresentImageIndices);
        ArgumentNullException.ThrowIfNull(presenterBackend);
        if (maxFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames), "Silk.NET presenter frame input provider must supply at least one frame input.");
        }

        this.swapchain = swapchain;
        this.plantId = plantId;
        this.outputImageId = outputImageId;
        this.pendingPresentImageIndices = pendingPresentImageIndices;
        this.presenterBackend = presenterBackend;
        this.maxFrames = maxFrames;
    }

    public IReadOnlyDictionary<AurelianFrameId, VisibleTriangleFrameState> Frames => frames;

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public int MaxFrames => maxFrames;

    public UiInputBatch? LastNormalizedInput { get; private set; }

    public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
        AurelianFrameId frameId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<AurelianFrameInput?>(cancellationToken);
        }

        presenterBackend.PumpEvents();
        HostIterationInput hostInput = CollectHostIterationInput();
        if (hostInput.CloseRequest is not null)
        {
            diagnostics.Add($"Frame {frameId.Value} carries an explicit Aurelian close request before acquire.");
            return ValueTask.FromResult<AurelianFrameInput?>(new AurelianFrameInput(
                frameId,
                Facts(frameId.Value, new PlantOutputRef(plantId, frameId.Value, outputImageId), new PresentationTargetRef(plantId, 0, frameId.Value), PlantOutputReadinessStatus.Pending),
                hostInput.Lifecycle,
                hostInput.CloseRequest));
        }

        if (suppliedFrames >= maxFrames)
        {
            return ValueTask.FromResult<AurelianFrameInput?>(null);
        }

        VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
        if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
        {
            diagnostics.Add($"Frame {frameId.Value} swapchain acquire stopped the presenter slice with status {acquire.Status}: {FormatDiagnostics(acquire)}");
            return ValueTask.FromResult<AurelianFrameInput?>(null);
        }

        PlantOutputRef outputRef = new(plantId, frameId.Value, outputImageId);
        PresentationTargetRef target = new(plantId, acquire.ImageIndex.Value, frameId.Value);
        var state = new VisibleTriangleFrameState(frameId, acquire.ImageIndex.Value, outputRef, target);
        frames.Add(frameId, state);
        pendingPresentImageIndices.Enqueue(acquire.ImageIndex.Value);
        suppliedFrames++;

        CompositorPolicyFacts facts = Facts(frameId.Value, outputRef, target, PlantOutputReadinessStatus.Ready);
        return ValueTask.FromResult<AurelianFrameInput?>(new AurelianFrameInput(frameId, facts, hostInput.Lifecycle));
    }

    private HostIterationInput CollectHostIterationInput()
    {
        if (LastNormalizedInput is null)
        {
            inputCollector.RecordSurfaceResize(swapchain.Facts.Width, swapchain.Facts.Height);
        }

        if (presenterBackend.CloseRequested && !closeCallbackRecorded)
        {
            inputCollector.RecordCloseRequest();
            closeCallbackRecorded = true;
        }

        UiInputBatch inputBatch = inputCollector.Publish();
        LastNormalizedInput = inputBatch;
        MachinaFrontendInputRoutingResult frontendRouting = MachinaFrontendInputRouter.Route(inputBatch);
        AurelianHostInputTranslation translation = AurelianHostInputTranslator.Translate(
            frontendRouting.FrontendMessages);
        return new HostIterationInput(
            translation.Lifecycle,
            translation.CloseRequests.FirstOrDefault());
    }

    private sealed record HostIterationInput(
        AurelianHostLifecycleInput Lifecycle,
        AurelianCloseRequest? CloseRequest);

    private static CompositorPolicyFacts Facts(
        ulong frameId,
        PlantOutputRef output,
        PresentationTargetRef target,
        PlantOutputReadinessStatus status)
    {
        var readiness = new PlantOutputReadiness(
            output,
            status,
            CompletedFenceValue: status is PlantOutputReadinessStatus.Ready or PlantOutputReadinessStatus.Reused ? frameId : null);
        var frameFacts = new CompositorFrameFacts(frameId, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(frameId, CompositorPolicyKind.Passthrough, [output]);
        return new CompositorPolicyFacts(frameFacts, required, target, CompositorPolicyKind.Passthrough);
    }

    private static string FormatDiagnostics(VulkanSwapchainAcquireResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
