using Aurelian.Core.Engine.Frames;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;

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
    private int suppliedFrames;

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

    public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
        AurelianFrameId frameId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<AurelianFrameInput?>(cancellationToken);
        }

        presenterBackend.PumpEvents();
        if (presenterBackend.CloseRequested)
        {
            diagnostics.Add($"Frame {frameId.Value} input stopped before acquire because the presenter backend requested close.");
            return ValueTask.FromResult<AurelianFrameInput?>(null);
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
        return ValueTask.FromResult<AurelianFrameInput?>(new AurelianFrameInput(frameId, facts));
    }

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
