namespace Machina.Fonts.Generation;

public sealed class GlyphGenerationPipeline
{
    private readonly IGlyphOutlineSource outlineSource;
    private readonly IGlyphDistanceFieldGenerator generator;

    public GlyphGenerationPipeline(
        IGlyphOutlineSource outlineSource,
        IGlyphDistanceFieldGenerator generator)
    {
        this.outlineSource = outlineSource ?? throw new ArgumentNullException(nameof(outlineSource));
        this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public async ValueTask<GlyphGenerationResult> GenerateAsync(
        GlyphKey key,
        GlyphOutlineLoadOptions outlineOptions,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        GlyphOutlineLoadResult outlineResult = await outlineSource.LoadGlyphOutlineAsync(
            key.Face,
            key.Codepoint,
            outlineOptions,
            cancellationToken);

        List<FontGenerationDiagnostic> diagnostics = [.. outlineResult.Diagnostics];
        if (!outlineResult.Success || outlineResult.Outline is null)
        {
            return new GlyphGenerationResult(
                false,
                null,
                null,
                outlineResult.Metrics,
                diagnostics);
        }

        GlyphOutline outline = new(
            key,
            outlineResult.Outline.Metrics,
            outlineResult.Outline.Bounds,
            outlineResult.Outline.Contours);

        GeneratedGlyphDistanceField distanceField = generator.Generate(outline, settings, cancellationToken);
        diagnostics.AddRange(distanceField.Diagnostics);

        bool success = diagnostics.All(static diagnostic => diagnostic.Severity != FontGenerationDiagnosticSeverity.Error);
        return new GlyphGenerationResult(
            success,
            outline,
            distanceField,
            outline.Metrics,
            diagnostics);
    }
}

public sealed record GlyphGenerationResult
{
    public GlyphGenerationResult(
        bool success,
        GlyphOutline? outline,
        GeneratedGlyphDistanceField? distanceField,
        GlyphMetrics? metrics,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null entries.", nameof(diagnostics));
        }

        Success = success;
        Outline = outline;
        DistanceField = distanceField;
        Metrics = metrics;
        Diagnostics = [.. diagnostics];
    }

    public bool Success { get; }

    public GlyphOutline? Outline { get; }

    public GeneratedGlyphDistanceField? DistanceField { get; }

    public GlyphMetrics? Metrics { get; }

    public IReadOnlyList<FontGenerationDiagnostic> Diagnostics { get; }
}
