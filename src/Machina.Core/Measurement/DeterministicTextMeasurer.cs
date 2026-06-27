using Machina.Core.Styling;

namespace Machina.Core.Measurement;

public sealed class DeterministicTextMeasurer : ITextMeasurer
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphGap = 1;

    public static DeterministicTextMeasurer Instance { get; } = new();

    public IntrinsicSize MeasureText(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        if (text.Length == 0)
        {
            return new IntrinsicSize(0, 0);
        }

        var scale = GetScale(style.Size);
        var advance = (GlyphWidth + GlyphGap) * scale;
        var width = (advance * text.Length) - (GlyphGap * scale);
        var height = GlyphHeight * scale;

        return new IntrinsicSize(width, height);
    }

    private static int GetScale(TextSize size)
    {
        return size switch
        {
            TextSize.Sm => 1,
            TextSize.Md => 2,
            TextSize.H1 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported text size."),
        };
    }
}
