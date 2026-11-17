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

/// <summary>Result of a non-blocking <see cref="VulkanNativeSwapchainPresenter.PollImage"/>.</summary>
public enum NativePresentReadiness
{
    /// <summary>An image is held; the next <see cref="VulkanNativeSwapchainPresenter.TryPresent"/> uses it without waiting.</summary>
    Ready,
    /// <summary>The presentation engine has no free image yet (composition throttled). Skip this frame; nothing blocked.</summary>
    NotReady,
    /// <summary>The swapchain no longer matches the surface; rebuild it.</summary>
    OutOfDate,
}

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
    public bool CaptureTimings { get; set; }
    public NativePresentTimings? LastTimings { get; private set; }
    private readonly VulkanFrameInFlightSet? frames;
    private readonly Silk.NET.Vulkan.Semaphore[] renderFinished;

    // Explicit acquisition state. A swapchain image is either not held, acquired with its fence still pending
    // (the presentation engine has not released it), or acquired and usable. The fence persists across polls:
    // while it is pending it is owned by the presentation engine and must be neither reset nor destroyed.
    private enum AcquireState { NoImage, ImagePending, ImageReady }
    private AcquireState acquireState;
    private uint heldImage;
    private Fence acquireFence;

    /// <summary>Polls that found no free image or a still-pending one (each is a skipped, non-blocking frame).</summary>
    public long ThrottledPolls { get; private set; }

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

    /// <summary>
    /// Non-blocking readiness check. Acquires at most one image (zero timeout) and observes its fence without waiting.
    /// Hosts call this before rendering a frame; on <see cref="NativePresentReadiness.NotReady"/> they skip the frame
    /// and keep their loop (input, media clocks, seeks) running instead of blocking on the compositor.
    /// </summary>
    public NativePresentReadiness PollImage()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (acquireState == AcquireState.NoImage)
        {
            if (acquireFence.Handle == 0)
            {
                FenceCreateInfo info = new() { SType = StructureType.FenceCreateInfo };
                if (plant.Vk.CreateFence(plant.Device, &info, null, out acquireFence) != Result.Success)
                {
                    throw new InvalidOperationException("AUR-FRAME-003: Acquire fence allocation failed.");
                }
            }
            VulkanSwapchainAcquireResult acquire = swapchain.TryAcquireNextImage(acquireFence);
            switch (acquire.Status)
            {
                case VulkanSwapchainAcquireStatus.NotReady:
                    ThrottledPolls++;
                    return NativePresentReadiness.NotReady;
                case VulkanSwapchainAcquireStatus.OutOfDate:
                    return NativePresentReadiness.OutOfDate;
                case VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal when acquire.ImageIndex is { } index:
                    heldImage = index;
                    acquireState = AcquireState.ImagePending;
                    break;
                default:
                    throw new InvalidOperationException(Format("Swapchain image acquisition failed", acquire.Diagnostics));
            }
        }
        if (acquireState == AcquireState.ImagePending)
        {
            Result status = plant.Vk.GetFenceStatus(plant.Device, acquireFence);
            if (status == Result.NotReady)
            {
                ThrottledPolls++;
                return NativePresentReadiness.NotReady;
            }
            if (status != Result.Success)
            {
                throw new InvalidOperationException($"AUR-FRAME-004: Acquire fence status failed with {status}.");
            }
            Fence signaled = acquireFence;
            _ = plant.Vk.ResetFences(plant.Device, 1, &signaled);
            acquireState = AcquireState.ImageReady;
        }
        return NativePresentReadiness.Ready;
    }

    /// <summary>Takes the held image, waiting for a still-pending fence only on the legacy blocking path.</summary>
    private uint? TakeHeldImage()
    {
        if (acquireState == AcquireState.NoImage) return null;
        if (acquireState == AcquireState.ImagePending)
        {
            Fence pending = acquireFence;
            if (plant.Vk.WaitForFences(plant.Device, 1, &pending, true, ulong.MaxValue) != Result.Success)
            {
                throw new InvalidOperationException("AUR-FRAME-004: Waiting for the held swapchain image failed.");
            }
            _ = plant.Vk.ResetFences(plant.Device, 1, &pending);
        }
        acquireState = AcquireState.NoImage;
        return heldImage;
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
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        // A host that polled first presents its held image without waiting; otherwise acquire as before (blocking).
        VulkanSwapchainAcquireResult acquire = TakeHeldImage() is { } held
            ? new VulkanSwapchainAcquireResult(VulkanSwapchainAcquireStatus.Acquired, held, [])
            : swapchain.AcquireNextImage();
        long acquired = System.Diagnostics.Stopwatch.GetTimestamp();
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
        long copied = System.Diagnostics.Stopwatch.GetTimestamp();
        VulkanSwapchainPresentResult present = swapchain.Present(acquire.ImageIndex.Value, signal);
        if (CaptureTimings)
        {
            LastTimings = new(System.Diagnostics.Stopwatch.GetElapsedTime(started, acquired).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(acquired, copied).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(copied).TotalMilliseconds);
        }
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
        if (acquireFence.Handle != 0)
        {
            // A pending acquire fence still belongs to the presentation engine: observe it before destroying.
            // If it never signals (lost surface), leak the handle rather than destroy a fence in use.
            Fence fence = acquireFence;
            bool idle = acquireState != AcquireState.ImagePending
                || plant.Vk.WaitForFences(plant.Device, 1, &fence, true, 1_000_000_000) == Result.Success;
            if (idle)
            {
                plant.Vk.DestroyFence(plant.Device, fence, null);
            }
            acquireFence = default;
            acquireState = AcquireState.NoImage;
        }
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
