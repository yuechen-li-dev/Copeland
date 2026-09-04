using System.Diagnostics;
using System.Text;
using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Machina.Fonts.Toml;

namespace Machina.Fonts.ReferenceRendering;

public sealed class DistanceFieldTextPipeline
{
    private readonly FontAtlasTomlExportMetadata metadata;
    private readonly GlyphGenerationPipeline generationPipeline;
    private readonly GeneratedFieldAtlasPacker packer;
    private readonly IGlyphPairAdjustmentSource? pairAdjustmentSource;

    public DistanceFieldTextPipeline(
        IGlyphOutlineSource outlineSource,
        IGlyphDistanceFieldGenerator generator,
        FontAtlasTomlExportMetadata metadata,
        GeneratedFieldAtlasPacker? packer = null,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null)
    {
        ArgumentNullException.ThrowIfNull(outlineSource);

        generationPipeline = new GlyphGenerationPipeline(
            outlineSource,
            generator ?? throw new ArgumentNullException(nameof(generator)));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.packer = packer ?? new GeneratedFieldAtlasPacker();
        this.pairAdjustmentSource = pairAdjustmentSource ?? outlineSource as IGlyphPairAdjustmentSource;
    }

    public async ValueTask<DistanceFieldTextPipelineResult> RenderTextAsync(
        string text,
        DistanceFieldTextRenderOptions options,
        string? artifactDirectory = null,
        IReadOnlyDictionary<int, double>? tokenAnchorOrigins = null,
        DistanceFieldTextLayoutResult? sharedLayout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            options.Face,
            options.EmSize,
            options.Weight,
            options.Slant);

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
        Stopwatch generationTimer = Stopwatch.StartNew();

        List<GeneratedGlyphDistanceField> fields = [];
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = [];
        List<GlyphKey> renderedGlyphs = [];
        List<GlyphKey> metricsOnlyGlyphs = [];
        List<FontGenerationDiagnostic> diagnostics = [];

        foreach (GlyphKey key in run.GlyphKeys.Distinct())
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
                return CreateFailure(metricsOnlyGlyphs, renderedGlyphs, diagnostics);
            }
        }

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

        GeneratedFieldAtlasPackResult packResult = packer.Pack(
            fields,
            new GeneratedFieldAtlasPackOptions(options.PageWidth, options.PageHeight, options.PagePadding, metadata.Name));
        diagnostics.AddRange(packResult.Diagnostics);

        if (!packResult.Success)
        {
            return CreateFailure(metricsOnlyGlyphs, renderedGlyphs, diagnostics);
        }

        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = await CollectPairAdjustmentsAsync(run, cancellationToken);
        DistanceFieldTextLayoutResult layout = sharedLayout
            ?? DistanceFieldTextLayout.Layout(
                run,
                metricsByGlyph,
                options,
                diagnostics,
                pairAdjustments,
                tokenAnchorOrigins);

        if (sharedLayout is not null && !string.Equals(sharedLayout.GlyphRun.Text, text, StringComparison.Ordinal))
        {
            throw new ArgumentException("The shared layout text must match the requested text.", nameof(sharedLayout));
        }
        if (layout.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            return CreateFailure(metricsOnlyGlyphs, renderedGlyphs, layout.Diagnostics);
        }

        string outputDirectory = artifactDirectory ?? Path.Combine(Path.GetTempPath(), "machina-fonts-m8k", Guid.NewGuid().ToString("N"));
        FontAtlasArtifactExportResult export = DistanceFieldAtlasArtifactExporter.Export(
            packResult,
            metadata,
            outputDirectory,
            metadata.Name);
        diagnostics.AddRange(export.Diagnostics.Select(ConvertDiagnostic));

        if (!export.Success)
        {
            return CreateFailure(metricsOnlyGlyphs, renderedGlyphs, diagnostics);
        }

        FontAtlasArtifactImportResult import = FontAtlasArtifactImporter.Import(export.TomlPath);
        diagnostics.AddRange(import.Diagnostics.Select(ConvertDiagnostic));
        if (!import.Success || import.Snapshot is null)
        {
            return CreateFailure(metricsOnlyGlyphs, renderedGlyphs, diagnostics);
        }

        Dictionary<int, DistanceFieldPageReference> pages = [];
        foreach (FontAtlasPage page in import.Snapshot.Pages)
        {
            string pagePath = Path.Combine(outputDirectory, page.ImagePath);
            pages.Add(page.Index, DistanceFieldPageReferenceReader.Read(pagePath));
        }
        generationTimer.Stop();

        Stopwatch renderTimer = Stopwatch.StartNew();
        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(import.Snapshot, pages, layout, options);
        string ppmPath = Path.Combine(outputDirectory, metadata.Name + ".ppm");
        PpmImageWriter.Write(ppmPath, image);
        renderTimer.Stop();

        return new DistanceFieldTextPipelineResult(
            true,
            image,
            ppmPath,
            import.Snapshot,
            layout,
            renderedGlyphs,
            metricsOnlyGlyphs,
            diagnostics,
            new DistanceFieldTextPipelineTimings(
                generationTimer.Elapsed.TotalMilliseconds,
                renderTimer.Elapsed.TotalMilliseconds));
    }

    private async ValueTask<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        DistanceFieldTextRun run,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> adjustments = [];
        if (pairAdjustmentSource is null)
        {
            return adjustments;
        }

        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;

        foreach (GlyphKey key in run.GlyphKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
            if (previousKey is GlyphKey previous && !previousWasWhitespace && !isWhitespace)
            {
                GlyphPairAdjustment? adjustment = await pairAdjustmentSource.GetPairAdjustmentAsync(previous, key, cancellationToken);
                if (adjustment is not null)
                {
                    adjustments[new GlyphPairKey(previous, key)] = adjustment;
                }
            }

            previousKey = key;
            previousWasWhitespace = isWhitespace;
        }

        return adjustments;
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

    private static DistanceFieldTextPipelineResult CreateFailure(
        IReadOnlyList<GlyphKey> metricsOnlyGlyphs,
        IReadOnlyList<GlyphKey> renderedGlyphs,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        return new DistanceFieldTextPipelineResult(
            false,
            null,
            null,
            null,
            null,
            renderedGlyphs.ToArray(),
            metricsOnlyGlyphs.ToArray(),
            diagnostics.ToArray(),
            null);
    }
}

public sealed record DistanceFieldTextPipelineResult(
    bool Success,
    RgbaImage? Image,
    string? PpmPath,
    FontAtlasSnapshot? Snapshot,
    DistanceFieldTextLayoutResult? Layout,
    IReadOnlyList<GlyphKey> RenderedGlyphs,
    IReadOnlyList<GlyphKey> MetricsOnlyGlyphs,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics,
    DistanceFieldTextPipelineTimings? Timings = null);

public sealed record DistanceFieldTextPipelineTimings(
    double AtlasGenerationMilliseconds,
    double RenderMilliseconds);
