using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Rendering;

internal static class FontReferenceOracleWorkflow
{
    public const string OutputDirectoryEnvironmentVariable = "MACHINA_FONT_REFERENCE_OUTPUT_DIR";
    public const string ManualInstructionsFileName = "manual-reference-instructions.txt";
    public const string PlacementReportTextFileName = "glyph-placement-report.txt";
    public const string PlacementReportJsonFileName = "glyph-placement-report.json";
    public const double ProofEmSize = 32d;
    public const int ProofWidth = 320;
    public const int ProofHeight = 64;
    public const double ProofOriginX = 8d;
    public const double ProofBaselineY = 40d;

    private static readonly Rgba32 Background = new(16, 16, 24, 255);
    private static readonly Rgba32 Foreground = new(240, 240, 240, 255);

    public static IReadOnlyList<FontReferenceOracleDefinition> Definitions { get; } =
    [
        new("machina", "Machina"),
        new("hello-machina", "Hello Machina"),
        new("kerning", "AV To Ta Wa Yo"),
        new("aa0", "Aa0"),
        new("a-space-a", "A A"),
    ];

    public static FontProofExportOptions CreateOptions(string outputDirectory)
    {
        return new FontProofExportOptions(
            outputDirectory,
            "crimson-text-reference-oracle",
            TypographyKerningFixtureFont.Face,
            ProofEmSize,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            ProofWidth,
            ProofHeight,
            32,
            32,
            4d,
            Foreground,
            Background,
            ProofOriginX,
            ProofBaselineY,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2);
    }

    public static async Task<FontReferenceOracleExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        FontProofExporter exporter = new(
            source,
            new MsdfSharpDistanceFieldGenerator(),
            CreateMetadata());

        FontProofArtifactDefinition[] artifactDefinitions = Definitions
            .Select(static definition => new FontProofArtifactDefinition(definition.MachinaPpmFileName, definition.Text))
            .ToArray();

        FontProofExportResult export = await exporter.ExportAsync(
            artifactDefinitions,
            CreateOptions(fullOutputDirectory),
            cancellationToken);

        if (!export.Success || export.Snapshot is null)
        {
            throw new InvalidOperationException("Machina MSDF reference-oracle export failed.");
        }

        List<FontReferenceOracleArtifact> artifacts = [];
        Dictionary<string, DistanceFieldTextLayoutResult> layouts = [];
        Dictionary<string, Dictionary<GlyphPairKey, GlyphPairAdjustment>> pairAdjustmentsByFixture = [];
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = await LoadMetricsAsync(source, cancellationToken);
        DistanceFieldTextRenderOptions renderOptions = CreateRenderOptions();

