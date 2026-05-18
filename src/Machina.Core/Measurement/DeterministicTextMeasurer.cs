using Machina.Core.Styling;

namespace Machina.Core.Measurement;

public sealed class DeterministicTextMeasurer : ITextMeasurer
{
    public static DeterministicTextMeasurer Instance { get; } = new();

    public IntrinsicSize MeasureText(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        var metrics = GetMetrics(style.Size);
        return new IntrinsicSize(
            text.Length * metrics.CharacterWidth,
            metrics.Height);
    }

    private static TextMetrics GetMetrics(TextSize size)
    {
        return size switch
        {
            TextSize.Sm => new TextMetrics(CharacterWidth: 7, Height: 16),
            TextSize.Md => new TextMetrics(CharacterWidth: 8, Height: 20),
            TextSize.H1 => new TextMetrics(CharacterWidth: 14, Height: 36),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported text size."),
        };
    }

    private readonly record struct TextMetrics(
        double CharacterWidth,
        double Height);
}
