using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;
using Machina.Renderer.Raster.Text;

namespace Machina.Renderer.Raster.Dominatus;

public sealed class RasterRenderRecorder
{
    private readonly record struct PixelClip(int Left, int Top, int Right, int Bottom)
    {
        public bool IsEmpty => Left >= Right || Top >= Bottom;
    }

    private readonly List<RasterFrame> _completedFrames = new();
    private readonly Stack<PixelClip> _clipStack = new();
    private RasterSurface? _activeSurface;
    private PixelClip _currentClip;

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
        _clipStack.Clear();
        _currentClip = new PixelClip(0, 0, width, height);
    }

    public void FillRect(string id, Rect rect, ColorToken color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot fill rectangle without an active frame.");
        }

        var rgba = Rgba32.FromRgba(color.Rgba);
        if (_currentClip.IsEmpty)
        {
            return;
        }

        Rasterizer.FillRect(_activeSurface, rect, rgba, ToRect(_currentClip));
    }

    public void StrokeRect(string id, Rect rect, ColorToken color, double thickness)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot stroke rectangle without an active frame.");
        }

        var rgba = Rgba32.FromRgba(color.Rgba);
        if (_currentClip.IsEmpty)
        {
            return;
        }

        Rasterizer.StrokeRect(_activeSurface, rect, rgba, thickness, ToRect(_currentClip));
    }


    public void DrawText(string id, Rect rect, string text, TextStyle style, ITextRasterizer textRasterizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(textRasterizer);

        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot draw text without an active frame.");
        }

        var rgba = style.Color is null
            ? Rgba32.White
            : Rgba32.FromRgba(style.Color.Value.Rgba);

        if (_currentClip.IsEmpty)
        {
            return;
        }

        textRasterizer.DrawText(_activeSurface, rect, text, style, rgba, ToRect(_currentClip));
    }

    public void PushClip(string id, Rect rect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot push clip without an active frame.");
        }

        if (!IsFinite(rect.X) || !IsFinite(rect.Y) || !IsFinite(rect.Width) || !IsFinite(rect.Height))
        {
            throw new ArgumentException("Clip rectangle must contain finite values.", nameof(rect));
        }

        _clipStack.Push(_currentClip);

        var pushedClip = ToPixelClip(rect);
        _currentClip = Intersect(_currentClip, pushedClip);
    }

    public void PopClip()
    {
        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot pop clip without an active frame.");
        }

        if (_clipStack.Count == 0)
        {
            throw new InvalidOperationException("Cannot pop clip because the clip stack is empty.");
        }

        _currentClip = _clipStack.Pop();
    }

    public void EndFrame()
    {
        if (_activeSurface is null)
        {
            throw new InvalidOperationException("Cannot end frame when no frame is active.");
        }
        if (_clipStack.Count > 0)
        {
            throw new InvalidOperationException("Cannot end frame while clip stack is not balanced.");
        }

        var frame = new RasterFrame(_activeSurface.Width, _activeSurface.Height, _activeSurface);
        _completedFrames.Add(frame);
        _activeSurface = null;
        _clipStack.Clear();
    }

    private static Rect ToRect(PixelClip clip)
    {
        return new Rect(clip.Left, clip.Top, clip.Right - clip.Left, clip.Bottom - clip.Top);
    }

    private static PixelClip ToPixelClip(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return new PixelClip(0, 0, 0, 0);
        }

        var left = (int)Math.Floor(rect.X);
        var top = (int)Math.Floor(rect.Y);
        var right = (int)Math.Ceiling(rect.X + rect.Width);
        var bottom = (int)Math.Ceiling(rect.Y + rect.Height);
        return new PixelClip(left, top, right, bottom);
    }

    private static PixelClip Intersect(PixelClip a, PixelClip b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);

        if (left >= right || top >= bottom)
        {
            return new PixelClip(0, 0, 0, 0);
        }

        return new PixelClip(left, top, right, bottom);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
