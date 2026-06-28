using System.Text;
using System.Text.Json;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tests.Rendering;

internal static class FontReferenceDiffWorkflow
{
    public const string DiffReportJsonFileName = "diff-report.json";
    public const string DiffReportTextFileName = "diff-report.txt";

    private static readonly Rgba32 OverlayBackground = new(10, 10, 14, 255);
    private static readonly Rgba32 BrowserOnlyColor = new(0, 220, 255, 255);
    private static readonly Rgba32 MachinaOnlyColor = new(255, 148, 32, 255);
    private static readonly Rgba32 OverlapColor = new(255, 255, 255, 255);
    private static readonly Rgba32 BaselineColor = new(255, 0, 0, 255);
    private static readonly Rgba32 BrowserBoundsColor = new(0, 220, 255, 255);
    private static readonly Rgba32 MachinaBoundsColor = new(255, 148, 32, 255);
    private static readonly Rgba32 BrowserActualMetricsColor = new(96, 255, 96, 255);
    private static readonly Rgba32 BrowserFontMetricsColor = new(184, 96, 255, 255);
    private static readonly Rgba32 MachinaGlyphBoundsColor = new(255, 204, 96, 255);

    private const int InkDistanceThreshold = 12;
    private const int BaselineDistanceThreshold = 24;
    private const int ThresholdDifferenceTolerance = 18;

    internal static RgbaImage CreateOverlayImage(RgbaImage browserImage, RgbaImage machinaImage)
    {
        return ImageDiffDiagnostics.CreateOverlayImage(
            browserImage,
            machinaImage,
            FontReferenceOracleWorkflow.BackgroundColor,
            FontReferenceOracleWorkflow.BaselineColor,
            OverlayBackground,
            BrowserOnlyColor,
            MachinaOnlyColor,
            OverlapColor);
    }

    internal static RgbaImage CreateAbsoluteDiffImage(RgbaImage browserImage, RgbaImage machinaImage)
    {
        return ImageDiffDiagnostics.CreateAbsoluteDiffImage(
            browserImage,
            machinaImage,
            FontReferenceOracleWorkflow.BaselineColor);
    }

    internal static RgbaImage CreateThresholdDiffImage(RgbaImage browserImage, RgbaImage machinaImage)
    {
        return ImageDiffDiagnostics.CreateThresholdDiffImage(
            browserImage,
            machinaImage,
            FontReferenceOracleWorkflow.BaselineColor,
            ThresholdDifferenceTolerance);
    }

    internal static PixelBounds? ComputeInkBounds(RgbaImage image)
    {
        return ImageDiffDiagnostics.ComputeInkBounds(
            image,
            FontReferenceOracleWorkflow.BackgroundColor,
            FontReferenceOracleWorkflow.BaselineColor);
    }

    internal static ImageDiffMetrics ComputeMetrics(RgbaImage browserImage, RgbaImage machinaImage)
    {
        return ImageDiffDiagnostics.ComputeMetrics(
            browserImage,
            machinaImage,
            FontReferenceOracleWorkflow.BackgroundColor,
            FontReferenceOracleWorkflow.BaselineColor,
            ThresholdDifferenceTolerance);
    }

    internal static RgbaImage CreateWireframeImage(
        RgbaImage browserImage,
        RgbaImage machinaImage,
        FontReferenceOracleFixtureReport placementFixture,
        ImageDiffMetrics metrics)
    {
        return ImageDiffDiagnostics.CreateWireframeImage(
            browserImage,
            machinaImage,
            placementFixture,
            metrics,
            FontReferenceOracleWorkflow.BackgroundColor,
            FontReferenceOracleWorkflow.BaselineColor,
            OverlayBackground,
            BrowserBoundsColor,
            MachinaBoundsColor,
            BrowserActualMetricsColor,
            BrowserFontMetricsColor,
            MachinaGlyphBoundsColor);
    }

    public static async Task<FontReferenceDiffExportResult> ExportAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        FontReferenceOracleExportResult oracle = await FontReferenceOracleWorkflow.ExportAsync(outputDirectory, cancellationToken);

