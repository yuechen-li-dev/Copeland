using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Dominatus;

public sealed class RasterRenderRecorder
{
    private readonly List<RasterFrame> _completedFrames = new();
    private RasterSurface? _activeSurface;

    public bool HasActiveFrame => _activeSurface is not null;

    public IReadOnlyList<RasterFrame> CompletedFrames => _completedFrames;

    public RasterFrame? LastFrame => _completedFrames.Count == 0 ? null : _completedFrames[^1];

    public void BeginFrame(int width, int height)
    {
        if (HasActiveFrame)
        {
            throw new InvalidOperationException("Cannot begin frame while another frame is active.");
        }

        if (width <= 0)
        {
            throw new InvalidOperationException("Frame width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new InvalidOperationException("Frame height must be greater than zero.");
        }

        _activeSurface = new RasterSurface(width, height);
        Rasterizer.Clear(_activeSurface, Rgba32.Transparent);
    }

    public void FillRect(string id, Rect rect, ColorToken color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot fill rectangle without an active frame.");
        }

        var rgba = Rgba32.FromRgba(color.Rgba);
        Rasterizer.FillRect(_activeSurface, rect, rgba);
    }

    public void EndFrame()
    {
        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot end frame when no frame is active.");
        }

        var frame = new RasterFrame(_activeSurface.Width, _activeSurface.Height, _activeSurface);
        _completedFrames.Add(frame);
        _activeSurface = null;
    }
}
