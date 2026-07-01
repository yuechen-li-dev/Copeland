using Aurelian.Graphics.Vulkan.Presentation;

namespace Aurelian.VisibleTriangle;

internal sealed class VisibleTriangleWindowState
{
    private readonly List<string> diagnostics = new();

    public bool CloseRequested { get; private set; }

    public int PumpCount { get; private set; }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public void Pump(AurelianVulkanSurface? surface)
    {
        if (surface is null)
        {
            return;
        }

        PumpCount++;
        surface.PumpEvents();
        if (surface.IsCloseRequested && !CloseRequested)
        {
            CloseRequested = true;
            diagnostics.Add($"Window close requested after event pump {PumpCount}.");
        }
    }
}
