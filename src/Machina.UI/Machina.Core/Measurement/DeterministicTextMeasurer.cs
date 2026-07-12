using Machina.Core.Styling;

namespace Machina.Core.Measurement;

public sealed class DeterministicTextMeasurer : ITextMeasurer
{
    public static DeterministicTextMeasurer Instance { get; } = new();

    public IntrinsicSize MeasureText(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        return DeterministicBitmapTextMetrics.Measure(text, style);
    }
}
