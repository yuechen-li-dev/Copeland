using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public static class CpuDistanceFieldTextRenderer
{
    public static RgbaImage RenderText(
        FontAtlasSnapshot snapshot,
        IReadOnlyDictionary<int, DistanceFieldPageReference> pages,
        DistanceFieldTextLayoutResult layout,
        DistanceFieldTextRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (layout.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            throw new InvalidOperationException("Cannot render text layout with error diagnostics.");
        }

        RgbaImage image = new(options.OutputWidth, options.OutputHeight);
        CpuDistanceFieldGlyphRenderer.Fill(image, options.Background);

        DistanceFieldRenderOptions glyphOptions = new(
            options.FieldWidth,
            options.FieldHeight,
            options.Foreground,
            Rgba32.Transparent,
            options.PixelRange,
            0.5d,
            options.FlipY);

        foreach (DistanceFieldGlyphPlacement placement in layout.Placements)
        {
            if (placement.IsWhitespace)
            {
                continue;
            }

            if (!snapshot.Glyphs.TryGetValue(placement.Key, out GlyphAtlasEntry? entry))
            {
                throw new InvalidOperationException($"Missing atlas entry for glyph U+{placement.Key.Codepoint:X4}.");
            }

            if (!pages.TryGetValue(entry.PageIndex, out DistanceFieldPageReference? page))
            {
                throw new InvalidOperationException($"Missing page {entry.PageIndex} for glyph U+{placement.Key.Codepoint:X4}.");
            }

            int outputWidth = Math.Max(1, RoundToInt(entry.Width * placement.Scale));
            int outputHeight = Math.Max(1, RoundToInt(entry.Height * placement.Scale));
            FieldCanvasPlacement fieldPlacement = ComputeFieldCanvasPlacement(placement, entry, options, outputWidth, outputHeight);
            int destinationX = RoundToInt((placement.X + (placement.Metrics.BearingX * placement.Scale)) - fieldPlacement.LeftPadding);
            int destinationY = RoundToInt((placement.BaselineY - (placement.Metrics.BearingY * placement.Scale)) - fieldPlacement.TopPadding);

            CpuDistanceFieldGlyphRenderer.RenderGlyphInto(
                image,
                page,
                entry,
                destinationX,
                destinationY,
                outputWidth,
                outputHeight,
                glyphOptions);
        }

        return image;
    }

    private static FieldCanvasPlacement ComputeFieldCanvasPlacement(
        DistanceFieldGlyphPlacement placement,
        GlyphAtlasEntry entry,
        DistanceFieldTextRenderOptions options,
        int outputWidth,
        int outputHeight)
    {
        double metricsWidth = placement.Metrics.Width * placement.Scale;
        double metricsHeight = placement.Metrics.Height * placement.Scale;

        if (metricsWidth <= 0d || metricsHeight <= 0d)
        {
            return new FieldCanvasPlacement(outputWidth * 0.5d, outputHeight * 0.5d);
        }

        double scaleX = outputWidth / (double)entry.Width;
        double scaleY = outputHeight / (double)entry.Height;
        double scaledPixelRangeX = options.PixelRange * scaleX;
        double scaledPixelRangeY = options.PixelRange * scaleY;
        double drawableWidth = Math.Max(0.0001d, outputWidth - (scaledPixelRangeX * 2d));
        double drawableHeight = Math.Max(0.0001d, outputHeight - (scaledPixelRangeY * 2d));
        double fitScale = Math.Min(drawableWidth / metricsWidth, drawableHeight / metricsHeight);

        if (!double.IsFinite(fitScale) || fitScale <= 0d)
        {
            return new FieldCanvasPlacement(0d, 0d);
        }

        double outlineWidth = metricsWidth * fitScale;
        double outlineHeight = metricsHeight * fitScale;

        return new FieldCanvasPlacement(
            Math.Max(0d, (outputWidth - outlineWidth) * 0.5d),
            Math.Max(0d, (outputHeight - outlineHeight) * 0.5d));
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private readonly record struct FieldCanvasPlacement(double LeftPadding, double TopPadding);
}