        Dictionary<string, BrowserTextMetricsFixture> browserFixtures = oracle.BrowserMetrics?.Fixtures
            .ToDictionary(static fixture => fixture.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, BrowserTextMetricsFixture>(StringComparer.Ordinal);

        List<FontReferenceDiffFixtureReport> fixtures = [];

        foreach (FontReferenceOracleArtifact artifact in oracle.Artifacts.OrderBy(static item => item.Definition.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FontReferenceOracleDefinition definition = artifact.Definition;
            BrowserTextMetricsFixture? browserFixture = browserFixtures.TryGetValue(definition.Id, out BrowserTextMetricsFixture? value)
                ? value
                : null;

            RgbaImage? browserImage = TryCreateBrowserImage(browserFixture);
            string browserPath = Path.Combine(oracle.OutputDirectory, definition.BrowserPngFileName);
            string overlayPath = Path.Combine(oracle.OutputDirectory, definition.OverlayPngFileName);
            string diffPath = Path.Combine(oracle.OutputDirectory, definition.DiffPngFileName);
            string thresholdDiffPath = Path.Combine(oracle.OutputDirectory, definition.ThresholdDiffPngFileName);
            string wireframePath = Path.Combine(oracle.OutputDirectory, definition.WireframePngFileName);
            string comparePath = Path.Combine(oracle.OutputDirectory, definition.ComparePngFileName);

            if (browserImage is not null)
            {
                RgbaPngWriter.Write(browserPath, browserImage);
            }

            FontReferenceOracleFixtureReport placementFixture = oracle.PlacementReport.Fixtures.Single(
                item => string.Equals(item.Id, definition.Id, StringComparison.Ordinal));

            ImageDiffMetrics metrics = ImageDiffDiagnostics.ComputeMetrics(
                browserImage,
                artifact.Image,
                FontReferenceOracleWorkflow.BackgroundColor,
                FontReferenceOracleWorkflow.BaselineColor,
                ThresholdDifferenceTolerance);

            if (browserImage is not null)
            {
                RgbaPngWriter.Write(overlayPath, ImageDiffDiagnostics.CreateOverlayImage(
                    browserImage,
                    artifact.Image,
                    FontReferenceOracleWorkflow.BackgroundColor,
                    FontReferenceOracleWorkflow.BaselineColor,
                    OverlayBackground,
                    BrowserOnlyColor,
                    MachinaOnlyColor,
                    OverlapColor));

                RgbaPngWriter.Write(diffPath, ImageDiffDiagnostics.CreateAbsoluteDiffImage(
                    browserImage,
                    artifact.Image,
                    FontReferenceOracleWorkflow.BaselineColor));

                RgbaPngWriter.Write(thresholdDiffPath, ImageDiffDiagnostics.CreateThresholdDiffImage(
                    browserImage,
                    artifact.Image,
                    FontReferenceOracleWorkflow.BaselineColor,
                    ThresholdDifferenceTolerance));

                RgbaPngWriter.Write(wireframePath, ImageDiffDiagnostics.CreateWireframeImage(
                    browserImage,
                    artifact.Image,
                    placementFixture,
                    metrics,
                    FontReferenceOracleWorkflow.BackgroundColor,
                    FontReferenceOracleWorkflow.BaselineColor,
                    OverlayBackground,
                    BrowserBoundsColor,
                    MachinaBoundsColor,
                    BrowserActualMetricsColor,
                    BrowserFontMetricsColor,
                    MachinaGlyphBoundsColor));

                RgbaPngWriter.Write(comparePath, ImageDiffDiagnostics.CreateSideBySideImage(
                    browserImage,
                    artifact.Image,
                    BrowserBoundsColor,
                    MachinaBoundsColor));
            }

            fixtures.Add(new FontReferenceDiffFixtureReport(
                definition.Id,
                definition.Text,
                browserImage is not null,
                browserPath,
                artifact.MachinaPngPath,
                overlayPath,
                diffPath,
                thresholdDiffPath,
                wireframePath,
                comparePath,
                browserFixture?.UnavailableReason,
                metrics));
        }

        FontReferenceDiffReport report = new(
            OutputDirectory: oracle.OutputDirectory,
            FontPath: oracle.FontPath,
            FontFace: oracle.PlacementReport.FontFace,
            EmSize: oracle.EmSize,
            CanvasWidth: oracle.OutputWidth,
            CanvasHeight: oracle.OutputHeight,
            OriginX: oracle.OriginX,
            BaselineY: oracle.BaselineY,
            InkMaskPolicy: new InkMaskPolicyDescription(
                BackgroundColor: ToHexColor(FontReferenceOracleWorkflow.BackgroundColor),
                BaselineGuideColor: ToHexColor(FontReferenceOracleWorkflow.BaselineColor),
                InkDistanceThreshold: InkDistanceThreshold,
                BaselineDistanceThreshold: BaselineDistanceThreshold,
                ThresholdDifferenceTolerance: ThresholdDifferenceTolerance,
                Description: "Ink pixels are any non-background pixels beyond a small RGB distance threshold, excluding pixels close to the 1 px red baseline guide."),
            Fixtures: fixtures);

        string jsonPath = Path.Combine(oracle.OutputDirectory, DiffReportJsonFileName);
        string textPath = Path.Combine(oracle.OutputDirectory, DiffReportTextFileName);

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(textPath, BuildTextReport(report));

        return new FontReferenceDiffExportResult(
            oracle.OutputDirectory,
            jsonPath,
            textPath,
            fixtures);
    }

    private static RgbaImage? TryCreateBrowserImage(BrowserTextMetricsFixture? fixture)
    {
        if (fixture?.Capture is null)
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
        int byteIndex = 0;

        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            pixels[pixelIndex] = new Rgba32(
                bytes[byteIndex++],
                bytes[byteIndex++],
                bytes[byteIndex++],
                bytes[byteIndex++]);
        }

        return new RgbaImage(fixture.Capture.Width, fixture.Capture.Height, pixels);
    }

