using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DistanceFieldTextRenderOptions(
    int OutputWidth,
    int OutputHeight,
    FontFaceId Face,
    double EmSize,
    MachinaFontWeight Weight,
    MachinaFontSlant Slant,
    DistanceFieldKind Kind,
    int FieldWidth,
    int FieldHeight,
    double PixelRange,
    Rgba32 Foreground,
    Rgba32 Background,
    double X,
    double BaselineY,
    double Threshold = 0.5d,
    double SmoothingMultiplier = 1d,
    bool ShowBaselineGuide = false,
    Rgba32? BaselineGuideColor = null,
    bool FlipY = false,
    int PageWidth = 96,
    int PageHeight = 96,
    int PagePadding = 2,
    string EdgeColoring = "simple",
    double MiterLimit = 2d)
{
    public DistanceFieldTextRenderOptions Validate()
    {
        if (OutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputWidth));
        }

        if (OutputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputHeight));
        }

        if (!double.IsFinite(EmSize) || EmSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(EmSize));
        }

        if (FieldWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FieldWidth));
        }

        if (FieldHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FieldHeight));
        }

        if (!double.IsFinite(PixelRange) || PixelRange <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelRange));
        }

        if (!double.IsFinite(X))
        {
            throw new ArgumentOutOfRangeException(nameof(X));
        }

        if (!double.IsFinite(BaselineY))
        {
            throw new ArgumentOutOfRangeException(nameof(BaselineY));
        }

        if (!double.IsFinite(Threshold) || Threshold < 0d || Threshold > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        }

        if (!double.IsFinite(SmoothingMultiplier) || SmoothingMultiplier <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(SmoothingMultiplier));
        }

        if (ShowBaselineGuide && BaselineGuideColor is null)
        {
            throw new ArgumentException("Baseline guide color must be provided when the baseline guide is enabled.", nameof(BaselineGuideColor));
        }

        if (PageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PageWidth));
        }

        if (PageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PageHeight));
        }

        if (PagePadding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PagePadding));
        }

        if (string.IsNullOrWhiteSpace(EdgeColoring))
        {
            throw new ArgumentException("Edge coloring must not be empty.", nameof(EdgeColoring));
        }

        if (!double.IsFinite(MiterLimit) || MiterLimit <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MiterLimit));
        }

        return this;
    }
}
