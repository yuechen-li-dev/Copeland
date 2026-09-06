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
public sealed class VulkanNativeSwapchainPresenter : IDisposable
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

    public VulkanNativeSwapchainPresenter(
        AurelianVulkanPlant plant,
        VulkanNativeFrameTarget target,
        AurelianVulkanSwapchain swapchain)
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
    }

    public string PresentMode => swapchain.Facts.SelectedPresentMode;

    public uint SwapchainImageCount => swapchain.Facts.ImageCount;

    public void Present(ulong frameId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
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
        VulkanCompositorResult dispatch = compositor.Dispatch(request, outputs, presentationTargets);
        if (!dispatch.Success)
        {
            throw new InvalidOperationException(Format("Native frame swapchain copy failed", dispatch.Diagnostics));
        }

        VulkanSwapchainPresentResult present = swapchain.Present(acquire.ImageIndex.Value);
        if (present.Status is not (VulkanSwapchainPresentStatus.Presented or VulkanSwapchainPresentStatus.Suboptimal))
        {
            throw new InvalidOperationException(Format("Swapchain presentation failed", present.Diagnostics));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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
