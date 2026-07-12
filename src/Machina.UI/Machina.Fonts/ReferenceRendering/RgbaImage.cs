namespace Machina.Fonts.ReferenceRendering;

public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
        : this(width, height, new Rgba32[checked(width * height)])
    {
    }

    public RgbaImage(int width, int height, Rgba32[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(pixels);

        int expectedLength = checked(width * height);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException($"Pixel array length must be {expectedLength}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public Rgba32[] Pixels { get; }

    public Rgba32 GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        return Pixels[(y * Width) + x];
    }

    public void SetPixel(int x, int y, Rgba32 color)
    {
        ValidateCoordinates(x, y);
        Pixels[(y * Width) + x] = color;
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
