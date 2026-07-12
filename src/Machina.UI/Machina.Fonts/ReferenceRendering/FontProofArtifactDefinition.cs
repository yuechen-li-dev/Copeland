using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record FontProofArtifactDefinition(string Name, string Text)
{
    public FontProofArtifactDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(Text);

        if (!Name.EndsWith(".ppm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Proof artifact names must end with .ppm.", nameof(Name));
        }

        return this;
    }
}

public sealed record FontProofArtifact(
    FontProofArtifactDefinition Definition,
    string PpmPath,
    RgbaImage Image,
    IReadOnlyList<GlyphKey> RenderedGlyphs,
    IReadOnlyList<GlyphKey> MetricsOnlyGlyphs);

public sealed record FontProofExportOptions(
    string OutputDirectory,
    string AtlasName,
    FontFaceId Face,
    double EmSize,
    MachinaFontWeight Weight,
    MachinaFontSlant Slant,
    DistanceFieldKind Kind,
    int OutputWidth,
    int OutputHeight,
    int FieldWidth,
    int FieldHeight,
    double PixelRange,
    Rgba32 Foreground,
    Rgba32 Background,
    double X,
    double BaselineY,
    bool ShowBaselineGuide = false,
    Rgba32? BaselineGuideColor = null,
    bool FlipY = false,
    int PageWidth = 96,
    int PageHeight = 96,
    int PagePadding = 2,
    string EdgeColoring = "simple",
    double MiterLimit = 2d)
{
    public FontProofExportOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(AtlasName);

        _ = ToRenderOptions();
        return this;
    }

    internal DistanceFieldTextRenderOptions ToRenderOptions()
    {
        return new DistanceFieldTextRenderOptions(
            OutputWidth,
            OutputHeight,
            Face,
            EmSize,
            Weight,
            Slant,
            Kind,
            FieldWidth,
            FieldHeight,
            PixelRange,
            Foreground,
            Background,
            X,
            BaselineY,
            ShowBaselineGuide,
            BaselineGuideColor,
            FlipY,
            PageWidth,
            PageHeight,
            PagePadding,
            EdgeColoring,
            MiterLimit).Validate();
    }
}

public sealed record FontProofExportResult(
    bool Success,
    string OutputDirectory,
    string? TomlPath,
    IReadOnlyList<string> PagePaths,
    FontAtlasSnapshot? Snapshot,
    IReadOnlyList<FontProofArtifact> Artifacts,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);
