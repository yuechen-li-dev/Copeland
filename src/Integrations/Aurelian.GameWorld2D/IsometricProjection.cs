namespace Aurelian.GameWorld2D;

/// <summary>Presentation-only diamond projection; compose with Camera2D after projection.</summary>
public sealed class IsometricProjection
{
    public IsometricProjection(double tileWidth, double tileHeight)
    {
        World2DUnitScale.ValidatePositiveFinite(tileWidth, nameof(tileWidth));
        World2DUnitScale.ValidatePositiveFinite(tileHeight, nameof(tileHeight));
        TileWidth = tileWidth;
        TileHeight = tileHeight;
    }

    public double TileWidth { get; }
    public double TileHeight { get; }

    public WorldPoint2 Project(WorldPoint2 point)
    {
        Validate(point);
        return new((point.X - point.Y) * TileWidth / 2, (point.X + point.Y) * TileHeight / 2);
    }

    public WorldPoint2 Unproject(WorldPoint2 point)
    {
        Validate(point);
        return new(point.Y / TileHeight + point.X / TileWidth, point.Y / TileHeight - point.X / TileWidth);
    }

    private static void Validate(WorldPoint2 point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
    }
}