        foreach (FontReferenceOracleDefinition definition in Definitions)
        {
            FontProofArtifact artifact = AssertSingleArtifact(export.Artifacts, definition.MachinaPpmFileName);
            string pngPath = Path.Combine(fullOutputDirectory, definition.MachinaPngFileName);
            RgbaPngWriter.Write(pngPath, artifact.Image);

            DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                definition.Text,
                TypographyKerningFixtureFont.Face,
                ProofEmSize,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright);
            Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = await CollectPairAdjustmentsAsync(source, run, cancellationToken);
            DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
                run,
                metricsByGlyph,
                renderOptions,
                pairAdjustments: pairAdjustments);

            layouts.Add(definition.Id, layout);
            pairAdjustmentsByFixture.Add(definition.Id, pairAdjustments);

            artifacts.Add(new FontReferenceOracleArtifact(
                definition,
                artifact.PpmPath,
                pngPath));
        }

        FontReferenceOraclePlacementReport report = BuildPlacementReport(
            export.Snapshot,
            metricsByGlyph,
            pairAdjustmentsByFixture,
            layouts);

        string reportTextPath = Path.Combine(fullOutputDirectory, PlacementReportTextFileName);
        string reportJsonPath = Path.Combine(fullOutputDirectory, PlacementReportJsonFileName);
        File.WriteAllText(reportTextPath, BuildTextReport(report));
        File.WriteAllText(reportJsonPath, JsonSerializer.Serialize(report, JsonOptions));

        return new FontReferenceOracleExportResult(
            OutputDirectory: fullOutputDirectory,
            TomlPath: export.TomlPath!,
            PagePaths: export.PagePaths,
            Artifacts: artifacts,
            PlacementReportTextPath: reportTextPath,
            PlacementReportJsonPath: reportJsonPath,
            FontPath: TypographyKerningFixtureFont.FontPath,
            EmSize: ProofEmSize,
            OutputWidth: ProofWidth,
            OutputHeight: ProofHeight,
            OriginX: ProofOriginX,
            BaselineY: ProofBaselineY);
    }

    public static string GetRequestedOutputDirectoryOrCreateTemp()
    {
        string? requested = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8o", Guid.NewGuid().ToString("N"));
    }

    public static string BuildManualInstructions(string outputDirectory)
    {
        StringBuilder builder = new();
        builder.AppendLine("Automated browser reference export was not available.");
        builder.AppendLine("Open the reference fixture in a local browser and save screenshots for each proof line.");
        builder.AppendLine();
        builder.AppendLine($"Output directory: {Path.GetFullPath(outputDirectory)}");
        builder.AppendLine($"Fixture font: {TypographyKerningFixtureFont.FontPath}");
        builder.AppendLine($"Em size: {ProofEmSize}");
        builder.AppendLine($"Canvas: {ProofWidth}x{ProofHeight}");
        builder.AppendLine($"OriginX: {ProofOriginX}");
        builder.AppendLine($"BaselineY: {ProofBaselineY}");
        builder.AppendLine();
        builder.AppendLine("Required texts:");
        foreach (FontReferenceOracleDefinition definition in Definitions.Take(3))
        {
            builder.AppendLine($"- {definition.Text}");
        }

        builder.AppendLine("Optional texts:");
        foreach (FontReferenceOracleDefinition definition in Definitions.Skip(3))
        {
            builder.AppendLine($"- {definition.Text}");
        }

        return builder.ToString();
    }

    private static async Task<Dictionary<GlyphKey, GlyphMetrics>> LoadMetricsAsync(
        TypographyGlyphOutlineSource source,
        CancellationToken cancellationToken)
    {
        GlyphOutlineLoadOptions options = new(
            (float)ProofEmSize,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = [];

        foreach (GlyphKey key in Definitions
                     .SelectMany(static definition => DistanceFieldTextRun.Create(
                         definition.Text,
                         TypographyKerningFixtureFont.Face,
                         ProofEmSize,
                         MachinaFontWeight.Regular,
                         MachinaFontSlant.Upright).GlyphKeys)
                     .Distinct())
        {
            GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
                key.Face,
                key.Codepoint,
                options,
                cancellationToken);

            if (result.Metrics is null)
            {
                throw new InvalidOperationException($"No glyph metrics were returned for U+{key.Codepoint:X4}.");
            }

            metricsByGlyph[key] = result.Metrics;
        }

        return metricsByGlyph;
    }

    private static async Task<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        TypographyGlyphOutlineSource source,
        DistanceFieldTextRun run,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> result = [];
        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;

        foreach (GlyphKey key in run.GlyphKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
            if (previousKey is GlyphKey previous && !previousWasWhitespace && !isWhitespace)
            {
                GlyphPairAdjustment? adjustment = await source.GetPairAdjustmentAsync(previous, key, cancellationToken);
                if (adjustment is not null)
                {
                    result[new GlyphPairKey(previous, key)] = adjustment;
                }
            }

            previousKey = key;
            previousWasWhitespace = isWhitespace;
        }

        return result;
    }

    private static FontReferenceOraclePlacementReport BuildPlacementReport(
        FontAtlasSnapshot snapshot,
        IReadOnlyDictionary<GlyphKey, GlyphMetrics> metricsByGlyph,
        IReadOnlyDictionary<string, Dictionary<GlyphPairKey, GlyphPairAdjustment>> pairAdjustmentsByFixture,
        IReadOnlyDictionary<string, DistanceFieldTextLayoutResult> layouts)
    {
        List<FontReferenceOracleFixtureReport> fixtures = [];
        DistanceFieldTextRenderOptions renderOptions = CreateRenderOptions();

        foreach (FontReferenceOracleDefinition definition in Definitions)
        {
            DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                definition.Text,
                TypographyKerningFixtureFont.Face,
                ProofEmSize,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright);
            DistanceFieldTextLayoutResult layout = layouts[definition.Id];
            Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = pairAdjustmentsByFixture[definition.Id];

            List<FontReferenceOracleGlyphRow> rows = [];
            double runningPenX = ProofOriginX;
            GlyphKey? previousKey = null;
            bool previousWasWhitespace = true;

            for (int index = 0; index < run.GlyphKeys.Count; index++)
            {
                GlyphKey key = run.GlyphKeys[index];
                GlyphMetrics metrics = metricsByGlyph[key];
                bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
                double penBeforeAdjustment = runningPenX;
                GlyphPairAdjustment? pairAdjustment = null;

                if (previousKey is GlyphKey previous
                    && !previousWasWhitespace
                    && !isWhitespace
                    && pairAdjustments.TryGetValue(new GlyphPairKey(previous, key), out GlyphPairAdjustment? adjustment))
                {
                    pairAdjustment = adjustment;
                    runningPenX += adjustment.AdvanceX;
                }

                double penAfterAdjustment = runningPenX;
                DistanceFieldGlyphPlacement placement = layout.Placements[index];
                GlyphAtlasEntry? atlasEntry = snapshot.Glyphs.TryGetValue(key, out GlyphAtlasEntry? entry)
                    ? entry
                    : null;
                FieldPlacementDetails? fieldPlacement = atlasEntry is null || isWhitespace
                    ? null
                    : ComputeFieldPlacement(placement, atlasEntry, renderOptions);

                rows.Add(new FontReferenceOracleGlyphRow(
                    index,
                    FormatCharacter(key.Codepoint),
                    $"U+{key.Codepoint:X4}",
                    $"{key.Face}:{key.Codepoint:X4}@{key.EmSize:0.##}",
                    metrics.Advance,
                    metrics.BearingX,
                    metrics.BearingY,
                    metrics.Width,
                    metrics.Height,
                    pairAdjustment?.AdvanceX,
                    pairAdjustment?.AdvanceY,
                    penBeforeAdjustment,
                    penAfterAdjustment,
                    fieldPlacement?.DrawX,
                    fieldPlacement?.DrawY,
                    atlasEntry?.PageIndex,
                    atlasEntry?.X,
                    atlasEntry?.Y,
                    atlasEntry?.Width,
                    atlasEntry?.Height,
                    atlasEntry?.U0,
                    atlasEntry?.V0,
                    atlasEntry?.U1,
                    atlasEntry?.V1,
                    fieldPlacement?.OutputWidth,
                    fieldPlacement?.OutputHeight,
                    fieldPlacement?.LeftPadding,
                    fieldPlacement?.TopPadding,
                    isWhitespace));

                runningPenX = penAfterAdjustment + metrics.Advance;
                previousKey = key;
                previousWasWhitespace = isWhitespace;
            }

            fixtures.Add(new FontReferenceOracleFixtureReport(
                definition.Id,
                definition.Text,
                layout.Width,
                rows));
        }

        return new FontReferenceOraclePlacementReport(
            FontPath: TypographyKerningFixtureFont.FontPath,
            EmSize: ProofEmSize,
            OutputWidth: ProofWidth,
            OutputHeight: ProofHeight,
            OriginX: ProofOriginX,
            BaselineY: ProofBaselineY,
            Fixtures: fixtures);
    }

    private static FieldPlacementDetails ComputeFieldPlacement(
        DistanceFieldGlyphPlacement placement,
        GlyphAtlasEntry entry,
        DistanceFieldTextRenderOptions options)
    {
        int outputWidth = Math.Max(1, RoundToInt(entry.Width * placement.Scale));
        int outputHeight = Math.Max(1, RoundToInt(entry.Height * placement.Scale));

        double metricsWidth = placement.Metrics.Width * placement.Scale;
        double metricsHeight = placement.Metrics.Height * placement.Scale;
        double leftPadding;
        double topPadding;

        if (metricsWidth <= 0d || metricsHeight <= 0d)
        {
            leftPadding = outputWidth * 0.5d;
            topPadding = outputHeight * 0.5d;
        }
        else
        {
            double scaleX = outputWidth / (double)entry.Width;
            double scaleY = outputHeight / (double)entry.Height;
            double scaledPixelRangeX = options.PixelRange * scaleX;
            double scaledPixelRangeY = options.PixelRange * scaleY;
            double drawableWidth = Math.Max(0.0001d, outputWidth - (scaledPixelRangeX * 2d));
            double drawableHeight = Math.Max(0.0001d, outputHeight - (scaledPixelRangeY * 2d));
            double fitScale = Math.Min(drawableWidth / metricsWidth, drawableHeight / metricsHeight);

            if (!double.IsFinite(fitScale) || fitScale <= 0d)
            {
                leftPadding = 0d;
                topPadding = 0d;
            }
            else
            {
                double outlineWidth = metricsWidth * fitScale;
                double outlineHeight = metricsHeight * fitScale;
                leftPadding = Math.Max(0d, (outputWidth - outlineWidth) * 0.5d);
                topPadding = Math.Max(0d, (outputHeight - outlineHeight) * 0.5d);
            }
        }

        int drawX = RoundToInt((placement.X + (placement.Metrics.BearingX * placement.Scale)) - leftPadding);
        int drawY = RoundToInt((placement.BaselineY - (placement.Metrics.BearingY * placement.Scale)) - topPadding);

        return new FieldPlacementDetails(
            outputWidth,
            outputHeight,
            leftPadding,
            topPadding,
            drawX,
            drawY);
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static FontAtlasTomlExportMetadata CreateMetadata()
    {
        return new FontAtlasTomlExportMetadata(
            "crimson-text-reference-oracle",
            "msdf",
            "Crimson Text",
            "Regular",
            TypographyKerningFixtureFont.FontPath,
            ComputeFileSha256(TypographyKerningFixtureFont.FontPath),
            "OFL-1.1",
            new FontAtlasMetricsToml
            {
                EmSize = ProofEmSize,
                UnitsPerEm = 1000,
                Ascent = 25.6,
                Descent = -6.4,
                LineGap = 0,
                LineHeight = 32,
            },
            new FontAtlasMsdfToml
            {
                Range = 4,
                Scale = 1,
                EdgeColoring = "simple",
                MiterLimit = 2,
            });
    }

    private static DistanceFieldTextRenderOptions CreateRenderOptions()
    {
        return new DistanceFieldTextRenderOptions(
            ProofWidth,
            ProofHeight,
            TypographyKerningFixtureFont.Face,
            ProofEmSize,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            32,
            32,
            4d,
            Foreground,
            Background,
            ProofOriginX,
            ProofBaselineY,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2,
            EdgeColoring: "simple",
            MiterLimit: 2d).Validate();
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildTextReport(FontReferenceOraclePlacementReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina MSDF glyph placement report");
        builder.AppendLine($"fontPath: {report.FontPath}");
        builder.AppendLine($"emSize: {report.EmSize}");
        builder.AppendLine($"output: {report.OutputWidth}x{report.OutputHeight}");
        builder.AppendLine($"originX: {report.OriginX}");
        builder.AppendLine($"baselineY: {report.BaselineY}");
        builder.AppendLine();

        foreach (FontReferenceOracleFixtureReport fixture in report.Fixtures)
        {
            builder.AppendLine($"[{fixture.Id}] {fixture.Text}");
            builder.AppendLine($"layoutWidth: {fixture.LayoutWidth:0.###}");
            builder.AppendLine("index\tchar\tcodepoint\tglyphKey\tadvance\tbearingX\tbearingY\tmetricsWidth\tmetricsHeight\tpairAdjustX\tpairAdjustY\tpenBefore\tpenAfter\tdrawX\tdrawY\tatlasPage\tatlasRect\tuv0\tuv1\tfieldSize\tfieldPadding\twhitespace");

            foreach (FontReferenceOracleGlyphRow row in fixture.Glyphs)
            {
                builder.AppendLine(
                    string.Join(
                        '\t',
                        row.Index,
                        row.Character,
                        row.Codepoint,
                        row.GlyphKey,
                        FormatNullable(row.Advance),
                        FormatNullable(row.BearingX),
                        FormatNullable(row.BearingY),
                        FormatNullable(row.MetricsWidth),
                        FormatNullable(row.MetricsHeight),
                        FormatNullable(row.PairAdjustmentAdvanceX),
                        FormatNullable(row.PairAdjustmentAdvanceY),
                        FormatNullable(row.PenXBeforePairAdjustment),
                        FormatNullable(row.PenXAfterPairAdjustment),
                        FormatNullable(row.DrawX),
                        FormatNullable(row.DrawY),
                        FormatNullable(row.AtlasPage),
                        $"{FormatNullable(row.AtlasRectX)},{FormatNullable(row.AtlasRectY)},{FormatNullable(row.AtlasRectWidth)},{FormatNullable(row.AtlasRectHeight)}",
                        $"{FormatNullable(row.U0)},{FormatNullable(row.V0)}",
                        $"{FormatNullable(row.U1)},{FormatNullable(row.V1)}",
                        $"{FormatNullable(row.FieldWidth)},{FormatNullable(row.FieldHeight)}",
                        $"{FormatNullable(row.LeftPadding)},{FormatNullable(row.TopPadding)}",
                        row.IsWhitespace ? "yes" : "no"));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatCharacter(int codepoint)
    {
        return codepoint switch
        {
            ' ' => "<space>",
            '\t' => "<tab>",
            '\n' => "<newline>",
            '\r' => "<carriage-return>",
            _ => new Rune(codepoint).ToString(),
        };
    }

    private static string FormatNullable(double? value)
    {
        return value?.ToString("0.###") ?? "not available";
    }

    private static string FormatNullable(int? value)
    {
        return value?.ToString() ?? "not available";
    }

    private static FontProofArtifact AssertSingleArtifact(IReadOnlyList<FontProofArtifact> artifacts, string fileName)
    {
        FontProofArtifact? artifact = artifacts.SingleOrDefault(
            item => string.Equals(Path.GetFileName(item.PpmPath), fileName, StringComparison.OrdinalIgnoreCase));

        return artifact ?? throw new InvalidOperationException($"Expected proof artifact '{fileName}' was not exported.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed record FieldPlacementDetails(
        int OutputWidth,
        int OutputHeight,
        double LeftPadding,
        double TopPadding,
        int DrawX,
        int DrawY);
}

internal sealed record FontReferenceOracleDefinition(string Id, string Text)
{
    public string ReferencePngFileName => $"reference-{Id}.png";

    public string MachinaPpmFileName => $"machina-msdf-{Id}.ppm";

    public string MachinaPngFileName => $"machina-msdf-{Id}.png";

    public string ComparePngFileName => $"compare-{Id}.png";
}

internal sealed record FontReferenceOracleArtifact(
    FontReferenceOracleDefinition Definition,
    string MachinaPpmPath,
    string MachinaPngPath);

internal sealed record FontReferenceOracleExportResult(
    string OutputDirectory,
    string TomlPath,
    IReadOnlyList<string> PagePaths,
    IReadOnlyList<FontReferenceOracleArtifact> Artifacts,
    string PlacementReportTextPath,
    string PlacementReportJsonPath,
    string FontPath,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    double OriginX,
    double BaselineY);

internal sealed record FontReferenceOraclePlacementReport(
    string FontPath,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    double OriginX,
    double BaselineY,
    IReadOnlyList<FontReferenceOracleFixtureReport> Fixtures);

internal sealed record FontReferenceOracleFixtureReport(
    string Id,
    string Text,
    double LayoutWidth,
    IReadOnlyList<FontReferenceOracleGlyphRow> Glyphs);

internal sealed record FontReferenceOracleGlyphRow(
    int Index,
    string Character,
    string Codepoint,
    string GlyphKey,
    double Advance,
    double BearingX,
    double BearingY,
    double MetricsWidth,
    double MetricsHeight,
    double? PairAdjustmentAdvanceX,
    double? PairAdjustmentAdvanceY,
    double PenXBeforePairAdjustment,
    double PenXAfterPairAdjustment,
    int? DrawX,
    int? DrawY,
    int? AtlasPage,
    int? AtlasRectX,
    int? AtlasRectY,
    int? AtlasRectWidth,
    int? AtlasRectHeight,
    double? U0,
    double? V0,
    double? U1,
    double? V1,
    int? FieldWidth,
    int? FieldHeight,
    double? LeftPadding,
    double? TopPadding,
    bool IsWhitespace);
