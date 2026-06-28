using System.Text;
using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Machina.Fonts.Toml;

namespace Machina.Fonts.ReferenceRendering;

public sealed class FontProofExporter
{
    private readonly FontAtlasTomlExportMetadata metadata;
    private readonly GlyphGenerationPipeline generationPipeline;
    private readonly GeneratedFieldAtlasPacker packer;

    public FontProofExporter(
        IGlyphOutlineSource outlineSource,
        IGlyphDistanceFieldGenerator generator,
        FontAtlasTomlExportMetadata metadata,
        GeneratedFieldAtlasPacker? packer = null)
    {
        generationPipeline = new GlyphGenerationPipeline(
            outlineSource ?? throw new ArgumentNullException(nameof(outlineSource)),
            generator ?? throw new ArgumentNullException(nameof(generator)));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.packer = packer ?? new GeneratedFieldAtlasPacker();
    }

    public async ValueTask<FontProofExportResult> ExportAsync(
        IReadOnlyList<FontProofArtifactDefinition> definitions,
        FontProofExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateDefinitions(definitions);
        cancellationToken.ThrowIfCancellationRequested();

        DistanceFieldTextRenderOptions renderOptions = options.ToRenderOptions();
        GlyphOutlineLoadOptions outlineOptions = new(
            (float)options.EmSize,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);
        MsdfGenerationSettings settings = new(
            options.Kind,
            options.FieldWidth,
            options.FieldHeight,
            options.PixelRange,
            1d,
            options.EdgeColoring,
            options.MiterLimit);

        List<DistanceFieldTextRun> runs = definitions
            .Select(definition => DistanceFieldTextRun.Create(
                definition.Text,
                options.Face,
                options.EmSize,
                options.Weight,
                options.Slant))
            .ToList();

        List<GeneratedGlyphDistanceField> fields = [];
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = [];
        List<FontGenerationDiagnostic> diagnostics = [];

        foreach (GlyphKey key in runs.SelectMany(static run => run.GlyphKeys).Distinct())
        {
            GlyphGenerationResult result = await generationPipeline.GenerateAsync(
                key,
                outlineOptions,
                settings,
                cancellationToken);

            if (result.Metrics is not null)
            {
                metricsByGlyph[key] = result.Metrics;
            }

            if (result.DistanceField is not null)
            {
                fields.Add(result.DistanceField);
            }

            if (IsMetricsOnly(result))
            {
                diagnostics.AddRange(result.Diagnostics.Where(static diagnostic => diagnostic.Severity != FontGenerationDiagnosticSeverity.Error));
                continue;
            }

            diagnostics.AddRange(result.Diagnostics);
            if (result.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
            {
                return CreateFailure(options.OutputDirectory, diagnostics);
            }
        }

        GeneratedFieldAtlasPackResult packResult = packer.Pack(
            fields,
            new GeneratedFieldAtlasPackOptions(options.PageWidth, options.PageHeight, options.PagePadding, options.AtlasName));
        diagnostics.AddRange(packResult.Diagnostics);

        if (!packResult.Success)
        {
            return CreateFailure(options.OutputDirectory, diagnostics);
        }

        FontAtlasArtifactExportResult export = DistanceFieldAtlasArtifactExporter.Export(
            packResult,
            metadata,
            options.OutputDirectory,
            options.AtlasName);
        diagnostics.AddRange(export.Diagnostics.Select(ConvertDiagnostic));

        if (!export.Success)
        {
            return CreateFailure(options.OutputDirectory, diagnostics, export.TomlPath, export.PagePaths);
        }

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        diagnostics.AddRange(import.Diagnostics.Select(ConvertDiagnostic));

        if (!import.Success || import.Snapshot is null)
        {
            return CreateFailure(options.OutputDirectory, diagnostics, export.TomlPath, export.PagePaths);
        }

        Dictionary<int, DistanceFieldPageReference> pages = [];
        foreach (FontAtlasPage page in import.Snapshot.Pages)
        {
            string pagePath = Path.Combine(options.OutputDirectory, page.ImagePath);
            pages.Add(page.Index, DistanceFieldPageReferenceReader.Read(pagePath));
        }

        List<FontProofArtifact> artifacts = [];
        for (int i = 0; i < definitions.Count; i++)
        {
            FontProofArtifactDefinition definition = definitions[i];
            DistanceFieldTextRun run = runs[i];
            DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(run, metricsByGlyph, renderOptions, diagnostics);

            if (layout.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
            {
                return CreateFailure(options.OutputDirectory, layout.Diagnostics, export.TomlPath, export.PagePaths, import.Snapshot);
            }

            RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(import.Snapshot, pages, layout, renderOptions);
            string ppmPath = Path.Combine(options.OutputDirectory, definition.Name);
            PpmImageWriter.Write(ppmPath, image);

            List<GlyphKey> renderedGlyphs = [];
            List<GlyphKey> metricsOnlyGlyphs = [];
            foreach (GlyphKey key in run.GlyphKeys)
            {
                if (Rune.IsWhiteSpace(new Rune(key.Codepoint)))
                {
                    metricsOnlyGlyphs.Add(key);
                }
                else
                {
                    renderedGlyphs.Add(key);
                }
            }

            artifacts.Add(new FontProofArtifact(
                definition,
                ppmPath,
                image,
                renderedGlyphs,
                metricsOnlyGlyphs));
        }

        return new FontProofExportResult(
            true,
            options.OutputDirectory,
            export.TomlPath,
            export.PagePaths,
            import.Snapshot,
            artifacts,
            diagnostics);
    }

    private static void ValidateDefinitions(IReadOnlyList<FontProofArtifactDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            throw new ArgumentException("At least one proof artifact definition is required.", nameof(definitions));
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (FontProofArtifactDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!names.Add(definition.Name))
            {
                throw new ArgumentException($"Duplicate proof artifact name '{definition.Name}'.", nameof(definitions));
            }
        }
    }

    private static bool IsMetricsOnly(GlyphGenerationResult result)
    {
        return result.Metrics is not null
            && result.Diagnostics.Any(static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline)
            && result.Diagnostics.All(static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline);
    }

    private static FontGenerationDiagnostic ConvertDiagnostic(FontAtlasTomlDiagnostic diagnostic)
    {
        return new FontGenerationDiagnostic(
            diagnostic.Severity == FontAtlasTomlDiagnosticSeverity.Error
                ? FontGenerationDiagnosticSeverity.Error
                : diagnostic.Severity == FontAtlasTomlDiagnosticSeverity.Warning
                    ? FontGenerationDiagnosticSeverity.Warning
                    : FontGenerationDiagnosticSeverity.Info,
            FontGenerationDiagnosticCode.AtlasPackingFailed,
            diagnostic.Message);
    }

    private static FontProofExportResult CreateFailure(
        string outputDirectory,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics,
        string? tomlPath = null,
        IReadOnlyList<string>? pagePaths = null,
        FontAtlasSnapshot? snapshot = null)
    {
        return new FontProofExportResult(
            false,
            outputDirectory,
            tomlPath,
            pagePaths?.ToArray() ?? Array.Empty<string>(),
            snapshot,
            Array.Empty<FontProofArtifact>(),
            diagnostics.ToArray());
    }
}
