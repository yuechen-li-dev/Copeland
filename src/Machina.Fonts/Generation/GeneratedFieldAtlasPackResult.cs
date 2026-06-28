namespace Machina.Fonts.Generation;

public sealed record GeneratedFieldAtlasPackResult
{
    public GeneratedFieldAtlasPackResult(
        bool success,
        FontAtlasSnapshot snapshot,
        IReadOnlyList<GeneratedFieldAtlasPage> pages,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (pages.Any(static page => page is null))
        {
            throw new ArgumentException("Pages must not contain null values.", nameof(pages));
        }

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null values.", nameof(diagnostics));
        }

        Success = success;
        Snapshot = snapshot;
        Pages = pages.ToArray();
        Diagnostics = diagnostics.ToArray();
    }

    public bool Success { get; }

    public FontAtlasSnapshot Snapshot { get; }

    public IReadOnlyList<GeneratedFieldAtlasPage> Pages { get; }

    public IReadOnlyList<FontGenerationDiagnostic> Diagnostics { get; }
}
