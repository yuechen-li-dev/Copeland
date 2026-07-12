namespace Machina.Fonts;

public sealed record FontAtlasPage
{
    public FontAtlasPage(int index, string imagePath, int width, int height, string? contentHash)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (string.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException("Image path must not be empty.", nameof(imagePath));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Index = index;
        ImagePath = imagePath;
        Width = width;
        Height = height;
        ContentHash = contentHash;
    }

    public int Index { get; }
    public string ImagePath { get; }
    public int Width { get; }
    public int Height { get; }
    public string? ContentHash { get; }
}
