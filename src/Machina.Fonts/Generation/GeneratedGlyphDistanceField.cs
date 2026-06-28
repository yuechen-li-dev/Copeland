namespace Machina.Fonts.Generation;

public sealed record GeneratedGlyphDistanceField
{
    public GeneratedGlyphDistanceField(
        GlyphKey key,
        GlyphMetrics metrics,
        int width,
        int height,
        DistanceFieldKind kind,
        int channelCount,
        ReadOnlyMemory<float> data,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null entries.", nameof(diagnostics));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        int expectedChannelCount = FakeDistanceFieldValidation.GetChannelCount(kind);
        if (channelCount != expectedChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount), $"Channel count must be {expectedChannelCount} for {kind}.");
        }

        int expectedLength = checked(width * height * channelCount);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException($"Data length must be {expectedLength}.", nameof(data));
        }

        Key = key;
        Metrics = metrics;
        Width = width;
        Height = height;
        Kind = kind;
        ChannelCount = channelCount;
        Data = data;
        Diagnostics = [.. diagnostics];
    }

    public GlyphKey Key { get; }

    public GlyphMetrics Metrics { get; }

    public int Width { get; }

    public int Height { get; }

    public DistanceFieldKind Kind { get; }

    public int ChannelCount { get; }

    public ReadOnlyMemory<float> Data { get; }

    public IReadOnlyList<FontGenerationDiagnostic> Diagnostics { get; }
}

internal static class FakeDistanceFieldValidation
{
    public static int GetChannelCount(DistanceFieldKind kind)
    {
        return kind switch
        {
            DistanceFieldKind.Sdf => 1,
            DistanceFieldKind.Psdf => 1,
            DistanceFieldKind.Msdf => 3,
            DistanceFieldKind.Mtsdf => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported distance field kind."),
        };
    }
}
