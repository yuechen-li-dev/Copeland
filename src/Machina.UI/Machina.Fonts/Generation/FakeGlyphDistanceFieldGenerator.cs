namespace Machina.Fonts.Generation;

public sealed class FakeGlyphDistanceFieldGenerator : IGlyphDistanceFieldGenerator
{
    public GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(settings);

        cancellationToken.ThrowIfCancellationRequested();

        if (outline.Contours.Count == 0 && !IsWhitespaceCodepoint(outline.Key.Codepoint))
        {
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Error,
                FontGenerationDiagnosticCode.EmptyOutline,
                "Non-whitespace glyph outlines must contain at least one contour.",
                outline.Key);

            return CreateResult(outline, settings, [diagnostic]);
        }

        return CreateResult(outline, settings, []);
    }

    private static GeneratedGlyphDistanceField CreateResult(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        int channelCount = FakeDistanceFieldValidation.GetChannelCount(settings.Kind);
        int length = checked(settings.Width * settings.Height * channelCount);
        float[] data = new float[length];

        double baseSeed = outline.Key.Codepoint
            + outline.Contours.Count * 17
            + outline.Bounds.MaxX * 0.13
            + outline.Bounds.MaxY * 0.07
            + settings.PixelRange * 0.11
            + settings.Scale * 0.19
            + settings.MiterLimit * 0.23
            + settings.EdgeColoring.Length * 0.03
            + (int)settings.Kind * 0.29;

        for (int y = 0; y < settings.Height; y++)
        {
            for (int x = 0; x < settings.Width; x++)
            {
                for (int channel = 0; channel < channelCount; channel++)
                {
                    int index = ((y * settings.Width) + x) * channelCount + channel;
                    double sample = baseSeed
                        + x * 0.37
                        + y * 0.61
                        + channel * 0.17
                        + outline.Metrics.Advance * 0.05
                        + outline.Metrics.Width * 0.09
                        + outline.Metrics.Height * 0.04;

                    double wave = Math.Sin(sample) * 0.5 + 0.5;
                    data[index] = (float)wave;
                }
            }
        }

        return new GeneratedGlyphDistanceField(
            outline.Key,
            outline.Metrics,
            settings.Width,
            settings.Height,
            settings.Kind,
            channelCount,
            data,
            GlyphFieldPlacement.CreateFromMetricsBox(outline.Metrics, settings.PixelRange, Math.Max(settings.Scale, 0.0001d)),
            diagnostics);
    }

    private static bool IsWhitespaceCodepoint(int codepoint)
    {
        return System.Text.Rune.TryCreate(codepoint, out System.Text.Rune rune) && System.Text.Rune.IsWhiteSpace(rune);
    }
}
