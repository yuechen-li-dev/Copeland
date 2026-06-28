namespace Machina.Fonts.Generation;

public sealed record GeneratedFieldAtlasPage
{
    public GeneratedFieldAtlasPage(
        int index,
        int width,
        int height,
        int channelCount,
        float[] data,
        IReadOnlyList<GlyphAtlasEntry> entries)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entries);

        int expectedLength = checked(width * height * channelCount);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException($"Page data length must be {expectedLength}.", nameof(data));
        }

        if (entries.Any(static entry => entry is null))
        {
            throw new ArgumentException("Entries must not contain null values.", nameof(entries));
        }

        Index = index;
        Width = width;
        Height = height;
        ChannelCount = channelCount;
        Data = data.ToArray();
        Entries = entries.ToArray();
    }

    public int Index { get; }

    public int Width { get; }

    public int Height { get; }

    public int ChannelCount { get; }

    public float[] Data { get; }

    public IReadOnlyList<GlyphAtlasEntry> Entries { get; }
}
