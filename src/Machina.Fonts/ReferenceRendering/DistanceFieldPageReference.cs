using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DistanceFieldPageReference
{
    public DistanceFieldPageReference(
        string sourcePath,
        DistanceFieldKind kind,
        int pageIndex,
        int width,
        int height,
        int channelCount,
        float[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(data);

        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        int expectedChannelCount = kind switch
        {
            DistanceFieldKind.Sdf => 1,
            DistanceFieldKind.Psdf => 1,
            DistanceFieldKind.Msdf => 3,
            DistanceFieldKind.Mtsdf => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (channelCount != expectedChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount), $"Channel count must be {expectedChannelCount} for {kind}.");
        }

        int expectedLength = checked(width * height * channelCount);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException($"Page data length must be {expectedLength}.", nameof(data));
        }

        SourcePath = sourcePath;
        Kind = kind;
        PageIndex = pageIndex;
        Width = width;
        Height = height;
        ChannelCount = channelCount;
        Data = data.ToArray();
    }

    public string SourcePath { get; }

    public DistanceFieldKind Kind { get; }

    public int PageIndex { get; }

    public int Width { get; }

    public int Height { get; }

    public int ChannelCount { get; }

    public float[] Data { get; }
}
