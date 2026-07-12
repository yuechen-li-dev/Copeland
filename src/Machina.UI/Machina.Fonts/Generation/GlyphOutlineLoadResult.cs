namespace Machina.Fonts.Generation;

public sealed record GlyphOutlineLoadResult
{
    public GlyphOutlineLoadResult(
        bool success,
        GlyphOutline? outline,
        GlyphMetrics? metrics,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null entries.", nameof(diagnostics));
        }

        if (success && outline is null)
        {
            throw new ArgumentException("Successful outline loads must include an outline.", nameof(outline));
        }

        Success = success;
        Outline = outline;
        Metrics = metrics;
        Diagnostics = [.. diagnostics];
    }

    public bool Success { get; }

    public GlyphOutline? Outline { get; }

    public GlyphMetrics? Metrics { get; }

    public IReadOnlyList<FontGenerationDiagnostic> Diagnostics { get; }
}
