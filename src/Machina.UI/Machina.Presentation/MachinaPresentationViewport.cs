namespace Machina.Presentation;

public readonly record struct MachinaPresentationViewport
{
    public MachinaPresentationViewport(int width, int height)
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
