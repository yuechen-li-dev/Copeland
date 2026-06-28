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
            int destinationX = ComputeDestinationX(placement, entry);
            int destinationY = ComputeDestinationY(placement, entry);

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

    private static int ComputeDestinationX(DistanceFieldGlyphPlacement placement, GlyphAtlasEntry entry)
    {
        double paddedLeft = Math.Max(0d, (entry.Width - placement.Metrics.Width) * 0.5d) * placement.Scale;
        double destinationX = placement.X + (placement.Metrics.BearingX * placement.Scale) - paddedLeft;
        return RoundToInt(destinationX);
    }

    private static int ComputeDestinationY(DistanceFieldGlyphPlacement placement, GlyphAtlasEntry entry)
    {
        double paddedTop = Math.Max(0d, (entry.Height - placement.Metrics.Height) * 0.5d) * placement.Scale;
        double destinationY = placement.BaselineY - (placement.Metrics.BearingY * placement.Scale) - paddedTop;
        return RoundToInt(destinationY);
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
