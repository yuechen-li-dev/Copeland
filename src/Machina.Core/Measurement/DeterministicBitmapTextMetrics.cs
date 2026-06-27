using Machina.Core.Styling;

namespace Machina.Core.Measurement;

public static class DeterministicBitmapTextMetrics
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphGap = 1;

    public static IntrinsicSize Measure(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Measure(text, style.Size);
    }

    public static IntrinsicSize Measure(string text, TextSize size)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return new IntrinsicSize(0, 0);
        }

        var scale = GetScale(size);
        var advance = (GlyphWidth + GlyphGap) * scale;
        var width = (advance * text.Length) - (GlyphGap * scale);
        var height = GlyphHeight * scale;

        return new IntrinsicSize(width, height);
    }

    public static int GetScale(TextSize size)
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
