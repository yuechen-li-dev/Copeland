using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Left >= Right || Top >= Bottom;

    public static PixelBounds FromSurface(int width, int height)
    {
        return new PixelBounds(0, 0, width, height);
    }

    public static PixelBounds FromRectangle(Resolved2DRectangle rectangle)
    {
        if (rectangle.IsEmptyOrNegative)
        {
            return new PixelBounds(0, 0, 0, 0);
        }

        return new PixelBounds(
            (int)Math.Floor(rectangle.X),
            (int)Math.Floor(rectangle.Y),
            (int)Math.Ceiling(rectangle.X + rectangle.Width),
            (int)Math.Ceiling(rectangle.Y + rectangle.Height));
    }

    public static PixelBounds Intersect(PixelBounds left, PixelBounds right)
    {
        var result = new PixelBounds(
            Math.Max(left.Left, right.Left),
            Math.Max(left.Top, right.Top),
            Math.Min(left.Right, right.Right),
            Math.Min(left.Bottom, right.Bottom));

        return result.IsEmpty ? new PixelBounds(0, 0, 0, 0) : result;
    }
}
