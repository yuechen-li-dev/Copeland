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
    public const string BrowserMetricsPathEnvironmentVariable = "MACHINA_FONT_REFERENCE_BROWSER_METRICS_PATH";
    public const string ManualInstructionsFileName = "manual-reference-instructions.txt";
    public const string BrowserTextMetricsFileName = "browser-text-metrics.json";
    public const string PlacementReportTextFileName = "glyph-placement-report.txt";
    public const string PlacementReportJsonFileName = "glyph-placement-report.json";
    public const string CoverageExperimentFileName = "coverage-experiment.json";
    public const double ProofEmSize = 32d;
    public const int ProofWidth = 320;
    public const int ProofHeight = 64;
    public const double ProofOriginX = 8d;
    public const double ProofBaselineY = 40d;
    public const double ProofThreshold = 0.54d;
    public const double ProofSmoothingMultiplier = 0.5d;

    private static readonly Rgba32 Background = new(16, 16, 24, 255);
    private static readonly Rgba32 Foreground = new(240, 240, 240, 255);
    private static readonly Rgba32 BaselineGuideColor = new(255, 0, 0, 255);
    private static readonly double[] ExperimentThresholds = [0.48d, 0.50d, 0.52d, 0.54d, 0.56d, 0.58d, 0.60d];
    private static readonly double[] ExperimentSmoothingMultipliers = [0.5d, 1.0d, 1.5d];
    private const string CoordinateConventionNote =
        "Font outline coordinates use +Y up relative to the alphabetic baseline. Output image coordinates use +Y down from the top-left. GlyphFieldPlacement.PlaneTop/PlaneBottom are stored as image-down offsets relative to the baseline, so negative PlaneTop draws above the baseline and positive PlaneBottom draws below it.";

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
            Threshold: ProofThreshold,
            SmoothingMultiplier: ProofSmoothingMultiplier,
            ShowBaselineGuide: true,
            BaselineGuideColor: BaselineGuideColor,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2);
    }

    public static async Task<FontReferenceOracleExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        BrowserTextMetricsDocument? browserMetrics = TryLoadBrowserMetricsFromEnvironment();

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
        Dictionary<string, CoverageScanResult> machinaCoverageByFixture = [];
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = await LoadMetricsAsync(source, cancellationToken);
        DistanceFieldTextRenderOptions renderOptions = CreateRenderOptions();
        Dictionary<int, DistanceFieldPageReference> pages = LoadPages(export.PagePaths, export.Snapshot);

        foreach (FontReferenceOracleDefinition definition in Definitions)
        {
            FontProofArtifact artifact = AssertSingleArtifact(export.Artifacts, definition.MachinaPpmFileName);
            string pngPath = Path.Combine(fullOutputDirectory, definition.MachinaPngFileName);
            RgbaPngWriter.Write(pngPath, artifact.Image);
            machinaCoverageByFixture[definition.Id] = CoverageMetrics.Scan(
                artifact.Image,
                Foreground,
                Background,
                ProofBaselineY,
                BaselineGuideColor);

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
            layouts,
            machinaCoverageByFixture,
            browserMetrics);

        string reportTextPath = Path.Combine(fullOutputDirectory, PlacementReportTextFileName);
        string reportJsonPath = Path.Combine(fullOutputDirectory, PlacementReportJsonFileName);
        File.WriteAllText(reportTextPath, BuildTextReport(report));
        File.WriteAllText(reportJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        CoverageExperimentReport experiment = BuildCoverageExperimentReport(
            export.Snapshot,
            pages,
            layouts,
            machinaCoverageByFixture,
            browserMetrics);
        string coverageExperimentPath = Path.Combine(fullOutputDirectory, CoverageExperimentFileName);
        File.WriteAllText(coverageExperimentPath, JsonSerializer.Serialize(experiment, JsonOptions));

        return new FontReferenceOracleExportResult(
            OutputDirectory: fullOutputDirectory,
            TomlPath: export.TomlPath!,
            PagePaths: export.PagePaths,
            Artifacts: artifacts,
            BrowserMetricsJsonPath: ResolveBrowserMetricsPath(),
            PlacementReportTextPath: reportTextPath,
            PlacementReportJsonPath: reportJsonPath,
            CoverageExperimentJsonPath: coverageExperimentPath,
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
        builder.AppendLine("BaselineGuideEnabled: true");
        builder.AppendLine($"BaselineGuideY: {ProofBaselineY}");
        builder.AppendLine($"BaselineGuideColor: {ToHexColor(BaselineGuideColor)}");
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
        IReadOnlyDictionary<string, DistanceFieldTextLayoutResult> layouts,
        IReadOnlyDictionary<string, CoverageScanResult> machinaCoverageByFixture,
        BrowserTextMetricsDocument? browserMetrics)
    {
        List<FontReferenceOracleFixtureReport> fixtures = [];
        IReadOnlyDictionary<string, BrowserTextMetricsFixture> browserMetricsByFixture =
            browserMetrics?.Fixtures.ToDictionary(static fixture => fixture.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, BrowserTextMetricsFixture>(StringComparer.Ordinal);

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
                    : ComputeFieldPlacement(placement, atlasEntry);

                rows.Add(new FontReferenceOracleGlyphRow(
                    index,
                    FormatCharacter(key.Codepoint),
                    $"U+{key.Codepoint:X4}",
                    key.Codepoint,
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
                    placement.X,
                    placement.BaselineY,
                    fieldPlacement?.DrawX,
                    fieldPlacement?.DrawY,
                    fieldPlacement?.OutputWidth,
                    fieldPlacement?.OutputHeight,
                    atlasEntry?.PageIndex,
                    atlasEntry?.X,
                    atlasEntry?.Y,
                    atlasEntry?.Width,
                    atlasEntry?.Height,
                    atlasEntry?.U0,
                    atlasEntry?.V0,
                    atlasEntry?.U1,
                    atlasEntry?.V1,
                    atlasEntry?.Placement.PlaneLeft,
                    atlasEntry?.Placement.PlaneTop,
                    atlasEntry?.Placement.PlaneRight,
                    atlasEntry?.Placement.PlaneBottom,
                    atlasEntry?.Placement.PixelRange,
                    atlasEntry?.Placement.ProjectionScale,
                    isWhitespace));

                runningPenX = penAfterAdjustment + metrics.Advance;
                previousKey = key;
                previousWasWhitespace = isWhitespace;
            }

            double? minPlaneTop = MinOrNull(rows.Select(static row => row.PlaneTop));
            double? maxPlaneBottom = MaxOrNull(rows.Select(static row => row.PlaneBottom));
            double? computedTextTop = minPlaneTop is null ? null : ProofBaselineY + minPlaneTop.Value;
            double? computedTextBottom = maxPlaneBottom is null ? null : ProofBaselineY + maxPlaneBottom.Value;
            CoverageScanResult? machinaCoverage = machinaCoverageByFixture.TryGetValue(definition.Id, out CoverageScanResult? bounds)
                ? bounds
                : null;
            BrowserTextMetricsFixture? browserFixture = browserMetricsByFixture.TryGetValue(definition.Id, out BrowserTextMetricsFixture? metricsFixture)
                ? metricsFixture
                : null;
            BrowserCoverageMetrics? browserCoverage = browserFixture?.Coverage;

            fixtures.Add(new FontReferenceOracleFixtureReport(
                definition.Id,
                definition.Text,
                layout.Width,
                TypographyKerningFixtureFont.Face.Value,
                ProofEmSize,
                ProofWidth,
                ProofHeight,
                ProofBaselineY,
                true,
                ProofBaselineY,
                BaselineGuideColor,
                computedTextTop,
                computedTextBottom,
                minPlaneTop,
                maxPlaneBottom,
                machinaCoverage?.InkTop,
                machinaCoverage?.InkBottom,
                machinaCoverage?.InkLeft,
                machinaCoverage?.InkRight,
                machinaCoverage?.InkHeight ?? 0,
                machinaCoverage?.InkWidth ?? 0,
                machinaCoverage?.AlphaCoverageCountAbove001 ?? 0,
                machinaCoverage?.AlphaCoverageCountAbove010 ?? 0,
                machinaCoverage?.AlphaCoverageCountAbove050 ?? 0,
                machinaCoverage?.MaxAlpha ?? 0d,
                machinaCoverage?.AverageAlphaNonZero ?? 0d,
                machinaCoverage?.DescentBelowBaseline,
                browserCoverage?.InkTop,
                browserCoverage?.InkBottom,
                browserCoverage?.InkLeft,
                browserCoverage?.InkRight,
                browserCoverage?.DescentBelowBaseline,
                browserFixture,
                CreateBrowserVerticalMetrics(browserFixture),
                rows));
        }

        return new FontReferenceOraclePlacementReport(
            FontPath: TypographyKerningFixtureFont.FontPath,
            FontFace: TypographyKerningFixtureFont.Face.Value,
            EmSize: ProofEmSize,
            OutputWidth: ProofWidth,
            OutputHeight: ProofHeight,
            OriginX: ProofOriginX,
            BaselineY: ProofBaselineY,
            BaselineGuideEnabled: true,
            BaselineGuideY: ProofBaselineY,
            BaselineGuideColor: BaselineGuideColor,
            CoordinateConvention: CoordinateConventionNote,
            Fixtures: fixtures);
    }

    private static FieldPlacementDetails ComputeFieldPlacement(
        DistanceFieldGlyphPlacement placement,
        GlyphAtlasEntry entry)
    {
        DistanceFieldGlyphDrawBounds drawBounds = CpuDistanceFieldGlyphRenderer.ComputeDrawBounds(placement, entry);

        return new FieldPlacementDetails(
            drawBounds.Width,
            drawBounds.Height,
            drawBounds.X,
            drawBounds.Y);
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
            Threshold: ProofThreshold,
            SmoothingMultiplier: ProofSmoothingMultiplier,
            ShowBaselineGuide: true,
            BaselineGuideColor: BaselineGuideColor,
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
        builder.AppendLine($"fontFace: {report.FontFace}");
        builder.AppendLine($"emSize: {report.EmSize}");
        builder.AppendLine($"output: {report.OutputWidth}x{report.OutputHeight}");
        builder.AppendLine($"originX: {report.OriginX}");
        builder.AppendLine($"baselineY: {report.BaselineY}");
        builder.AppendLine($"baselineGuideEnabled: {report.BaselineGuideEnabled.ToString().ToLowerInvariant()}");
        builder.AppendLine($"baselineGuideY: {report.BaselineGuideY:0.###}");
        builder.AppendLine($"baselineGuideColor: {ToHexColor(report.BaselineGuideColor)}");
        builder.AppendLine($"coordinateConvention: {report.CoordinateConvention}");
        builder.AppendLine();

        foreach (FontReferenceOracleFixtureReport fixture in report.Fixtures)
        {
            builder.AppendLine($"[{fixture.Id}] {fixture.Text}");
            builder.AppendLine($"fontFace: {fixture.FontFace}");
            builder.AppendLine($"emSize: {fixture.EmSize}");
            builder.AppendLine($"output: {fixture.OutputWidth}x{fixture.OutputHeight}");
            builder.AppendLine($"baselineY: {fixture.BaselineY}");
            builder.AppendLine($"baselineGuideEnabled: {fixture.BaselineGuideEnabled.ToString().ToLowerInvariant()}");
            builder.AppendLine($"baselineGuideY: {fixture.BaselineGuideY:0.###}");
            builder.AppendLine($"baselineGuideColor: {ToHexColor(fixture.BaselineGuideColor)}");
            builder.AppendLine($"layoutWidth: {fixture.LayoutWidth:0.###}");
            builder.AppendLine($"computedTextTop: {FormatNullable(fixture.ComputedTextTop)}");
            builder.AppendLine($"computedTextBottom: {FormatNullable(fixture.ComputedTextBottom)}");
            builder.AppendLine($"minPlaneTop: {FormatNullable(fixture.MinPlaneTop)}");
            builder.AppendLine($"maxPlaneBottom: {FormatNullable(fixture.MaxPlaneBottom)}");
            builder.AppendLine($"inkTop: {FormatNullable(fixture.InkTop)}");
            builder.AppendLine($"inkBottom: {FormatNullable(fixture.InkBottom)}");
            builder.AppendLine($"inkLeft: {FormatNullable(fixture.InkLeft)}");
            builder.AppendLine($"inkRight: {FormatNullable(fixture.InkRight)}");
            builder.AppendLine($"inkHeight: {fixture.InkHeight}");
            builder.AppendLine($"inkWidth: {fixture.InkWidth}");
            builder.AppendLine($"alphaCoverageCount_above_001: {fixture.AlphaCoverageCountAbove001}");
            builder.AppendLine($"alphaCoverageCount_above_010: {fixture.AlphaCoverageCountAbove010}");
            builder.AppendLine($"alphaCoverageCount_above_050: {fixture.AlphaCoverageCountAbove050}");
            builder.AppendLine($"maxAlpha: {fixture.MaxAlpha:0.###}");
            builder.AppendLine($"averageAlphaNonZero: {fixture.AverageAlphaNonZero:0.###}");
            builder.AppendLine($"descentBelowBaseline: {FormatNullable(fixture.DescentBelowBaseline)}");
            builder.AppendLine($"browserInkTop: {FormatNullable(fixture.BrowserInkTop)}");
            builder.AppendLine($"browserInkBottom: {FormatNullable(fixture.BrowserInkBottom)}");
            builder.AppendLine($"browserInkLeft: {FormatNullable(fixture.BrowserInkLeft)}");
            builder.AppendLine($"browserInkRight: {FormatNullable(fixture.BrowserInkRight)}");
            builder.AppendLine($"browserDescentBelowBaseline: {FormatNullable(fixture.BrowserDescentBelowBaseline)}");

            if (fixture.BrowserVerticalMetrics is not null)
            {
                builder.AppendLine($"browserTextBaseline: {fixture.Browser?.TextBaseline ?? "not available"}");
                builder.AppendLine($"browserTextAlign: {fixture.Browser?.TextAlign ?? "not available"}");
                builder.AppendLine($"browserBaselineY: {FormatNullable(fixture.Browser?.BaselineY)}");
                builder.AppendLine($"browserBaselineGuideEnabled: {FormatNullable(fixture.Browser?.BaselineGuideEnabled)}");
                builder.AppendLine($"browserBaselineGuideY: {FormatNullable(fixture.Browser?.BaselineGuideY)}");
                builder.AppendLine($"browserBaselineGuideColor: {fixture.Browser?.BaselineGuideColor ?? "not available"}");
                builder.AppendLine($"browserActualTop: {FormatNullable(fixture.BrowserVerticalMetrics.ActualTop)}");
                builder.AppendLine($"browserActualBottom: {FormatNullable(fixture.BrowserVerticalMetrics.ActualBottom)}");
                builder.AppendLine($"browserFontTop: {FormatNullable(fixture.BrowserVerticalMetrics.FontTop)}");
                builder.AppendLine($"browserFontBottom: {FormatNullable(fixture.BrowserVerticalMetrics.FontBottom)}");
                builder.AppendLine($"browserEmTop: {FormatNullable(fixture.BrowserVerticalMetrics.EmTop)}");
                builder.AppendLine($"browserEmBottom: {FormatNullable(fixture.BrowserVerticalMetrics.EmBottom)}");
                builder.AppendLine($"browserActualBoundingBoxAscent: {FormatNullable(fixture.BrowserVerticalMetrics.ActualBoundingBoxAscent)}");
                builder.AppendLine($"browserActualBoundingBoxDescent: {FormatNullable(fixture.BrowserVerticalMetrics.ActualBoundingBoxDescent)}");
                builder.AppendLine($"browserFontBoundingBoxAscent: {FormatNullable(fixture.BrowserVerticalMetrics.FontBoundingBoxAscent)}");
                builder.AppendLine($"browserFontBoundingBoxDescent: {FormatNullable(fixture.BrowserVerticalMetrics.FontBoundingBoxDescent)}");
            }

            builder.AppendLine("index\tchar\tcodepoint\tcodepointValue\tglyphKey\tadvance\tbearingX\tbearingY\tmetricsWidth\tmetricsHeight\tpairAdjustX\tpairAdjustY\tpenBefore\tpenAfter\tpenX\tbaselineY\tdrawX\tdrawY\tdrawWidth\tdrawHeight\tatlasPage\tatlasRect\tuv0\tuv1\tplaneBounds\tpixelRange\tprojectionScale\twhitespace");

            foreach (FontReferenceOracleGlyphRow row in fixture.Glyphs)
            {
                builder.AppendLine(
                    string.Join(
                        '\t',
                        row.Index,
                        row.Character,
                        row.Codepoint,
                        row.CodepointValue,
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
                        FormatNullable(row.PenX),
                        FormatNullable(row.BaselineY),
                        FormatNullable(row.DrawX),
                        FormatNullable(row.DrawY),
                        FormatNullable(row.DrawWidth),
                        FormatNullable(row.DrawHeight),
                        FormatNullable(row.AtlasPage),
                        $"{FormatNullable(row.AtlasRectX)},{FormatNullable(row.AtlasRectY)},{FormatNullable(row.AtlasRectWidth)},{FormatNullable(row.AtlasRectHeight)}",
                        $"{FormatNullable(row.U0)},{FormatNullable(row.V0)}",
                        $"{FormatNullable(row.U1)},{FormatNullable(row.V1)}",
                        $"{FormatNullable(row.PlaneLeft)},{FormatNullable(row.PlaneTop)},{FormatNullable(row.PlaneRight)},{FormatNullable(row.PlaneBottom)}",
                        FormatNullable(row.PixelRange),
                        FormatNullable(row.ProjectionScale),
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

    private static string FormatNullable(bool? value)
    {
        return value?.ToString().ToLowerInvariant() ?? "not available";
    }

    private static string ToHexColor(Rgba32 color)
    {
        return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    private static BrowserVerticalMetrics? CreateBrowserVerticalMetrics(BrowserTextMetricsFixture? fixture)
    {
        if (fixture is null)
        {
            return null;
        }

        return new BrowserVerticalMetrics(
            fixture.Metrics.ActualBoundingBoxAscent,
            fixture.Metrics.ActualBoundingBoxDescent,
            fixture.Metrics.FontBoundingBoxAscent,
            fixture.Metrics.FontBoundingBoxDescent,
            fixture.Metrics.EmHeightAscent,
            fixture.Metrics.EmHeightDescent,
            fixture.Metrics.AlphabeticBaseline,
            fixture.Metrics.HangingBaseline,
            fixture.Metrics.IdeographicBaseline,
            ComputeTop(fixture.BaselineY, fixture.Metrics.ActualBoundingBoxAscent),
            ComputeBottom(fixture.BaselineY, fixture.Metrics.ActualBoundingBoxDescent),
            ComputeTop(fixture.BaselineY, fixture.Metrics.FontBoundingBoxAscent),
            ComputeBottom(fixture.BaselineY, fixture.Metrics.FontBoundingBoxDescent),
            ComputeTop(fixture.BaselineY, fixture.Metrics.EmHeightAscent),
            ComputeBottom(fixture.BaselineY, fixture.Metrics.EmHeightDescent));
    }

    private static double? ComputeTop(double baselineY, double? ascent)
    {
        return ascent is null ? null : baselineY - ascent.Value;
    }

    private static double? ComputeBottom(double baselineY, double? descent)
    {
        return descent is null ? null : baselineY + descent.Value;
    }

    private static Dictionary<int, DistanceFieldPageReference> LoadPages(
        IReadOnlyList<string> pagePaths,
        FontAtlasSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(pagePaths);

        if (snapshot is null)
        {
            throw new InvalidOperationException("Expected a snapshot when loading proof pages.");
        }

        Dictionary<int, DistanceFieldPageReference> pages = [];
        foreach (FontAtlasPage page in snapshot.Pages)
        {
            string? pagePath = pagePaths.FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), page.ImagePath, StringComparison.OrdinalIgnoreCase));
            if (pagePath is null)
            {
                throw new InvalidOperationException($"Missing exported page artifact for page {page.Index}.");
            }

            pages[page.Index] = DistanceFieldPageReferenceReader.Read(pagePath);
        }

        return pages;
    }

    private static CoverageExperimentReport BuildCoverageExperimentReport(
        FontAtlasSnapshot? snapshot,
        IReadOnlyDictionary<int, DistanceFieldPageReference> pages,
        IReadOnlyDictionary<string, DistanceFieldTextLayoutResult> layouts,
        IReadOnlyDictionary<string, CoverageScanResult> defaultCoverageByFixture,
        BrowserTextMetricsDocument? browserMetrics)
    {
        if (snapshot is null)
        {
            throw new InvalidOperationException("Expected a snapshot when building the coverage experiment.");
        }

        IReadOnlyDictionary<string, BrowserTextMetricsFixture> browserMetricsByFixture =
            browserMetrics?.Fixtures.ToDictionary(static fixture => fixture.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, BrowserTextMetricsFixture>(StringComparer.Ordinal);
        List<CoverageExperimentFixtureReport> fixtures = [];

        foreach (FontReferenceOracleDefinition definition in Definitions)
        {
            DistanceFieldTextLayoutResult layout = layouts[definition.Id];
            CoverageScanResult defaultCoverage = defaultCoverageByFixture[definition.Id];
            BrowserTextMetricsFixture? browserFixture = browserMetricsByFixture.TryGetValue(definition.Id, out BrowserTextMetricsFixture? browserMatch)
                ? browserMatch
                : null;
            List<CoverageExperimentMeasurement> measurements = [];

            foreach (double threshold in ExperimentThresholds)
            {
                foreach (double smoothingMultiplier in ExperimentSmoothingMultipliers)
                {
                    RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
                        snapshot,
                        new Dictionary<int, DistanceFieldPageReference>(pages),
                        layout,
                        CreateRenderOptions() with
                        {
                            Threshold = threshold,
                            SmoothingMultiplier = smoothingMultiplier,
                        });

                    CoverageScanResult coverage = CoverageMetrics.Scan(
                        image,
                        Foreground,
                        Background,
                        ProofBaselineY,
                        BaselineGuideColor);
                    double? browserDescent = browserFixture?.Coverage?.DescentBelowBaseline;

                    measurements.Add(new CoverageExperimentMeasurement(
                        threshold,
                        smoothingMultiplier,
                        coverage.InkTop,
                        coverage.InkBottom,
                        coverage.InkHeight,
                        coverage.AlphaCoverageCountAbove001,
                        coverage.AlphaCoverageCountAbove010,
                        coverage.AlphaCoverageCountAbove050,
                        coverage.MaxAlpha,
                        coverage.AverageAlphaNonZero,
                        coverage.DescentBelowBaseline,
                        browserDescent is null || coverage.DescentBelowBaseline is null
                            ? null
                            : coverage.DescentBelowBaseline.Value - browserDescent.Value));
                }
            }

            fixtures.Add(new CoverageExperimentFixtureReport(
                definition.Id,
                definition.Text,
                defaultCoverage.DescentBelowBaseline,
                browserFixture?.Coverage?.DescentBelowBaseline,
                measurements));
        }

        return new CoverageExperimentReport(
            ProofBaselineY,
            ExperimentThresholds,
            ExperimentSmoothingMultipliers,
            fixtures);
    }

    internal static CoverageScanResult ScanCoverageForTest(
        RgbaImage image,
        double baselineY,
        Rgba32? ignoredColor = null)
    {
        return CoverageMetrics.Scan(image, Foreground, Background, baselineY, ignoredColor);
    }

    internal static FontReferenceOraclePlacementReport ReadPlacementReportForTest(string outputDirectory)
    {
        return JsonSerializer.Deserialize<FontReferenceOraclePlacementReport>(
            File.ReadAllText(Path.Combine(outputDirectory, PlacementReportJsonFileName)),
            JsonReadOptions)
            ?? throw new InvalidOperationException("Placement report could not be read.");
    }

    internal static CoverageExperimentReport ReadCoverageExperimentForTest(string outputDirectory)
    {
        return JsonSerializer.Deserialize<CoverageExperimentReport>(
            File.ReadAllText(Path.Combine(outputDirectory, CoverageExperimentFileName)),
            JsonReadOptions)
            ?? throw new InvalidOperationException("Coverage experiment report could not be read.");
    }

    internal static BrowserTextMetricsDocument ReadBrowserMetricsForTest(string outputDirectory)
    {
        return JsonSerializer.Deserialize<BrowserTextMetricsDocument>(
            File.ReadAllText(Path.Combine(outputDirectory, BrowserTextMetricsFileName)),
            JsonReadOptions)
            ?? throw new InvalidOperationException("Browser metrics document could not be read.");
    }

    private static double? MinOrNull(IEnumerable<double?> values)
    {
        double[] collected = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return collected.Length == 0 ? null : collected.Min();
    }

    private static double? MaxOrNull(IEnumerable<double?> values)
    {
        double[] collected = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return collected.Length == 0 ? null : collected.Max();
    }

    private static BrowserTextMetricsDocument? TryLoadBrowserMetricsFromEnvironment()
    {
        string? path = ResolveBrowserMetricsPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BrowserTextMetricsDocument>(json, JsonReadOptions);
    }

    private static string? ResolveBrowserMetricsPath()
    {
        string? requested = Environment.GetEnvironmentVariable(BrowserMetricsPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(requested)
            ? null
            : Path.GetFullPath(requested);
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

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record FieldPlacementDetails(
        int OutputWidth,
        int OutputHeight,
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
    string? BrowserMetricsJsonPath,
    string PlacementReportTextPath,
    string PlacementReportJsonPath,
    string CoverageExperimentJsonPath,
    string FontPath,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    double OriginX,
    double BaselineY);

internal sealed record FontReferenceOraclePlacementReport(
    string FontPath,
    string FontFace,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    double OriginX,
    double BaselineY,
    bool BaselineGuideEnabled,
    double BaselineGuideY,
    Rgba32 BaselineGuideColor,
    string CoordinateConvention,
    IReadOnlyList<FontReferenceOracleFixtureReport> Fixtures);

internal sealed record FontReferenceOracleFixtureReport(
    string Id,
    string Text,
    double LayoutWidth,
    string FontFace,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    double BaselineY,
    bool BaselineGuideEnabled,
    double BaselineGuideY,
    Rgba32 BaselineGuideColor,
    double? ComputedTextTop,
    double? ComputedTextBottom,
    double? MinPlaneTop,
    double? MaxPlaneBottom,
    int? InkTop,
    int? InkBottom,
    int? InkLeft,
    int? InkRight,
    int InkHeight,
    int InkWidth,
    int AlphaCoverageCountAbove001,
    int AlphaCoverageCountAbove010,
    int AlphaCoverageCountAbove050,
    double MaxAlpha,
    double AverageAlphaNonZero,
    double? DescentBelowBaseline,
    int? BrowserInkTop,
    int? BrowserInkBottom,
    int? BrowserInkLeft,
    int? BrowserInkRight,
    double? BrowserDescentBelowBaseline,
    BrowserTextMetricsFixture? Browser,
    BrowserVerticalMetrics? BrowserVerticalMetrics,
    IReadOnlyList<FontReferenceOracleGlyphRow> Glyphs);

internal sealed record FontReferenceOracleGlyphRow(
    int Index,
    string Character,
    string Codepoint,
    int CodepointValue,
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
    double PenX,
    double BaselineY,
    int? DrawX,
    int? DrawY,
    int? DrawWidth,
    int? DrawHeight,
    int? AtlasPage,
    int? AtlasRectX,
    int? AtlasRectY,
    int? AtlasRectWidth,
    int? AtlasRectHeight,
    double? U0,
    double? V0,
    double? U1,
    double? V1,
    double? PlaneLeft,
    double? PlaneTop,
    double? PlaneRight,
    double? PlaneBottom,
    double? PixelRange,
    double? ProjectionScale,
    bool IsWhitespace);

internal sealed record BrowserTextMetricsDocument(
    string? GeneratedAtUtc,
    string? BrowserPath,
    string? FixtureHtmlPath,
    IReadOnlyList<BrowserTextMetricsFixture> Fixtures);

internal sealed record BrowserTextMetricsFixture(
    string Id,
    string Text,
    string FontFamily,
    double FontSize,
    int CanvasWidth,
    int CanvasHeight,
    double X,
    double BaselineY,
    bool BaselineGuideEnabled,
    double? BaselineGuideY,
    string? BaselineGuideColor,
    string TextBaseline,
    string TextAlign,
    BrowserTextMetricValues Metrics,
    BrowserCoverageMetrics? Coverage,
    string? UnavailableReason = null);

internal sealed record BrowserTextMetricValues(
    double? Width,
    double? ActualBoundingBoxLeft,
    double? ActualBoundingBoxRight,
    double? ActualBoundingBoxAscent,
    double? ActualBoundingBoxDescent,
    double? FontBoundingBoxAscent,
    double? FontBoundingBoxDescent,
    double? EmHeightAscent,
    double? EmHeightDescent,
    double? AlphabeticBaseline,
    double? HangingBaseline,
    double? IdeographicBaseline);

internal sealed record BrowserCoverageMetrics(
    int? InkTop,
    int? InkBottom,
    int? InkLeft,
    int? InkRight,
    int InkHeight,
    int InkWidth,
    int AlphaCoverageCountAbove001,
    int AlphaCoverageCountAbove010,
    int AlphaCoverageCountAbove050,
    double MaxAlpha,
    double AverageAlphaNonZero,
    double BaselineY,
    double? DescentBelowBaseline);

internal sealed record BrowserVerticalMetrics(
    double? ActualBoundingBoxAscent,
    double? ActualBoundingBoxDescent,
    double? FontBoundingBoxAscent,
    double? FontBoundingBoxDescent,
    double? EmHeightAscent,
    double? EmHeightDescent,
    double? AlphabeticBaseline,
    double? HangingBaseline,
    double? IdeographicBaseline,
    double? ActualTop,
    double? ActualBottom,
    double? FontTop,
    double? FontBottom,
    double? EmTop,
    double? EmBottom);

internal sealed record CoverageExperimentReport(
    double BaselineY,
    IReadOnlyList<double> Thresholds,
    IReadOnlyList<double> SmoothingMultipliers,
    IReadOnlyList<CoverageExperimentFixtureReport> Fixtures);

internal sealed record CoverageExperimentFixtureReport(
    string Id,
    string Text,
    double? DefaultDescentBelowBaseline,
    double? BrowserDescentBelowBaseline,
    IReadOnlyList<CoverageExperimentMeasurement> Measurements);

internal sealed record CoverageExperimentMeasurement(
    double Threshold,
    double SmoothingMultiplier,
    int? InkTop,
    int? InkBottom,
    int InkHeight,
    int AlphaCoverageCountAbove001,
    int AlphaCoverageCountAbove010,
    int AlphaCoverageCountAbove050,
    double MaxAlpha,
    double AverageAlphaNonZero,
    double? DescentBelowBaseline,
    double? DeltaVsBrowserDescent);
