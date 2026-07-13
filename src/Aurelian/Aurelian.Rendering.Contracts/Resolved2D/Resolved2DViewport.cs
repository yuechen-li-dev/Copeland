namespace Aurelian.Rendering.Contracts.Resolved2D;

/// <summary>
/// Pixel dimensions of a resolved 2D renderer plan.
/// </summary>
public readonly record struct Resolved2DViewport
{
    public Resolved2DViewport(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Viewport width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Viewport height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}
