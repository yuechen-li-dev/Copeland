using Machina.Core.Measurement;
using Machina.Core.Styling;

namespace Machina.Standard.Text;

public interface IMachinaTextMeasurer
{
    MachinaTextSize Measure(string text, MachinaTextVariant variant, MachinaTextRunStyle style);
}

public static class MachinaTextMeasurers
{
    public static IMachinaTextMeasurer Deterministic { get; } = new DeterministicMachinaTextMeasurer();

    public static IMachinaTextMeasurer FromCore(ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        return new CoreMachinaTextMeasurerAdapter(textMeasurer);
    }

    private sealed class CoreMachinaTextMeasurerAdapter(ITextMeasurer textMeasurer) : IMachinaTextMeasurer
    {
        public MachinaTextSize Measure(string text, MachinaTextVariant variant, MachinaTextRunStyle style)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(style);

            var measured = textMeasurer.MeasureText(text, new TextStyle(Size: MapTextSize(variant, style)));
            return new MachinaTextSize(measured.Width, measured.Height);
        }

        internal static TextSize MapTextSize(MachinaTextVariant variant, MachinaTextRunStyle style)
        {
            var effectiveVariant = style.Code ? MachinaTextVariant.Mono : variant;

            return effectiveVariant switch
            {
                MachinaTextVariant.Title => TextSize.H1,
                MachinaTextVariant.Body => TextSize.Md,
                _ => TextSize.Sm,
            };
        }
    }

    private sealed class DeterministicMachinaTextMeasurer : IMachinaTextMeasurer
    {
        public MachinaTextSize Measure(string text, MachinaTextVariant variant, MachinaTextRunStyle style)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(style);
            var mappedSize = CoreMachinaTextMeasurerAdapter.MapTextSize(variant, style);
            var measured = DeterministicBitmapTextMetrics.Measure(text, mappedSize);
            return new MachinaTextSize(measured.Width, measured.Height);
        }
    }
}
