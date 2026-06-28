using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Rendering;

internal static class ThreeWayShapeDiffWorkflow
{
    public const string OutputDirectoryEnvironmentVariable = "MACHINA_FONT_SHAPE_DIFF_OUTPUT_DIR";
    public const string BrowserCapturePathEnvironmentVariable = "MACHINA_FONT_SHAPE_DIFF_BROWSER_CAPTURE_PATH";
    public const string BrowserCaptureFileName = "browser-shape-diff-captures.json";
    public const string ReportJsonFileName = "shape-diff-report.json";
    public const string ReportTextFileName = "shape-diff-report.txt";
    public const string ManualInstructionsFileName = "manual-shape-diff-instructions.txt";

    private static readonly Rgba32 Background = new(16, 16, 24, 255);
    private static readonly Rgba32 Foreground = new(240, 240, 240, 255);
    private static readonly Rgba32 BaselineGuideColor = new(255, 0, 0, 255);
    private static readonly Rgba32 OverlayBackground = new(10, 10, 14, 255);
    private static readonly Rgba32 BrowserOnlyColor = new(0, 220, 255, 255);
    private static readonly Rgba32 DirectOnlyColor = new(96, 255, 96, 255);
    private static readonly Rgba32 MsdfOnlyColor = new(255, 148, 32, 255);
    private static readonly Rgba32 OverlapColor = new(255, 255, 255, 255);
    private static readonly Rgba32 WireframeColor = new(255, 204, 96, 255);

    private static readonly InkMaskExtractionOptions MaskExtractionOptions = new(
        Background,
        BaselineGuideColor,
        InkDistanceThreshold: 12,
        BaselineDistanceThreshold: 24);

    public static IReadOnlyList<ShapeDiffTextDefinition> TextDefinitions { get; } =
    [
        new("machina", "Machina"),
        new("hello-machina", "Hello Machina"),
        new("kerning", "AV To Ta Wa Yo"),
        new("aa0", "Aa0"),
        new("a-space-a", "A A"),
    ];

    public static IReadOnlyList<ShapeDiffCanvasDefinition> CanvasDefinitions { get; } =
    [
        new(32, 320, 64, 8d, 40d),
        new(48, 480, 96, 12d, 60d),
        new(64, 640, 128, 16d, 80d),
    ];

    public static string FontPath => TypographyKerningFixtureFont.FontPath;

    public static string GetRequestedOutputDirectoryOrCreateTemp()
    {
        string? requested = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8s", Guid.NewGuid().ToString("N"));
    }

    public static string BuildManualInstructions(string outputDirectory)
    {
        StringBuilder builder = new();
        builder.AppendLine("Automated browser shape-diff capture was not available.");
        builder.AppendLine("Open the reference fixture in Edge or Chrome, export the captures, then rerun the script.");
        builder.AppendLine();
        builder.AppendLine($"Output directory: {Path.GetFullPath(outputDirectory)}");
        builder.AppendLine($"Fixture font: {FontPath}");
        builder.AppendLine("Sizes:");
        foreach (ShapeDiffCanvasDefinition canvas in CanvasDefinitions)
        {
            builder.AppendLine($"- {canvas.SizePx}px => canvas {canvas.Width}x{canvas.Height}, x={canvas.OriginX}, baselineY={canvas.BaselineY}");
        }

        builder.AppendLine("Texts:");
        foreach (ShapeDiffTextDefinition definition in TextDefinitions)
        {
            builder.AppendLine($"- {definition.Text}");
        }

        builder.AppendLine("Baseline guide: enabled (#ff0000)");
        return builder.ToString();
    }

    public static async Task<ThreeWayShapeDiffExportResult> ExportAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        ShapeDiffBrowserCaptureDocument? browserCaptureDocument = TryLoadBrowserCaptureDocument();
        Dictionary<(int SizePx, string Id), ShapeDiffBrowserCaptureFixture> browserFixtures = browserCaptureDocument?.Fixtures
            .ToDictionary(static fixture => (fixture.SizePx, fixture.Id), StringComparerTuple.Ordinal)
            ?? new Dictionary<(int SizePx, string Id), ShapeDiffBrowserCaptureFixture>(StringComparerTuple.Ordinal);

        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();

        List<ThreeWayShapeDiffSizeReport> sizeReports = [];

