using Aurelian.Graphics.Vulkan.Native3D;
using Silk.NET.Vulkan;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Native2D;

/// <summary>
/// Copies a completed native frame target directly to an acquired swapchain image.
/// Pixel readback remains a separate, explicit capture operation.
/// </summary>
public sealed unsafe class VulkanNativeSwapchainPresenter : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanNativeFrameTarget target;
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanCompositorPassthrough compositor;
    private readonly VulkanPresentationTargetImageSet presentationTargets;
    private readonly bool[] presentedImages;
    private bool disposed;
    private readonly VulkanFrameInFlightSet? frames;
    private readonly Silk.NET.Vulkan.Semaphore[] renderFinished;

    public VulkanNativeSwapchainPresenter(
        AurelianVulkanPlant plant,
        VulkanNativeFrameTarget target,
        AurelianVulkanSwapchain swapchain, bool enableFramesInFlight = false)
    {
        this.plant = plant ?? throw new ArgumentNullException(nameof(plant));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.swapchain = swapchain ?? throw new ArgumentNullException(nameof(swapchain));

        if (target.Width != swapchain.Facts.Width || target.Height != swapchain.Facts.Height)
        {
            throw new ArgumentException("Native frame and swapchain extents must match.", nameof(swapchain));
        }

        if (!string.Equals(target.Format, swapchain.Facts.SelectedFormat, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Native frame format {target.Format} does not match swapchain format {swapchain.Facts.SelectedFormat}.",
                nameof(swapchain));
        }

        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
        compositor = new VulkanCompositorPassthrough(plant, commandPool, submitter);
        presentationTargets = swapchain.CreatePresentationTargetImageSet();
        presentedImages = new bool[swapchain.Images.Count];
        renderFinished = new Silk.NET.Vulkan.Semaphore[enableFramesInFlight ? swapchain.Images.Count : 0];
        if (enableFramesInFlight)
        {
            frames = new(fences.CommandListFence);
            SemaphoreCreateInfo info = new() { SType = StructureType.SemaphoreCreateInfo };
            for (int index = 0; index < renderFinished.Length; index++)
            {
                if (plant.Vk.CreateSemaphore(plant.Device, &info, null, out renderFinished[index]) != Result.Success)
                {
                    Dispose();
                    throw new InvalidOperationException("AUR-FRAME-002: Presentation semaphore allocation failed.");
                }
            }
        }
    }

    public string PresentMode => swapchain.Facts.SelectedPresentMode;

    public uint SwapchainImageCount => swapchain.Facts.ImageCount;

    public void Present(ulong frameId)
    {
        if (!TryPresent(frameId))
        {
            throw new InvalidOperationException(
                $"{VulkanPresentationDiagnosticCodes.SwapchainOutOfDate}: the swapchain is out of date; recreate it for the current surface extent.");
        }
    }

    /// <summary>
    /// Present like <see cref="Present"/>, but report an out-of-date swapchain (for example after a window resize
    /// raced the frame) as false instead of throwing, so the owner can rebuild surface resources and continue.
    /// </summary>
    public bool TryPresent(ulong frameId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        frames?.Begin();
        VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
        if (acquire.Status == VulkanSwapchainAcquireStatus.OutOfDate)
        {
            return false;
        }
        if (acquire.ImageIndex is null
            || acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal))
        {
            throw new InvalidOperationException(Format("Swapchain image acquisition failed", acquire.Diagnostics));
        }

        int imageIndex = checked((int)acquire.ImageIndex.Value);
        if (!presentedImages[imageIndex])
        {
            presentationTargets.Images[imageIndex].LayoutTracker.TryMarkCurrentLayout(
                0,
                0,
                VulkanResourceLayout.Undefined);
            presentedImages[imageIndex] = true;
        }

        PlantOutputRef outputRef = new(plant.Context.Id.Value, frameId, "native-frame");
        var outputs = new VulkanPlantOutputImageSet([
            new VulkanPlantOutputImage(outputRef, target.Texture),
        ]);
        var request = new CompositorDispatchRequest(
            frameId,
            CompositorPolicyKind.Passthrough,
            [outputRef],
            new PresentationTargetRef(plant.Context.Id.Value, acquire.ImageIndex.Value, frameId));
        Silk.NET.Vulkan.Semaphore? signal = frames is null ? null : renderFinished[imageIndex];
        VulkanCompositorResult dispatch = compositor.Dispatch(request, outputs, presentationTargets,
            waitForCompletion: frames is null, signal);
        if (!dispatch.Success)
        {
            throw new InvalidOperationException(Format("Native frame swapchain copy failed", dispatch.Diagnostics));
        }

        frames?.Submitted(dispatch.SignalFenceValue!.Value);
        VulkanSwapchainPresentResult present = swapchain.Present(acquire.ImageIndex.Value, signal);
        if (present.Status == VulkanSwapchainPresentStatus.OutOfDate)
        {
            return false;
        }
        if (present.Status is not (VulkanSwapchainPresentStatus.Presented or VulkanSwapchainPresentStatus.Suboptimal))
        {
            throw new InvalidOperationException(Format("Swapchain presentation failed", present.Diagnostics));
        }
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        // Presentation waits can outlive the copy's retirement timeline.
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        foreach (var semaphore in renderFinished)
        {
            if (semaphore.Handle != 0)
            {
                plant.Vk.DestroySemaphore(plant.Device, semaphore, null);
            }
        }
        compositor.Dispose();
        submitter.Dispose();
        commandPool.Dispose();
        fences.Dispose();
    }

    private static string Format(string prefix, IEnumerable<VulkanPresentationDiagnostic> diagnostics)
    {
        string details = string.Join("; ", diagnostics.Select(static item => $"{item.Code}: {item.Message}"));
        return details.Length == 0 ? prefix : $"{prefix}: {details}";
    }

    private static string Format(string prefix, IEnumerable<VulkanCompositorDiagnostic> diagnostics)
    {
        string details = string.Join("; ", diagnostics.Select(static item => $"{item.Code}: {item.Message}"));
        return details.Length == 0 ? prefix : $"{prefix}: {details}";
    }
}
