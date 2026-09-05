using Aurelian.Graphics.Vulkan.Native2D;

namespace Aurelian.GameWorld2D;

public readonly record struct WorldPoint2(double X, double Y);

public readonly record struct WorldSize2(double Width, double Height);

public readonly record struct WorldRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public readonly record struct TilePoint2(int X, int Y);

public readonly record struct TileRect(int X, int Y, int Width, int Height);

public readonly record struct PixelPoint2(double X, double Y);

public readonly record struct PixelRect(double X, double Y, double Width, double Height);

public readonly record struct UvRect(double U0, double V0, double U1, double V1);

public readonly record struct SpriteAssetId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct WorldPresentationId(string Value)
{
    public override string ToString() => Value;
}

public enum WorldSpriteLayer
{
    Ground = 0,
    World = 100,
    Actors = 200,
    Foreground = 300,
}

public enum SpriteSampling
{
    Nearest,
    Linear,
}

public sealed record World2DUnitScale(double TileSizeWorld, double PixelsPerWorldUnit)
{
    public void Validate()
    {
        ValidatePositiveFinite(TileSizeWorld, nameof(TileSizeWorld));
        ValidatePositiveFinite(PixelsPerWorldUnit, nameof(PixelsPerWorldUnit));
    }

    public WorldPoint2 TileToWorld(TilePoint2 point)
    {
        Validate();
        return new WorldPoint2(point.X * TileSizeWorld, point.Y * TileSizeWorld);
    }

    public WorldRect TileToWorld(TileRect rect)
    {
        Validate();
        return new WorldRect(
            rect.X * TileSizeWorld,
            rect.Y * TileSizeWorld,
            rect.Width * TileSizeWorld,
            rect.Height * TileSizeWorld);
    }

    internal static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be positive and finite.");
        }
    }
}

public sealed record Camera2DSnapshot(
    WorldPoint2 Position,
    PixelRect Viewport,
    double Zoom,
    WorldRect Bounds);

public sealed class Camera2D
{
    private WorldPoint2 position;
    private PixelRect viewport;
    private double zoom;
    private WorldRect bounds;

    public Camera2D(WorldPoint2 position, PixelRect viewport, double zoom, WorldRect bounds)
    {
        ValidateViewport(viewport);
        ValidateBounds(bounds);
        World2DUnitScale.ValidatePositiveFinite(zoom, nameof(zoom));
        this.position = position;
        this.viewport = viewport;
        this.zoom = zoom;
        this.bounds = bounds;
    }

    public WorldPoint2 Position => position;

    public PixelRect Viewport => viewport;

    public double Zoom
    {
        get => zoom;
        set
        {
            World2DUnitScale.ValidatePositiveFinite(value, nameof(value));
            zoom = value;
        }
    }

    public WorldRect Bounds => bounds;

    public void Follow(WorldPoint2 target, World2DUnitScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        scale.Validate();
        double visibleWidth = viewport.Width / (scale.PixelsPerWorldUnit * zoom);
        double visibleHeight = viewport.Height / (scale.PixelsPerWorldUnit * zoom);
        position = new WorldPoint2(target.X - visibleWidth / 2, target.Y - visibleHeight / 2);
        Clamp(scale);
    }

    public void SetZoom(double zoom, World2DUnitScale scale)
    {
        Zoom = zoom;
        Clamp(scale);
    }

    public void SnapTo(WorldPoint2 position, World2DUnitScale scale)
    {
        this.position = position;
        Clamp(scale);
    }

    public void Clamp(World2DUnitScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        scale.Validate();
        double visibleWidth = viewport.Width / (scale.PixelsPerWorldUnit * zoom);
        double visibleHeight = viewport.Height / (scale.PixelsPerWorldUnit * zoom);
        double maximumX = Math.Max(bounds.X, bounds.Right - visibleWidth);
        double maximumY = Math.Max(bounds.Y, bounds.Bottom - visibleHeight);
        position = new WorldPoint2(
            Math.Clamp(position.X, bounds.X, maximumX),
            Math.Clamp(position.Y, bounds.Y, maximumY));
    }

    public void Resize(PixelRect viewport, World2DUnitScale scale)
    {
        ValidateViewport(viewport);
        this.viewport = viewport;
        Clamp(scale);
    }

    public void SetBounds(WorldRect bounds, World2DUnitScale scale)
    {
        ValidateBounds(bounds);
        this.bounds = bounds;
        Clamp(scale);
    }

    public Camera2DSnapshot Snapshot() => new(position, viewport, zoom, bounds);

    private static void ValidateViewport(PixelRect viewport)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "Viewport origin must be finite.");
        }
        World2DUnitScale.ValidatePositiveFinite(viewport.Width, nameof(viewport.Width));
        World2DUnitScale.ValidatePositiveFinite(viewport.Height, nameof(viewport.Height));
    }

    private static void ValidateBounds(WorldRect bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Camera bounds origin must be finite.");
        }
        World2DUnitScale.ValidatePositiveFinite(bounds.Width, nameof(bounds.Width));
        World2DUnitScale.ValidatePositiveFinite(bounds.Height, nameof(bounds.Height));
    }
}

public sealed record SpriteFrameMetadata(
    string FrameId,
    int AtlasX,
    int AtlasY,
    int Width,
    int Height,
    double PivotX,
    double PivotY,
    int OffsetX,
    int OffsetY,
    double Scale,
    UvRect Uv);

public sealed record WorldSprite(
    WorldPresentationId StableId,
    WorldPoint2 Anchor,
    SpriteAssetId AssetId,
    string SpriteId,
    string? ClipId,
    TimeSpan Elapsed,
    bool Restart,
    double Scale,
    Native2DTint Tint,
    WorldSpriteLayer Layer,
    double FeetY,
    bool SnapToIntegerPixels = true);

public sealed record OrderedWorldSprite(
    WorldSprite Source,
    SpriteFrameMetadata Frame,
    NativeQuadSubmission Submission);

public sealed record WorldPresentationSnapshot(IReadOnlyList<WorldSprite> Sprites);