        foreach (ShapeDiffCanvasDefinition canvas in CanvasDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string sizeDirectory = Path.Combine(fullOutputDirectory, canvas.SizeDirectoryName);
            Directory.CreateDirectory(sizeDirectory);
            FontProofExporter exporter = new(
                source,
                new MsdfSharpDistanceFieldGenerator(),
                CreateMetadata(canvas.SizePx));

            FontProofExportResult msdfExport = await exporter.ExportAsync(
                TextDefinitions.Select(definition => new FontProofArtifactDefinition(definition.MsdfPpmFileName, definition.Text)).ToArray(),
                CreateProofOptions(sizeDirectory, canvas),
                cancellationToken);

            if (!msdfExport.Success || msdfExport.Snapshot is null)
            {
                throw new InvalidOperationException($"MSDF proof export failed for {canvas.SizePx}px.");
            }

            Dictionary<GlyphKey, GlyphOutline> outlinesByGlyph = await LoadOutlinesAsync(source, canvas, cancellationToken);
            Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = outlinesByGlyph.ToDictionary(static item => item.Key, static item => item.Value.Metrics);
            List<ThreeWayShapeDiffFixtureReport> fixtureReports = [];

            foreach (ShapeDiffTextDefinition definition in TextDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                    definition.Text,
                    TypographyKerningFixtureFont.Face,
                    canvas.SizePx,
                    MachinaFontWeight.Regular,
                    MachinaFontSlant.Upright);

                Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = await CollectPairAdjustmentsAsync(source, run, cancellationToken);
                DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
                    run,
                    metricsByGlyph,
                    CreateRenderOptions(canvas),
                    pairAdjustments: pairAdjustments);

                FontProofArtifact msdfArtifact = AssertArtifact(msdfExport.Artifacts, definition.MsdfPpmFileName);
                string msdfPngPath = Path.Combine(sizeDirectory, definition.MsdfPngFileName);
                RgbaPngWriter.Write(msdfPngPath, msdfArtifact.Image);

                DirectOutlineMaskRenderOptions directOptions = CreateDirectOptions(canvas);
                InkMask directMask = DirectOutlineMaskRenderer.RenderMask(outlinesByGlyph, layout, directOptions);
                RgbaImage directImage = directMask.ToImage(
                    Foreground,
                    Background,
                    showBaselineGuide: true,
                    baselineY: canvas.BaselineY,
                    baselineGuideColor: BaselineGuideColor);
                string directPngPath = Path.Combine(sizeDirectory, definition.DirectOutlinePngFileName);
                RgbaPngWriter.Write(directPngPath, directImage);

                RgbaImage wireframeImage = DirectOutlineMaskRenderer.RenderWireframe(
                    outlinesByGlyph,
                    layout,
                    directOptions,
                    WireframeColor,
                    OverlayBackground);
                string wireframePath = Path.Combine(sizeDirectory, definition.WireframePngFileName);
                RgbaPngWriter.Write(wireframePath, wireframeImage);

                RgbaImage? browserImage = TryCreateBrowserImage(browserFixtures, canvas.SizePx, definition.Id);
                string browserPngPath = Path.Combine(sizeDirectory, definition.BrowserPngFileName);
                if (browserImage is not null)
                {
                    RgbaPngWriter.Write(browserPngPath, browserImage);
                }

                InkMask msdfMask = InkMask.FromImage(msdfArtifact.Image, MaskExtractionOptions);
                InkMask? browserMask = browserImage is null ? null : InkMask.FromImage(browserImage, MaskExtractionOptions);

                ThreeWayPairReport? browserVsDirect = browserMask is null
                    ? null
                    : BuildPairReport(
                        "browser-vs-direct",
                        "browser",
                        "direct-outline",
                        browserMask,
                        directMask,
                        canvas.BaselineY,
                        Path.Combine(sizeDirectory, definition.BrowserVsDirectDiffPngFileName));

                ThreeWayPairReport directVsMsdf = BuildPairReport(
                    "direct-vs-msdf",
                    "direct-outline",
                    "msdf",
                    directMask,
                    msdfMask,
                    canvas.BaselineY,
                    Path.Combine(sizeDirectory, definition.DirectVsMsdfDiffPngFileName));

                ThreeWayPairReport? browserVsMsdf = browserMask is null
                    ? null
                    : BuildPairReport(
                        "browser-vs-msdf",
                        "browser",
                        "msdf",
                        browserMask,
                        msdfMask,
                        canvas.BaselineY,
                        Path.Combine(sizeDirectory, definition.BrowserVsMsdfDiffPngFileName));

