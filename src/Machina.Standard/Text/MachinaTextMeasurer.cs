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

        private static TextSize MapTextSize(MachinaTextVariant variant, MachinaTextRunStyle style)
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

            if (text.Length == 0)
            {
                return new MachinaTextSize(0, 0);
            }

            var fontSize = ResolveFontSize(style.Code ? MachinaTextVariant.Mono : variant);
            var width = (fontSize * ((6d * text.Length) - 1d)) / 7d;
            return new MachinaTextSize(width, fontSize);
        }

        private static double ResolveFontSize(MachinaTextVariant variant)
        {
            return variant switch
            {
                MachinaTextVariant.Body => 14,
                MachinaTextVariant.Label => 12,
                MachinaTextVariant.Caption => 11,
                MachinaTextVariant.Title => 18,
                MachinaTextVariant.Mono => 12,
                _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unsupported Machina text variant."),
            };
        }
    }
}
