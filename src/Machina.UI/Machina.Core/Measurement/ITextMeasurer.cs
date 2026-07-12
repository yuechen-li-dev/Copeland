using Machina.Core.Styling;

namespace Machina.Core.Measurement;

public interface ITextMeasurer
{
    IntrinsicSize MeasureText(string text, TextStyle style);
}