                string threeWayOverlayPath = Path.Combine(sizeDirectory, definition.ThreeWayOverlayPngFileName);
                if (browserMask is not null)
                {
                    RgbaImage overlay = ShapeDiffArtifactWriter.CreateThreeWayOverlay(
                        browserMask,
                        directMask,
                        msdfMask,
                        OverlayBackground,
                        BrowserOnlyColor,
                        DirectOnlyColor,
                        MsdfOnlyColor,
                        OverlapColor,
                        canvas.BaselineY,
                        BaselineGuideColor);
                    RgbaPngWriter.Write(threeWayOverlayPath, overlay);
                }

                PairMismatchFinding pairFinding = AnalyzeFixture(
                    browserVsDirect,
                    directVsMsdf,
                    browserVsMsdf);

                fixtureReports.Add(new ThreeWayShapeDiffFixtureReport(
                    definition.Id,
                    definition.Text,
                    browserImage is not null,
                    browserPngPath,
                    directPngPath,
                    msdfPngPath,
                    Path.Combine(sizeDirectory, definition.BrowserVsDirectDiffPngFileName),
                    Path.Combine(sizeDirectory, definition.DirectVsMsdfDiffPngFileName),
                    Path.Combine(sizeDirectory, definition.BrowserVsMsdfDiffPngFileName),
                    threeWayOverlayPath,
                    wireframePath,
                    browserVsDirect,
                    directVsMsdf,
                    browserVsMsdf,
                    pairFinding));
            }

            sizeReports.Add(new ThreeWayShapeDiffSizeReport(
                canvas.SizePx,
                sizeDirectory,
                canvas.Width,
                canvas.Height,
                canvas.OriginX,
                canvas.BaselineY,
                fixtureReports));
        }

        PairMismatchFinding overallFinding = AnalyzeAcrossSizes(sizeReports);
        ThreeWayShapeDiffReport report = new(
            FontPath,
            TypographyKerningFixtureFont.Face.Value,
            CanvasDefinitions.Select(static definition => definition.SizePx).ToArray(),
            TextDefinitions.Select(static definition => definition.Text).ToArray(),
            "Direct outline rasterization is diagnostic-only. Curves are flattened deterministically, filled with even-odd winding by default, and supersampled at 4x.",
            "Browser and MSDF masks use RGB threshold extraction. Background-like pixels are excluded, and pixels near the red baseline guide are ignored.",
            "Metrics are computed on extracted ink masks using IoU, bounds deltas, unique-area counts, and symmetric edge-distance summaries.",
            sizeReports,
            overallFinding);

        string reportJsonPath = Path.Combine(fullOutputDirectory, ReportJsonFileName);
        string reportTextPath = Path.Combine(fullOutputDirectory, ReportTextFileName);
        File.WriteAllText(reportJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(reportTextPath, BuildTextReport(report));

        return new ThreeWayShapeDiffExportResult(fullOutputDirectory, reportJsonPath, reportTextPath, report);
    }

    private static async Task<Dictionary<GlyphKey, GlyphOutline>> LoadOutlinesAsync(
        TypographyGlyphOutlineSource source,
        ShapeDiffCanvasDefinition canvas,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphKey, GlyphOutline> outlines = [];
        GlyphOutlineLoadOptions options = new(
            canvas.SizePx,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);

        foreach (GlyphKey key in TextDefinitions
                     .SelectMany(definition => DistanceFieldTextRun.Create(
                         definition.Text,
                         TypographyKerningFixtureFont.Face,
                         canvas.SizePx,
                         MachinaFontWeight.Regular,
                         MachinaFontSlant.Upright).GlyphKeys)
                     .Distinct())
        {
            GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
                key.Face,
                key.Codepoint,
                options,
                cancellationToken);

            if (!result.Success || result.Outline is null)
            {
                throw new InvalidOperationException($"Failed to load outline for U+{key.Codepoint:X4} at {canvas.SizePx}px.");
            }

            outlines[key] = result.Outline;
        }

        return outlines;
    }

    private static async Task<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        TypographyGlyphOutlineSource source,
        DistanceFieldTextRun run,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> adjustments = [];
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
                    adjustments[new GlyphPairKey(previous, key)] = adjustment;
                }
            }

            previousKey = key;
            previousWasWhitespace = isWhitespace;
        }

        return adjustments;
    }

    private static ThreeWayPairReport BuildPairReport(
        string comparisonId,
        string leftLabel,
        string rightLabel,
        InkMask left,
        InkMask right,
        double baselineY,
        string outputPath)
    {
        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY);
        PairMismatchFinding classification = ClassifyPair(metrics);

        RgbaImage overlay = ShapeDiffArtifactWriter.CreatePairwiseOverlay(
            left,
            right,
            OverlayBackground,
            leftLabel == "browser" ? BrowserOnlyColor : leftLabel == "direct-outline" ? DirectOnlyColor : MsdfOnlyColor,
            rightLabel == "browser" ? BrowserOnlyColor : rightLabel == "direct-outline" ? DirectOnlyColor : MsdfOnlyColor,
            OverlapColor,
            baselineY,
            BaselineGuideColor);
        RgbaPngWriter.Write(outputPath, overlay);

        return new ThreeWayPairReport(
            comparisonId,
            leftLabel,
            rightLabel,
            outputPath,
            metrics,
            classification);
    }

    private static PairMismatchFinding ClassifyPair(ShapeDiffMetrics metrics)
    {
        if (metrics.IntersectionOverUnion >= 0.98d && metrics.MaxEdgeDistance <= 0.5d)
        {
            return new PairMismatchFinding("unknown", 0.95d, "The pairwise masks are effectively identical at the extracted-mask level.");
        }

        int deltaLeft = Math.Abs(metrics.DeltaLeft ?? 0);
        int deltaTop = Math.Abs(metrics.DeltaTop ?? 0);
        int deltaRight = Math.Abs(metrics.DeltaRight ?? 0);
        int deltaBottom = Math.Abs(metrics.DeltaBottom ?? 0);

        if (deltaLeft <= 1 && deltaRight <= 1 && deltaTop >= 2 && deltaBottom >= 2)
        {
            return new PairMismatchFinding("global-shift", 0.65d, "Bounds align horizontally but shift vertically, which points to a shared coordinate-placement disagreement.");
        }

        if (metrics.BelowBaselineExtraArea > (metrics.AboveBaselineExtraArea * 1.5d))
        {
            return new PairMismatchFinding("vertical-overrun", 0.60d, "Mismatch is concentrated below the baseline, which suggests a vertical coverage overrun.");
        }

        if (Math.Abs(metrics.DeltaWidth ?? 0) >= 3 && Math.Abs(metrics.DeltaWidth ?? 0) > Math.Abs(metrics.DeltaHeight ?? 0))
        {
            return new PairMismatchFinding("horizontal-overrun", 0.55d, "Mismatch is dominated by width drift, which suggests a horizontal overrun or placement disagreement.");
        }

        if (metrics.RightInkArea > (metrics.LeftInkArea * 1.10d))
        {
            return new PairMismatchFinding("coverage-heavy", 0.55d, "The right-hand mask consistently covers more area than the left-hand mask.");
        }

        if (metrics.RightInkArea < (metrics.LeftInkArea * 0.90d))
        {
            return new PairMismatchFinding("coverage-light", 0.55d, "The right-hand mask consistently covers less area than the left-hand mask.");
        }

        return new PairMismatchFinding("unknown", 0.40d, "Mismatch is real but does not isolate to a single pairwise pattern.");
    }

    private static PairMismatchFinding AnalyzeFixture(
        ThreeWayPairReport? browserVsDirect,
        ThreeWayPairReport directVsMsdf,
        ThreeWayPairReport? browserVsMsdf)
    {
        if (browserVsDirect is null || browserVsMsdf is null)
        {
            return new PairMismatchFinding("unknown", 0.10d, "Browser capture was unavailable, so only the direct-outline vs MSDF path could be compared.");
        }

        double browserDirectIou = browserVsDirect.Metrics.IntersectionOverUnion;
        double directMsdfIou = directVsMsdf.Metrics.IntersectionOverUnion;
        double browserMsdfIou = browserVsMsdf.Metrics.IntersectionOverUnion;

        if (browserDirectIou >= 0.80d && directMsdfIou <= 0.70d)
        {
            double confidence = Math.Min(0.90d, 0.55d + (browserDirectIou - directMsdfIou));
            return new PairMismatchFinding(
                "msdf-render-mismatch",
                confidence,
                "Browser and direct-outline masks stay relatively close while the MSDF path diverges, so the mismatch most likely enters during distance-field generation or MSDF rendering.");
        }

        if (browserDirectIou <= 0.70d && directMsdfIou >= 0.80d)
        {
            double confidence = Math.Min(0.90d, 0.55d + (directMsdfIou - browserDirectIou));
            return new PairMismatchFinding(
                "outline-mismatch",
                confidence,
                "Direct-outline and MSDF masks stay relatively close while the browser differs, so the mismatch most likely enters before MSDF sampling.");
        }

        if (browserDirectIou <= 0.70d && directMsdfIou <= 0.70d && browserMsdfIou <= 0.70d)
        {
            return new PairMismatchFinding(
                "global-shift",
                0.45d,
                "All three paths differ materially, which suggests a remaining shared convention mismatch rather than a single isolated stage.");
        }

        return new PairMismatchFinding(
            "unknown",
            0.35d,
            "The three pairwise comparisons do not isolate one stage cleanly.");
    }

    private static PairMismatchFinding AnalyzeAcrossSizes(IReadOnlyList<ThreeWayShapeDiffSizeReport> sizeReports)
    {
        List<(int SizePx, PairMismatchFinding Finding)> findings = sizeReports
            .SelectMany(report => report.Fixtures.Select(fixture => (report.SizePx, fixture.Finding)))
            .ToList();

        if (findings.Count == 0)
        {
            return new PairMismatchFinding("unknown", 0d, "No size reports were generated.");
        }

        PairMismatchFinding strongest = findings
            .OrderByDescending(static item => item.Finding.Confidence)
            .ThenBy(static item => item.SizePx)
            .First().Finding;
        double avgBrowserDirect = AveragePairIou(sizeReports, static fixture => fixture.BrowserVsDirect);
        double avgDirectMsdf = AveragePairIou(sizeReports, static fixture => fixture.DirectVsMsdf);
        double avgBrowserMsdf = AveragePairIou(sizeReports, static fixture => fixture.BrowserVsMsdf);
        double avgDirectMsdf32 = AveragePairIouForSize(sizeReports, 32, static fixture => fixture.DirectVsMsdf);
        double avgDirectMsdf64 = AveragePairIouForSize(sizeReports, 64, static fixture => fixture.DirectVsMsdf);
        double avgBrowserDirect32 = AveragePairIouForSize(sizeReports, 32, static fixture => fixture.BrowserVsDirect);
        double avgBrowserDirect64 = AveragePairIouForSize(sizeReports, 64, static fixture => fixture.BrowserVsDirect);

        Dictionary<string, int> countsByKind = findings
            .GroupBy(static item => item.Finding.Kind, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        double avg32 = AverageOverallIou(sizeReports, 32);
        double avg64 = AverageOverallIou(sizeReports, 64);

        if (avg32 >= 0d && avg64 >= 0d)
        {
            if (avg64 + 0.08d < avg32)
            {
                if (avgBrowserDirect >= 0d
                    && avgDirectMsdf >= 0d
                    && avgBrowserDirect > avgDirectMsdf + 0.04d
                    && avgDirectMsdf32 >= 0d
                    && avgDirectMsdf64 >= 0d
                    && avgBrowserDirect32 >= 0d
                    && avgBrowserDirect64 >= 0d
                    && avgDirectMsdf32 > avgDirectMsdf64 + 0.15d
                    && Math.Abs(avgBrowserDirect32 - avgBrowserDirect64) <= 0.06d)
                {
                    return new PairMismatchFinding(
                        "msdf-render-mismatch",
                        0.88d,
                        $"Browser-vs-direct IoU stays relatively stable ({avgBrowserDirect32:0.000} at 32px vs {avgBrowserDirect64:0.000} at 64px), while direct-vs-MSDF drops sharply ({avgDirectMsdf32:0.000} to {avgDirectMsdf64:0.000}). The mismatch most likely enters in the MSDF generation or MSDF rendering stage, and it becomes more obvious at larger sizes.");
                }

                return new PairMismatchFinding(
                    strongest.Kind,
                    Math.Min(0.92d, strongest.Confidence + 0.05d),
                    $"{strongest.Notes} The mismatch grows at larger sizes ({avg32:0.000} IoU at 32px vs {avg64:0.000} at 64px), which points away from pure small-size hinting noise.");
            }

            if (avg32 + 0.08d < avg64)
            {
                return new PairMismatchFinding(
                    strongest.Kind,
                    Math.Min(0.88d, strongest.Confidence + 0.03d),
                    $"{strongest.Notes} The mismatch is worse at smaller sizes ({avg32:0.000} IoU at 32px vs {avg64:0.000} at 64px), so antialiasing or hinting differences may still be part of the picture.");
            }
        }

        if (countsByKind.TryGetValue(strongest.Kind, out int count) && count >= 3)
        {
            return new PairMismatchFinding(
                strongest.Kind,
                Math.Min(0.90d, strongest.Confidence + 0.05d),
                strongest.Notes);
        }

        return strongest;
    }

    private static double AveragePairIou(
        IReadOnlyList<ThreeWayShapeDiffSizeReport> reports,
        Func<ThreeWayShapeDiffFixtureReport, ThreeWayPairReport?> selector)
    {
        double[] values = reports
            .SelectMany(static report => report.Fixtures)
            .Select(selector)
            .Where(static pair => pair is not null)
            .Select(static pair => pair!.Metrics.IntersectionOverUnion)
            .ToArray();

        return values.Length == 0 ? -1d : values.Average();
    }

    private static double AveragePairIouForSize(
        IReadOnlyList<ThreeWayShapeDiffSizeReport> reports,
        int sizePx,
        Func<ThreeWayShapeDiffFixtureReport, ThreeWayPairReport?> selector)
    {
        ThreeWayShapeDiffSizeReport? report = reports.SingleOrDefault(item => item.SizePx == sizePx);
        if (report is null)
        {
            return -1d;
        }

        double[] values = report.Fixtures
            .Select(selector)
            .Where(static pair => pair is not null)
            .Select(static pair => pair!.Metrics.IntersectionOverUnion)
            .ToArray();

        return values.Length == 0 ? -1d : values.Average();
    }

    private static double AverageOverallIou(IReadOnlyList<ThreeWayShapeDiffSizeReport> reports, int sizePx)
    {
        ThreeWayShapeDiffSizeReport? report = reports.SingleOrDefault(item => item.SizePx == sizePx);
        if (report is null)
        {
            return -1d;
        }

        double[] values = report.Fixtures
            .Where(static fixture => fixture.BrowserVsMsdf is not null)
            .Select(static fixture => fixture.BrowserVsMsdf!.Metrics.IntersectionOverUnion)
            .ToArray();

        return values.Length == 0 ? -1d : values.Average();
    }

    private static FontProofExportOptions CreateProofOptions(string outputDirectory, ShapeDiffCanvasDefinition canvas)
    {
        return new FontProofExportOptions(
            outputDirectory,
            $"crimson-shape-diff-{canvas.SizePx}",
            TypographyKerningFixtureFont.Face,
            canvas.SizePx,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            canvas.Width,
            canvas.Height,
            32,
            32,
            4d,
            Foreground,
            Background,
            canvas.OriginX,
            canvas.BaselineY,
            ShowBaselineGuide: true,
            BaselineGuideColor: BaselineGuideColor,
            FlipY: true,
            PageWidth: 256,
            PageHeight: 256,
            PagePadding: 2);
    }

    private static DistanceFieldTextRenderOptions CreateRenderOptions(ShapeDiffCanvasDefinition canvas)
    {
        return new DistanceFieldTextRenderOptions(
            canvas.Width,
            canvas.Height,
            TypographyKerningFixtureFont.Face,
            canvas.SizePx,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            DistanceFieldKind.Msdf,
            32,
            32,
            4d,
            Foreground,
            Background,
            canvas.OriginX,
            canvas.BaselineY,
            ShowBaselineGuide: true,
            BaselineGuideColor: BaselineGuideColor,
            FlipY: true,
            PageWidth: 256,
            PageHeight: 256,
            PagePadding: 2,
            EdgeColoring: "simple",
            MiterLimit: 2d).Validate();
    }

    private static DirectOutlineMaskRenderOptions CreateDirectOptions(ShapeDiffCanvasDefinition canvas)
    {
        return new DirectOutlineMaskRenderOptions(
            canvas.Width,
            canvas.Height,
            Foreground,
            Background,
            canvas.OriginX,
            canvas.BaselineY,
            Supersample: 4,
            FillRule: OutlineFillRule.EvenOdd,
            CurveSubdivisionCount: 24,
            ShowBaselineGuide: true,
            BaselineGuideColor: BaselineGuideColor);
    }

    private static FontAtlasTomlExportMetadata CreateMetadata(double emSize)
    {
        return new FontAtlasTomlExportMetadata(
            "crimson-shape-diff",
            "msdf",
            "Crimson Text",
            "Regular",
            FontPath,
            ComputeFileSha256(FontPath),
            "OFL-1.1",
            new FontAtlasMetricsToml
            {
                EmSize = emSize,
                UnitsPerEm = 1000,
                Ascent = emSize * 0.8d,
                Descent = emSize * -0.2d,
                LineGap = 0,
                LineHeight = emSize,
            },
            new FontAtlasMsdfToml
            {
                Range = 4,
                Scale = 1,
                EdgeColoring = "simple",
                MiterLimit = 2,
            });
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FontProofArtifact AssertArtifact(IReadOnlyList<FontProofArtifact> artifacts, string fileName)
    {
        FontProofArtifact? artifact = artifacts.SingleOrDefault(
            item => string.Equals(Path.GetFileName(item.PpmPath), fileName, StringComparison.OrdinalIgnoreCase));

        return artifact ?? throw new InvalidOperationException($"Expected artifact '{fileName}' was not exported.");
    }

    private static RgbaImage? TryCreateBrowserImage(
        IReadOnlyDictionary<(int SizePx, string Id), ShapeDiffBrowserCaptureFixture> fixtures,
        int sizePx,
        string id)
    {
        if (!fixtures.TryGetValue((sizePx, id), out ShapeDiffBrowserCaptureFixture? fixture) || fixture.Capture is null)
        {
            return null;
        }

        if (!string.Equals(fixture.Capture.PixelFormat, "rgba8", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported browser capture pixel format '{fixture.Capture.PixelFormat}'.");
        }

        byte[] bytes = Convert.FromBase64String(fixture.Capture.RgbaBase64);
        int expectedLength = checked(fixture.Capture.Width * fixture.Capture.Height * 4);
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Browser capture byte length {bytes.Length} did not match expected RGBA size {expectedLength}.");
        }

        Rgba32[] pixels = new Rgba32[fixture.Capture.Width * fixture.Capture.Height];
        int offset = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = new Rgba32(
                bytes[offset++],
                bytes[offset++],
                bytes[offset++],
                bytes[offset++]);
        }

        return new RgbaImage(fixture.Capture.Width, fixture.Capture.Height, pixels);
    }

    private static ShapeDiffBrowserCaptureDocument? TryLoadBrowserCaptureDocument()
    {
        string? path = ResolveBrowserCapturePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ShapeDiffBrowserCaptureDocument>(File.ReadAllText(path), JsonReadOptions);
    }

    private static string? ResolveBrowserCapturePath()
    {
        string? requested = Environment.GetEnvironmentVariable(BrowserCapturePathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(requested)
            ? null
            : Path.GetFullPath(requested);
    }

    private static string BuildTextReport(ThreeWayShapeDiffReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina M8s three-way shape diff report");
        builder.AppendLine($"fontPath: {report.FontPath}");
        builder.AppendLine($"fontFace: {report.FontFace}");
        builder.AppendLine($"sizes: {string.Join(", ", report.FontSizes.Select(static value => value + "px"))}");
        builder.AppendLine($"texts: {string.Join(" | ", report.Texts)}");
        builder.AppendLine($"directOutlineRasterization: {report.DirectOutlineRasterizationPolicy}");
        builder.AppendLine($"maskPolicy: {report.MaskPolicy}");
        builder.AppendLine($"metricsPolicy: {report.MetricsPolicy}");
        builder.AppendLine($"overallFinding: {report.OverallFinding.Kind} (confidence {report.OverallFinding.Confidence:0.00})");
        builder.AppendLine($"overallNotes: {report.OverallFinding.Notes}");
        builder.AppendLine();

        foreach (ThreeWayShapeDiffSizeReport size in report.Sizes)
        {
            builder.AppendLine($"[{size.SizePx}px] canvas={size.CanvasWidth}x{size.CanvasHeight}, x={size.OriginX:0.###}, baselineY={size.BaselineY:0.###}");

            foreach (ThreeWayShapeDiffFixtureReport fixture in size.Fixtures)
            {
                builder.AppendLine($"  - {fixture.Text}");
                builder.AppendLine($"    finding: {fixture.Finding.Kind} (confidence {fixture.Finding.Confidence:0.00})");
                builder.AppendLine($"    notes: {fixture.Finding.Notes}");
                builder.AppendLine($"    browserPng: {fixture.BrowserPngPath}");
                builder.AppendLine($"    directOutlinePng: {fixture.DirectOutlinePngPath}");
                builder.AppendLine($"    msdfPng: {fixture.MsdfPngPath}");
                builder.AppendLine($"    browserVsDirect: {FormatPair(fixture.BrowserVsDirect)}");
                builder.AppendLine($"    directVsMsdf: {FormatPair(fixture.DirectVsMsdf)}");
                builder.AppendLine($"    browserVsMsdf: {FormatPair(fixture.BrowserVsMsdf)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatPair(ThreeWayPairReport? pair)
    {
        if (pair is null)
        {
            return "not available";
        }

        return $"{pair.Metrics.IntersectionOverUnion:0.000} IoU, meanEdge={pair.Metrics.MeanEdgeDistance:0.000}, p95={pair.Metrics.P95EdgeDistance:0.000}, class={pair.Classification.Kind}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static class StringComparerTuple
    {
        public static IEqualityComparer<(int SizePx, string Id)> Ordinal { get; } = new TupleComparer();

        private sealed class TupleComparer : IEqualityComparer<(int SizePx, string Id)>
        {
            public bool Equals((int SizePx, string Id) left, (int SizePx, string Id) right)
            {
                return left.SizePx == right.SizePx
                    && string.Equals(left.Id, right.Id, StringComparison.Ordinal);
            }

            public int GetHashCode((int SizePx, string Id) value)
            {
                return HashCode.Combine(value.SizePx, StringComparer.Ordinal.GetHashCode(value.Id));
            }
        }
    }
}

internal sealed record ShapeDiffTextDefinition(string Id, string Text)
{
    public string BrowserPngFileName => $"browser-{Id}.png";

    public string DirectOutlinePngFileName => $"direct-outline-{Id}.png";

    public string MsdfPpmFileName => $"msdf-{Id}.ppm";

    public string MsdfPngFileName => $"msdf-{Id}.png";

    public string BrowserVsDirectDiffPngFileName => $"diff-browser-vs-direct-{Id}.png";

    public string DirectVsMsdfDiffPngFileName => $"diff-direct-vs-msdf-{Id}.png";

    public string BrowserVsMsdfDiffPngFileName => $"diff-browser-vs-msdf-{Id}.png";

    public string ThreeWayOverlayPngFileName => $"overlay-three-way-{Id}.png";

    public string WireframePngFileName => $"wireframe-{Id}.png";
}

internal sealed record ShapeDiffCanvasDefinition(
    int SizePx,
    int Width,
    int Height,
    double OriginX,
    double BaselineY)
{
    public string SizeDirectoryName => SizePx.ToString();
}

internal sealed record ThreeWayPairReport(
    string ComparisonId,
    string LeftLabel,
    string RightLabel,
    string DiffPngPath,
    ShapeDiffMetrics Metrics,
    PairMismatchFinding Classification);

internal sealed record PairMismatchFinding(
    string Kind,
    double Confidence,
    string Notes);

internal sealed record ThreeWayShapeDiffFixtureReport(
    string Id,
    string Text,
    bool BrowserCaptured,
    string BrowserPngPath,
    string DirectOutlinePngPath,
    string MsdfPngPath,
    string BrowserVsDirectDiffPngPath,
    string DirectVsMsdfDiffPngPath,
    string BrowserVsMsdfDiffPngPath,
    string ThreeWayOverlayPngPath,
    string WireframePngPath,
    ThreeWayPairReport? BrowserVsDirect,
    ThreeWayPairReport DirectVsMsdf,
    ThreeWayPairReport? BrowserVsMsdf,
    PairMismatchFinding Finding);

internal sealed record ThreeWayShapeDiffSizeReport(
    int SizePx,
    string OutputDirectory,
    int CanvasWidth,
    int CanvasHeight,
    double OriginX,
    double BaselineY,
    IReadOnlyList<ThreeWayShapeDiffFixtureReport> Fixtures);

internal sealed record ThreeWayShapeDiffReport(
    string FontPath,
    string FontFace,
    IReadOnlyList<int> FontSizes,
    IReadOnlyList<string> Texts,
    string DirectOutlineRasterizationPolicy,
    string MaskPolicy,
    string MetricsPolicy,
    IReadOnlyList<ThreeWayShapeDiffSizeReport> Sizes,
    PairMismatchFinding OverallFinding);

internal sealed record ThreeWayShapeDiffExportResult(
    string OutputDirectory,
    string ReportJsonPath,
    string ReportTextPath,
    ThreeWayShapeDiffReport Report);

internal sealed record ShapeDiffBrowserCaptureDocument(
    string? GeneratedAtUtc,
    string? BrowserPath,
    string? FixtureHtmlPath,
    IReadOnlyList<ShapeDiffBrowserCaptureFixture> Fixtures);

internal sealed record ShapeDiffBrowserCaptureFixture(
    string Id,
    int SizePx,
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
    ShapeDiffBrowserCapture? Capture,
    string? UnavailableReason = null);

internal sealed record ShapeDiffBrowserCapture(
    int Width,
    int Height,
    string PixelFormat,
    string RgbaBase64);