    private static string BuildTextReport(FontReferenceDiffReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("Machina MSDF browser-vs-Machina diff report");
        builder.AppendLine($"outputDirectory: {report.OutputDirectory}");
        builder.AppendLine($"fontPath: {report.FontPath}");
        builder.AppendLine($"fontFace: {report.FontFace}");
        builder.AppendLine($"emSize: {report.EmSize:0.###}");
        builder.AppendLine($"canvas: {report.CanvasWidth}x{report.CanvasHeight}");
        builder.AppendLine($"originX: {report.OriginX:0.###}");
        builder.AppendLine($"baselineY: {report.BaselineY:0.###}");
        builder.AppendLine("inkMaskPolicy:");
        builder.AppendLine($"  backgroundColor: {report.InkMaskPolicy.BackgroundColor}");
        builder.AppendLine($"  baselineGuideColor: {report.InkMaskPolicy.BaselineGuideColor}");
        builder.AppendLine($"  inkDistanceThreshold: {report.InkMaskPolicy.InkDistanceThreshold}");
        builder.AppendLine($"  baselineDistanceThreshold: {report.InkMaskPolicy.BaselineDistanceThreshold}");
        builder.AppendLine($"  thresholdDifferenceTolerance: {report.InkMaskPolicy.ThresholdDifferenceTolerance}");
        builder.AppendLine($"  description: {report.InkMaskPolicy.Description}");
        builder.AppendLine();

        foreach (FontReferenceDiffFixtureReport fixture in report.Fixtures)
        {
            builder.AppendLine($"[{fixture.Id}] {fixture.Text}");
            builder.AppendLine($"browserCaptured: {fixture.BrowserCaptured.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(fixture.UnavailableReason))
            {
                builder.AppendLine($"unavailableReason: {fixture.UnavailableReason}");
            }

            builder.AppendLine($"browserPngPath: {fixture.BrowserPngPath}");
            builder.AppendLine($"machinaPngPath: {fixture.MachinaPngPath}");
            builder.AppendLine($"overlayPngPath: {fixture.OverlayPngPath}");
            builder.AppendLine($"diffPngPath: {fixture.DiffPngPath}");
            builder.AppendLine($"thresholdDiffPngPath: {fixture.ThresholdDiffPngPath}");
            builder.AppendLine($"wireframePngPath: {fixture.WireframePngPath}");
            builder.AppendLine($"comparePngPath: {fixture.ComparePngPath}");
            AppendBounds(builder, "browserInkBounds", fixture.Metrics.BrowserInkBounds);
            AppendBounds(builder, "machinaInkBounds", fixture.Metrics.MachinaInkBounds);
            builder.AppendLine($"deltaLeft: {fixture.Metrics.DeltaLeft}");
            builder.AppendLine($"deltaTop: {fixture.Metrics.DeltaTop}");
            builder.AppendLine($"deltaRight: {fixture.Metrics.DeltaRight}");
            builder.AppendLine($"deltaBottom: {fixture.Metrics.DeltaBottom}");
            builder.AppendLine($"deltaWidth: {fixture.Metrics.DeltaWidth}");
            builder.AppendLine($"deltaHeight: {fixture.Metrics.DeltaHeight}");
            builder.AppendLine($"browserInkArea: {fixture.Metrics.BrowserInkArea}");
            builder.AppendLine($"machinaInkArea: {fixture.Metrics.MachinaInkArea}");
            builder.AppendLine($"overlapArea: {fixture.Metrics.OverlapArea}");
            builder.AppendLine($"browserOnlyArea: {fixture.Metrics.BrowserOnlyArea}");
            builder.AppendLine($"machinaOnlyArea: {fixture.Metrics.MachinaOnlyArea}");
            builder.AppendLine($"intersectionOverUnion: {fixture.Metrics.IntersectionOverUnion:0.0000}");
            builder.AppendLine($"meanAbsoluteDifference: {fixture.Metrics.MeanAbsoluteDifference:0.0000}");
            builder.AppendLine($"maxDifference: {fixture.Metrics.MaxDifference:0.0000}");
            builder.AppendLine($"mismatchPixelCount: {fixture.Metrics.MismatchPixelCount}");
            builder.AppendLine($"mismatchRatio: {fixture.Metrics.MismatchRatio:0.0000}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendBounds(StringBuilder builder, string label, PixelBounds? bounds)
    {
        if (bounds is null)
        {
            builder.AppendLine($"{label}: <none>");
            return;
        }

        builder.AppendLine(
            $"{label}: left={bounds.Left}, top={bounds.Top}, right={bounds.Right}, bottom={bounds.Bottom}, width={bounds.Width}, height={bounds.Height}");
    }

    private static string ToHexColor(Rgba32 color)
    {
        return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static class ImageDiffDiagnostics
    {
        public static ImageDiffMetrics ComputeMetrics(
            RgbaImage? browserImage,
            RgbaImage machinaImage,
            Rgba32 background,
            Rgba32 baselineGuideColor,
            int thresholdDifferenceTolerance)
        {
            if (browserImage is null)
            {
                return ImageDiffMetrics.Empty;
            }

            ValidateSameSize(browserImage, machinaImage);

            bool[] browserMask = BuildInkMask(browserImage, background, baselineGuideColor);
            bool[] machinaMask = BuildInkMask(machinaImage, background, baselineGuideColor);

            PixelBounds? browserBounds = ComputeBounds(browserMask, browserImage.Width, browserImage.Height);
            PixelBounds? machinaBounds = ComputeBounds(machinaMask, browserImage.Width, browserImage.Height);

            int overlapArea = 0;
            int browserOnlyArea = 0;
            int machinaOnlyArea = 0;
            int browserInkArea = 0;
            int machinaInkArea = 0;
            int mismatchPixelCount = 0;
            double totalDifference = 0d;
            double maxDifference = 0d;
            int pixelCount = browserImage.Width * browserImage.Height;

            for (int index = 0; index < pixelCount; index++)
            {
                bool browserInk = browserMask[index];
                bool machinaInk = machinaMask[index];

                if (browserInk)
                {
                    browserInkArea++;
                }

                if (machinaInk)
                {
                    machinaInkArea++;
                }

                if (browserInk && machinaInk)
                {
                    overlapArea++;
                }
                else if (browserInk)
                {
                    browserOnlyArea++;
                }
                else if (machinaInk)
                {
                    machinaOnlyArea++;
                }

                Rgba32 browserPixel = browserImage.Pixels[index];
                Rgba32 machinaPixel = machinaImage.Pixels[index];
                double difference = ComputePixelDifference(browserPixel, machinaPixel);
                totalDifference += difference;
                maxDifference = Math.Max(maxDifference, difference);

                if (difference > thresholdDifferenceTolerance || browserInk != machinaInk)
                {
                    mismatchPixelCount++;
                }
            }

            int unionArea = overlapArea + browserOnlyArea + machinaOnlyArea;
            double iou = unionArea == 0 ? 1d : overlapArea / (double)unionArea;
            double meanAbsoluteDifference = pixelCount == 0 ? 0d : totalDifference / pixelCount;
            double mismatchRatio = pixelCount == 0 ? 0d : mismatchPixelCount / (double)pixelCount;

            return new ImageDiffMetrics(
                BrowserInkBounds: browserBounds,
                MachinaInkBounds: machinaBounds,
                DeltaLeft: GetDelta(browserBounds?.Left, machinaBounds?.Left),
                DeltaTop: GetDelta(browserBounds?.Top, machinaBounds?.Top),
                DeltaRight: GetDelta(browserBounds?.Right, machinaBounds?.Right),
                DeltaBottom: GetDelta(browserBounds?.Bottom, machinaBounds?.Bottom),
                DeltaWidth: GetDelta(browserBounds?.Width, machinaBounds?.Width),
                DeltaHeight: GetDelta(browserBounds?.Height, machinaBounds?.Height),
                BrowserInkArea: browserInkArea,
                MachinaInkArea: machinaInkArea,
                OverlapArea: overlapArea,
                BrowserOnlyArea: browserOnlyArea,
                MachinaOnlyArea: machinaOnlyArea,
                IntersectionOverUnion: iou,
                MeanAbsoluteDifference: meanAbsoluteDifference,
                MaxDifference: maxDifference,
                MismatchPixelCount: mismatchPixelCount,
                MismatchRatio: mismatchRatio);
        }

        public static RgbaImage CreateOverlayImage(
            RgbaImage browserImage,
            RgbaImage machinaImage,
            Rgba32 background,
            Rgba32 baselineGuideColor,
            Rgba32 overlayBackground,
            Rgba32 browserOnlyColor,
            Rgba32 machinaOnlyColor,
            Rgba32 overlapColor)
        {
            ValidateSameSize(browserImage, machinaImage);
            RgbaImage output = CreateFilled(browserImage.Width, browserImage.Height, overlayBackground);
            bool[] browserMask = BuildInkMask(browserImage, background, baselineGuideColor);
            bool[] machinaMask = BuildInkMask(machinaImage, background, baselineGuideColor);

            for (int index = 0; index < output.Pixels.Length; index++)
            {
                Rgba32 browserPixel = browserImage.Pixels[index];
                Rgba32 machinaPixel = machinaImage.Pixels[index];

                if (IsBaselinePixel(browserPixel, baselineGuideColor) || IsBaselinePixel(machinaPixel, baselineGuideColor))
                {
                    output.Pixels[index] = baselineGuideColor;
                    continue;
                }

                bool browserInk = browserMask[index];
                bool machinaInk = machinaMask[index];

                output.Pixels[index] = browserInk && machinaInk
                    ? overlapColor
                    : browserInk
                        ? browserOnlyColor
                        : machinaInk
                            ? machinaOnlyColor
                            : overlayBackground;
            }

            return output;
        }

        public static RgbaImage CreateAbsoluteDiffImage(
            RgbaImage browserImage,
            RgbaImage machinaImage,
            Rgba32 baselineGuideColor)
        {
            ValidateSameSize(browserImage, machinaImage);
            RgbaImage output = CreateFilled(browserImage.Width, browserImage.Height, OverlayBackground);

            for (int index = 0; index < output.Pixels.Length; index++)
            {
                Rgba32 browserPixel = browserImage.Pixels[index];
                Rgba32 machinaPixel = machinaImage.Pixels[index];

                if (IsBaselinePixel(browserPixel, baselineGuideColor) || IsBaselinePixel(machinaPixel, baselineGuideColor))
                {
                    output.Pixels[index] = baselineGuideColor;
                    continue;
                }

                byte intensity = (byte)Math.Round(
                    Math.Clamp(ComputePixelDifference(browserPixel, machinaPixel), 0d, 255d),
                    MidpointRounding.AwayFromZero);
                output.Pixels[index] = new Rgba32(intensity, intensity, intensity, 255);
            }

            return output;
        }

        public static RgbaImage CreateThresholdDiffImage(
            RgbaImage browserImage,
            RgbaImage machinaImage,
            Rgba32 baselineGuideColor,
            int tolerance)
        {
            ValidateSameSize(browserImage, machinaImage);
            RgbaImage output = CreateFilled(browserImage.Width, browserImage.Height, OverlayBackground);

            for (int index = 0; index < output.Pixels.Length; index++)
            {
                Rgba32 browserPixel = browserImage.Pixels[index];
                Rgba32 machinaPixel = machinaImage.Pixels[index];

                if (IsBaselinePixel(browserPixel, baselineGuideColor) || IsBaselinePixel(machinaPixel, baselineGuideColor))
                {
                    output.Pixels[index] = baselineGuideColor;
                    continue;
                }

                double difference = ComputePixelDifference(browserPixel, machinaPixel);
                output.Pixels[index] = difference > tolerance
                    ? new Rgba32(255, 255, 128, 255)
                    : OverlayBackground;
            }

            return output;
        }

        public static RgbaImage CreateWireframeImage(
            RgbaImage browserImage,
            RgbaImage machinaImage,
            FontReferenceOracleFixtureReport placementFixture,
            ImageDiffMetrics metrics,
            Rgba32 background,
            Rgba32 baselineGuideColor,
            Rgba32 overlayBackground,
            Rgba32 browserBoundsColor,
            Rgba32 machinaBoundsColor,
            Rgba32 browserActualMetricsColor,
            Rgba32 browserFontMetricsColor,
            Rgba32 machinaGlyphBoundsColor)
        {
            RgbaImage output = CreateOverlayImage(
                browserImage,
                machinaImage,
                background,
                baselineGuideColor,
                overlayBackground,
                new Rgba32(0, 108, 144, 255),
                new Rgba32(144, 92, 20, 255),
                new Rgba32(180, 180, 180, 255));

            DrawHorizontalLine(
                output,
                (int)Math.Round(placementFixture.BaselineY, MidpointRounding.AwayFromZero),
                baselineGuideColor);

            DrawBounds(output, metrics.BrowserInkBounds, browserBoundsColor);
            DrawBounds(output, metrics.MachinaInkBounds, machinaBoundsColor);

            DrawBounds(output, CreateBrowserActualBounds(placementFixture.BrowserVerticalMetrics, placementFixture.Browser), browserActualMetricsColor);
            DrawBounds(output, CreateBrowserFontBounds(placementFixture.BrowserVerticalMetrics, placementFixture.Browser), browserFontMetricsColor);

            foreach (FontReferenceOracleGlyphRow glyph in placementFixture.Glyphs.Where(static glyph => !glyph.IsWhitespace))
            {
                if (glyph.DrawX is null || glyph.DrawY is null || glyph.DrawWidth is null || glyph.DrawHeight is null)
                {
                    continue;
                }

                DrawRectangle(
                    output,
                    new PixelBounds(
                        glyph.DrawX.Value,
                        glyph.DrawY.Value,
                        glyph.DrawX.Value + glyph.DrawWidth.Value - 1,
                        glyph.DrawY.Value + glyph.DrawHeight.Value - 1),
                    machinaGlyphBoundsColor);
            }

            return output;
        }

        public static RgbaImage CreateSideBySideImage(
            RgbaImage browserImage,
            RgbaImage machinaImage,
            Rgba32 browserFrameColor,
            Rgba32 machinaFrameColor)
        {
            ValidateSameSize(browserImage, machinaImage);

            const int gutter = 12;
            int width = (browserImage.Width * 2) + (gutter * 3);
            int height = browserImage.Height + (gutter * 2);
            RgbaImage output = CreateFilled(width, height, OverlayBackground);

            Blit(browserImage, output, gutter, gutter);
            Blit(machinaImage, output, (gutter * 2) + browserImage.Width, gutter);

            DrawRectangle(
                output,
                new PixelBounds(gutter, gutter, gutter + browserImage.Width - 1, gutter + browserImage.Height - 1),
                browserFrameColor);

            DrawRectangle(
                output,
                new PixelBounds(
                    (gutter * 2) + browserImage.Width,
                    gutter,
                    (gutter * 2) + (browserImage.Width * 2) - 1,
                    gutter + browserImage.Height - 1),
                machinaFrameColor);

            return output;
        }

        public static PixelBounds? ComputeInkBounds(
            RgbaImage image,
            Rgba32 background,
            Rgba32 baselineGuideColor)
        {
            bool[] mask = BuildInkMask(image, background, baselineGuideColor);
            return ComputeBounds(mask, image.Width, image.Height);
        }

        public static bool[] BuildInkMask(
            RgbaImage image,
            Rgba32 background,
            Rgba32 baselineGuideColor)
        {
            bool[] mask = new bool[image.Pixels.Length];

            for (int index = 0; index < image.Pixels.Length; index++)
            {
                Rgba32 pixel = image.Pixels[index];
                mask[index] = IsInkPixel(pixel, background, baselineGuideColor);
            }

            return mask;
        }

        private static PixelBounds? CreateBrowserActualBounds(
            BrowserVerticalMetrics? metrics,
            BrowserTextMetricsFixture? fixture)
        {
            if (fixture is null || metrics?.ActualTop is null || metrics.ActualBottom is null || fixture.Metrics.ActualBoundingBoxLeft is null || fixture.Metrics.ActualBoundingBoxRight is null)
            {
                return null;
            }

            int left = (int)Math.Round(fixture.X - fixture.Metrics.ActualBoundingBoxLeft.Value, MidpointRounding.AwayFromZero);
            int right = (int)Math.Round(fixture.X + fixture.Metrics.ActualBoundingBoxRight.Value, MidpointRounding.AwayFromZero) - 1;
            int top = (int)Math.Round(metrics.ActualTop.Value, MidpointRounding.AwayFromZero);
            int bottom = (int)Math.Round(metrics.ActualBottom.Value, MidpointRounding.AwayFromZero) - 1;
            return CreateBoundsIfValid(left, top, right, bottom);
        }

        private static PixelBounds? CreateBrowserFontBounds(
            BrowserVerticalMetrics? metrics,
            BrowserTextMetricsFixture? fixture)
        {
            if (fixture is null || metrics?.FontTop is null || metrics.FontBottom is null || fixture.Metrics.ActualBoundingBoxLeft is null || fixture.Metrics.ActualBoundingBoxRight is null)
            {
                return null;
            }

            int left = (int)Math.Round(fixture.X - fixture.Metrics.ActualBoundingBoxLeft.Value, MidpointRounding.AwayFromZero);
            int right = (int)Math.Round(fixture.X + fixture.Metrics.ActualBoundingBoxRight.Value, MidpointRounding.AwayFromZero) - 1;
            int top = (int)Math.Round(metrics.FontTop.Value, MidpointRounding.AwayFromZero);
            int bottom = (int)Math.Round(metrics.FontBottom.Value, MidpointRounding.AwayFromZero) - 1;
            return CreateBoundsIfValid(left, top, right, bottom);
        }

        private static PixelBounds? CreateBoundsIfValid(int left, int top, int right, int bottom)
        {
            return right >= left && bottom >= top
                ? new PixelBounds(left, top, right, bottom)
                : null;
        }

        private static PixelBounds? ComputeBounds(bool[] mask, int width, int height)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (!mask[rowOffset + x])
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            return maxX < 0 || maxY < 0
                ? null
                : new PixelBounds(minX, minY, maxX, maxY);
        }

        private static bool IsInkPixel(Rgba32 pixel, Rgba32 background, Rgba32 baselineGuideColor)
        {
            if (IsBaselinePixel(pixel, baselineGuideColor))
            {
                return false;
            }

            return ComputeColorDistance(pixel, background) > InkDistanceThreshold;
        }

        private static bool IsBaselinePixel(Rgba32 pixel, Rgba32 baselineGuideColor)
        {
            return ComputeColorDistance(pixel, baselineGuideColor) <= BaselineDistanceThreshold;
        }

        private static int ComputeColorDistance(Rgba32 left, Rgba32 right)
        {
            int deltaR = Math.Abs(left.R - right.R);
            int deltaG = Math.Abs(left.G - right.G);
            int deltaB = Math.Abs(left.B - right.B);
            return Math.Max(deltaR, Math.Max(deltaG, deltaB));
        }

        private static double ComputePixelDifference(Rgba32 left, Rgba32 right)
        {
            return (
                Math.Abs(left.R - right.R) +
                Math.Abs(left.G - right.G) +
                Math.Abs(left.B - right.B)) / 3d;
        }

        private static int? GetDelta(int? browserValue, int? machinaValue)
        {
            return browserValue.HasValue && machinaValue.HasValue
                ? machinaValue.Value - browserValue.Value
                : null;
        }

        private static void ValidateSameSize(RgbaImage left, RgbaImage right)
        {
            if (left.Width != right.Width || left.Height != right.Height)
            {
                throw new InvalidOperationException(
                    $"Image sizes must match. Left={left.Width}x{left.Height}, right={right.Width}x{right.Height}.");
            }
        }

        private static RgbaImage CreateFilled(int width, int height, Rgba32 color)
        {
            RgbaImage image = new(width, height);
            CpuDistanceFieldGlyphRenderer.Fill(image, color);
            return image;
        }

        private static void Blit(RgbaImage source, RgbaImage destination, int offsetX, int offsetY)
        {
            for (int y = 0; y < source.Height; y++)
            {
                int targetY = offsetY + y;
                if ((uint)targetY >= (uint)destination.Height)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int targetX = offsetX + x;
                    if ((uint)targetX >= (uint)destination.Width)
                    {
                        continue;
                    }

                    destination.SetPixel(targetX, targetY, source.GetPixel(x, y));
                }
            }
        }

        private static void DrawBounds(RgbaImage image, PixelBounds? bounds, Rgba32 color)
        {
            if (bounds is null)
            {
                return;
            }

            DrawRectangle(image, bounds, color);
        }

        private static void DrawRectangle(RgbaImage image, PixelBounds bounds, Rgba32 color)
        {
            DrawHorizontalSegment(image, bounds.Left, bounds.Right, bounds.Top, color);
            DrawHorizontalSegment(image, bounds.Left, bounds.Right, bounds.Bottom, color);
            DrawVerticalSegment(image, bounds.Left, bounds.Top, bounds.Bottom, color);
            DrawVerticalSegment(image, bounds.Right, bounds.Top, bounds.Bottom, color);
        }

        private static void DrawHorizontalLine(RgbaImage image, int y, Rgba32 color)
        {
            DrawHorizontalSegment(image, 0, image.Width - 1, y, color);
        }

        private static void DrawHorizontalSegment(RgbaImage image, int left, int right, int y, Rgba32 color)
        {
            if ((uint)y >= (uint)image.Height)
            {
                return;
            }

            int clampedLeft = Math.Max(0, left);
            int clampedRight = Math.Min(image.Width - 1, right);
            for (int x = clampedLeft; x <= clampedRight; x++)
            {
                image.SetPixel(x, y, color);
            }
        }

        private static void DrawVerticalSegment(RgbaImage image, int x, int top, int bottom, Rgba32 color)
        {
            if ((uint)x >= (uint)image.Width)
            {
                return;
            }

            int clampedTop = Math.Max(0, top);
            int clampedBottom = Math.Min(image.Height - 1, bottom);
            for (int y = clampedTop; y <= clampedBottom; y++)
            {
                image.SetPixel(x, y, color);
            }
        }
    }
}

internal sealed record FontReferenceDiffExportResult(
    string OutputDirectory,
    string DiffReportJsonPath,
    string DiffReportTextPath,
    IReadOnlyList<FontReferenceDiffFixtureReport> Fixtures);

internal sealed record FontReferenceDiffReport(
    string OutputDirectory,
    string FontPath,
    string FontFace,
    double EmSize,
    int CanvasWidth,
    int CanvasHeight,
    double OriginX,
    double BaselineY,
    InkMaskPolicyDescription InkMaskPolicy,
    IReadOnlyList<FontReferenceDiffFixtureReport> Fixtures);

internal sealed record FontReferenceDiffFixtureReport(
    string Id,
    string Text,
    bool BrowserCaptured,
    string BrowserPngPath,
    string MachinaPngPath,
    string OverlayPngPath,
    string DiffPngPath,
    string ThresholdDiffPngPath,
    string WireframePngPath,
    string ComparePngPath,
    string? UnavailableReason,
    ImageDiffMetrics Metrics);

internal sealed record InkMaskPolicyDescription(
    string BackgroundColor,
    string BaselineGuideColor,
    int InkDistanceThreshold,
    int BaselineDistanceThreshold,
    int ThresholdDifferenceTolerance,
    string Description);

internal sealed record ImageDiffMetrics(
    PixelBounds? BrowserInkBounds,
    PixelBounds? MachinaInkBounds,
    int? DeltaLeft,
    int? DeltaTop,
    int? DeltaRight,
    int? DeltaBottom,
    int? DeltaWidth,
    int? DeltaHeight,
    int BrowserInkArea,
    int MachinaInkArea,
    int OverlapArea,
    int BrowserOnlyArea,
    int MachinaOnlyArea,
    double IntersectionOverUnion,
    double MeanAbsoluteDifference,
    double MaxDifference,
    int MismatchPixelCount,
    double MismatchRatio)
{
    public static ImageDiffMetrics Empty { get; } = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        0,
        0,
        0d,
        0d,
        0d,
        0,
        0d);
}

internal sealed record PixelBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => (Right - Left) + 1;

    public int Height => (Bottom - Top) + 1;
}
