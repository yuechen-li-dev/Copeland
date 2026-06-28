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
            options.Threshold,
            options.SmoothingMultiplier,
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

            DistanceFieldGlyphDrawBounds drawBounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(placement, entry);

            CpuDistanceFieldGlyphRenderer.RenderGlyphInto(
                image,
                page,
                entry,
                drawBounds.X,
                drawBounds.Y,
                drawBounds.Width,
                drawBounds.Height,
                glyphOptions);
        }

        if (options.ShowBaselineGuide)
        {
            DrawHorizontalLine(
                image,
                (int)Math.Round(options.BaselineY, MidpointRounding.AwayFromZero),
                options.BaselineGuideColor ?? throw new InvalidOperationException("Baseline guide color was not configured."));
        }

        return image;
    }

    private static void DrawHorizontalLine(RgbaImage image, int y, Rgba32 color)
    {
        if ((uint)y >= (uint)image.Height)
        {
            return;
        }

        for (int x = 0; x < image.Width; x++)
        {
            image.SetPixel(x, y, color);
        }
    }
}
