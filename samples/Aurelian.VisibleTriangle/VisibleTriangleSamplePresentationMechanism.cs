using Aurelian.Core.Engine.Graphics;
using Aurelian.Graphics.Vulkan.Presentation;

namespace Aurelian.VisibleTriangle;

internal sealed class VisibleTriangleSamplePresentationMechanism : IPresentationMechanism
{
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly Queue<uint> pendingPresentImageIndices;
    private readonly VisibleTriangleWindowState windowState;
    private readonly AurelianVulkanSurface? surface;
    private readonly List<string> diagnostics = new();

    public VisibleTriangleSamplePresentationMechanism(
        AurelianVulkanSwapchain swapchain,
        Queue<uint> pendingPresentImageIndices,
        VisibleTriangleWindowState windowState,
        AurelianVulkanSurface? surface)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        ArgumentNullException.ThrowIfNull(pendingPresentImageIndices);
        ArgumentNullException.ThrowIfNull(windowState);
        this.swapchain = swapchain;
        this.pendingPresentImageIndices = pendingPresentImageIndices;
        this.windowState = windowState;
        this.surface = surface;
    }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public VulkanSwapchainPresentResult Present()
    {
        if (pendingPresentImageIndices.Count == 0)
        {
            throw new InvalidOperationException("Visible triangle presentation was requested before a frame acquired a swapchain image.");
        }

        uint imageIndex = pendingPresentImageIndices.Dequeue();
        VulkanSwapchainPresentResult result = swapchain.Present(imageIndex);
        windowState.Pump(surface);
        diagnostics.Add($"Presented acquired swapchain image {imageIndex} with status {result.Status}.");
        if (windowState.CloseRequested)
        {
            diagnostics.Add("Window close was requested after presentation event pump.");
        }
        return result;
    }

    public Task PresentAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        try
        {
            VulkanSwapchainPresentResult result = Present();
            return result.Status is VulkanSwapchainPresentStatus.Presented or VulkanSwapchainPresentStatus.Suboptimal
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException($"Swapchain present failed with status {result.Status}: {FormatDiagnostics(result)}"));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static string FormatDiagnostics(VulkanSwapchainPresentResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
