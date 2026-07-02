using Aurelian.Core.Engine.Graphics;
using Aurelian.Graphics.Vulkan.Presentation;

namespace Aurelian.Core.Graphics.Vulkan.Presentation;

public sealed class VulkanPresentationMechanism : IPresentationMechanism
{
    private readonly AurelianVulkanSwapchain swapchain;
    private readonly Queue<uint> pendingPresentImageIndices;
    private readonly Action? afterPresent;
    private readonly List<string> diagnostics = [];

    public VulkanPresentationMechanism(
        AurelianVulkanSwapchain swapchain,
        Queue<uint> pendingPresentImageIndices,
        Action? afterPresent = null)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        ArgumentNullException.ThrowIfNull(pendingPresentImageIndices);
        this.swapchain = swapchain;
        this.pendingPresentImageIndices = pendingPresentImageIndices;
        this.afterPresent = afterPresent;
    }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public Task PresentAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        try
        {
            if (pendingPresentImageIndices.Count == 0)
            {
                return Task.FromException(new InvalidOperationException("Vulkan presentation was requested before a frame acquired a swapchain image."));
            }

            uint imageIndex = pendingPresentImageIndices.Dequeue();
            VulkanSwapchainPresentResult result = swapchain.Present(imageIndex);
            afterPresent?.Invoke();

            diagnostics.Add($"Presented acquired swapchain image {imageIndex} with status {result.Status}.");
            return result.Status is VulkanSwapchainPresentStatus.Presented or VulkanSwapchainPresentStatus.Suboptimal
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException($"Swapchain present failed with status {result.Status}: {FormatDiagnostics(result)}"));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static string FormatDiagnostics(VulkanSwapchainPresentResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
