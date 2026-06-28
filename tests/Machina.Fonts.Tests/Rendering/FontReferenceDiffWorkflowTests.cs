using System.Linq;
using System.Text.Json;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

[Collection("EnvironmentVariable")]
public sealed class FontReferenceDiffWorkflowTests
{
    [Fact]
    public void FontReferenceDiff_GeneratesOverlayImage()
    {
        RgbaImage browser = CreateFilledImage(4, 4, FontReferenceOracleWorkflow.BackgroundColor);
        RgbaImage machina = CreateFilledImage(4, 4, FontReferenceOracleWorkflow.BackgroundColor);

        browser.SetPixel(0, 0, FontReferenceOracleWorkflow.ForegroundColor);
        browser.SetPixel(1, 1, FontReferenceOracleWorkflow.ForegroundColor);
        machina.SetPixel(1, 1, FontReferenceOracleWorkflow.ForegroundColor);
        machina.SetPixel(2, 2, FontReferenceOracleWorkflow.ForegroundColor);
        browser.SetPixel(0, 3, FontReferenceOracleWorkflow.BaselineColor);

        RgbaImage overlay = FontReferenceDiffWorkflow.CreateOverlayImage(browser, machina);

        Assert.Equal(new Rgba32(0, 220, 255, 255), overlay.GetPixel(0, 0));
        Assert.Equal(new Rgba32(255, 255, 255, 255), overlay.GetPixel(1, 1));
        Assert.Equal(new Rgba32(255, 148, 32, 255), overlay.GetPixel(2, 2));
        Assert.Equal(FontReferenceOracleWorkflow.BaselineColor, overlay.GetPixel(0, 3));
    }

    [Fact]
    public void FontReferenceDiff_GeneratesAbsoluteDiffImage()
    {
        RgbaImage browser = CreateFilledImage(3, 3, FontReferenceOracleWorkflow.BackgroundColor);
        RgbaImage machina = CreateFilledImage(3, 3, FontReferenceOracleWorkflow.BackgroundColor);

        machina.SetPixel(1, 1, FontReferenceOracleWorkflow.ForegroundColor);

        RgbaImage diff = FontReferenceDiffWorkflow.CreateAbsoluteDiffImage(browser, machina);

        Assert.True(diff.GetPixel(1, 1).R > 0);
        Assert.Equal(new Rgba32(0, 0, 0, 255), diff.GetPixel(0, 0));
    }

    [Fact]
    public void FontReferenceDiff_GeneratesWireframeImage()
    {
        RgbaImage browser = CreateFilledImage(16, 12, FontReferenceOracleWorkflow.BackgroundColor);
        RgbaImage machina = CreateFilledImage(16, 12, FontReferenceOracleWorkflow.BackgroundColor);

        FillRect(browser, 2, 2, 6, 5, FontReferenceOracleWorkflow.ForegroundColor);
        FillRect(machina, 3, 3, 7, 6, FontReferenceOracleWorkflow.ForegroundColor);

        ImageDiffMetrics metrics = FontReferenceDiffWorkflow.ComputeMetrics(browser, machina);
        FontReferenceOracleFixtureReport placementFixture = CreatePlacementFixture();

        RgbaImage wireframe = FontReferenceDiffWorkflow.CreateWireframeImage(browser, machina, placementFixture, metrics);

        Assert.Contains(wireframe.Pixels, static pixel => pixel == new Rgba32(184, 96, 255, 255));
        Assert.Contains(wireframe.Pixels, static pixel => pixel == new Rgba32(255, 204, 96, 255));
        Assert.Equal(FontReferenceOracleWorkflow.BaselineColor, wireframe.GetPixel(0, 8));
    }

    [Fact]
    public async Task FontReferenceDiff_WritesDiffReport()
    {
        string directory = CreateDirectory();
        string capturePath = WriteBrowserCaptureJson(directory);
        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, capturePath);

            FontReferenceDiffExportResult result = await FontReferenceDiffWorkflow.ExportAsync(directory);

            Assert.True(File.Exists(result.DiffReportJsonPath));
            Assert.True(File.Exists(result.DiffReportTextPath));
            Assert.Contains("intersectionOverUnion", File.ReadAllText(result.DiffReportTextPath));
            Assert.Contains("\"InkMaskPolicy\"", File.ReadAllText(result.DiffReportJsonPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void InkBounds_IgnoresBaselineGuideColor()
    {
        RgbaImage image = CreateFilledImage(8, 8, FontReferenceOracleWorkflow.BackgroundColor);
        FillRect(image, 0, 4, 7, 4, FontReferenceOracleWorkflow.BaselineColor);
        image.SetPixel(5, 2, FontReferenceOracleWorkflow.ForegroundColor);

        PixelBounds? bounds = FontReferenceDiffWorkflow.ComputeInkBounds(image);

        Assert.Equal(new PixelBounds(5, 2, 5, 2), bounds);
    }

    [Fact]
    public void InkBounds_DetectsKnownSyntheticBounds()
    {
        RgbaImage image = CreateFilledImage(10, 10, FontReferenceOracleWorkflow.BackgroundColor);
        FillRect(image, 2, 3, 6, 7, FontReferenceOracleWorkflow.ForegroundColor);

        PixelBounds? bounds = FontReferenceDiffWorkflow.ComputeInkBounds(image);

        Assert.Equal(new PixelBounds(2, 3, 6, 7), bounds);
    }

    [Fact]
    public void DiffMetrics_ComputesIntersectionAndDeltas()
    {
        RgbaImage browser = CreateFilledImage(8, 8, FontReferenceOracleWorkflow.BackgroundColor);
        RgbaImage machina = CreateFilledImage(8, 8, FontReferenceOracleWorkflow.BackgroundColor);

        FillRect(browser, 1, 1, 3, 3, FontReferenceOracleWorkflow.ForegroundColor);
        FillRect(machina, 2, 2, 4, 4, FontReferenceOracleWorkflow.ForegroundColor);

        ImageDiffMetrics metrics = FontReferenceDiffWorkflow.ComputeMetrics(browser, machina);

        Assert.Equal(new PixelBounds(1, 1, 3, 3), metrics.BrowserInkBounds);
        Assert.Equal(new PixelBounds(2, 2, 4, 4), metrics.MachinaInkBounds);
        Assert.Equal(1, metrics.DeltaLeft);
        Assert.Equal(1, metrics.DeltaTop);
        Assert.Equal(1, metrics.DeltaRight);
        Assert.Equal(1, metrics.DeltaBottom);
        Assert.Equal(0, metrics.DeltaWidth);
        Assert.Equal(0, metrics.DeltaHeight);
        Assert.Equal(4, metrics.OverlapArea);
        Assert.Equal(5, metrics.BrowserOnlyArea);
        Assert.Equal(5, metrics.MachinaOnlyArea);
        Assert.Equal(4d / 14d, metrics.IntersectionOverUnion, 4);
    }

    [Fact]
    public async Task ReferenceDiffWorkflow_GeneratesExpectedArtifacts()
    {
        string directory = CreateDirectory();
        string capturePath = WriteBrowserCaptureJson(directory);
        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, capturePath);

            FontReferenceDiffExportResult result = await FontReferenceDiffWorkflow.ExportAsync(directory);

            Assert.True(File.Exists(result.DiffReportJsonPath));
            Assert.True(File.Exists(result.DiffReportTextPath));

            foreach (FontReferenceOracleDefinition definition in FontReferenceOracleWorkflow.Definitions)
            {
                Assert.True(File.Exists(Path.Combine(directory, definition.BrowserPngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.MachinaPngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.OverlayPngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.DiffPngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.ThresholdDiffPngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.WireframePngFileName)));
                Assert.True(File.Exists(Path.Combine(directory, definition.ComparePngFileName)));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task ReferenceDiffWorkflow_IsDeterministicForSameInputs()
    {
        string leftDirectory = CreateDirectory();
        string rightDirectory = CreateDirectory();
        string leftCapturePath = WriteBrowserCaptureJson(leftDirectory);
        string rightCapturePath = WriteBrowserCaptureJson(rightDirectory);
        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, leftCapturePath);
            FontReferenceDiffExportResult left = await FontReferenceDiffWorkflow.ExportAsync(leftDirectory);

            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, rightCapturePath);
            FontReferenceDiffExportResult right = await FontReferenceDiffWorkflow.ExportAsync(rightDirectory);

            foreach (FontReferenceOracleDefinition definition in FontReferenceOracleWorkflow.Definitions)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(leftDirectory, definition.OverlayPngFileName)),
                    File.ReadAllBytes(Path.Combine(rightDirectory, definition.OverlayPngFileName)));
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(leftDirectory, definition.DiffPngFileName)),
                    File.ReadAllBytes(Path.Combine(rightDirectory, definition.DiffPngFileName)));
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(leftDirectory, definition.WireframePngFileName)),
                    File.ReadAllBytes(Path.Combine(rightDirectory, definition.WireframePngFileName)));
            }

            FontReferenceDiffReport leftReport = JsonSerializer.Deserialize<FontReferenceDiffReport>(File.ReadAllText(left.DiffReportJsonPath))!;
            FontReferenceDiffReport rightReport = JsonSerializer.Deserialize<FontReferenceDiffReport>(File.ReadAllText(right.DiffReportJsonPath))!;

            Assert.Equal(leftReport.InkMaskPolicy, rightReport.InkMaskPolicy);
            Assert.Equal(leftReport.Fixtures.Select(static fixture => fixture.Metrics), rightReport.Fixtures.Select(static fixture => fixture.Metrics));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task ReferenceDiffWorkflow_ScriptWorkflowExportsArtifacts()
    {
        string directory = FontReferenceOracleWorkflow.GetRequestedOutputDirectoryOrCreateTemp();
        string capturePath = Path.Combine(directory, FontReferenceOracleWorkflow.BrowserTextMetricsFileName);

        Directory.CreateDirectory(directory);
        File.WriteAllText(capturePath, CreateBrowserCaptureJson());

        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, capturePath);

            FontReferenceDiffExportResult result = await FontReferenceDiffWorkflow.ExportAsync(directory);

            Assert.True(File.Exists(result.DiffReportJsonPath));
            Assert.True(File.Exists(result.DiffReportTextPath));
            Assert.True(File.Exists(Path.Combine(directory, "overlay-machina.png")));
            Assert.True(File.Exists(Path.Combine(directory, "diff-machina.png")));
            Assert.True(File.Exists(Path.Combine(directory, "wireframe-machina.png")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    private static FontReferenceOracleFixtureReport CreatePlacementFixture()
    {
        BrowserTextMetricsFixture browser = new(
            "synthetic",
            "Wireframe",
            "CrimsonText-Regular",
            32,
            16,
            12,
            2,
            8,
            true,
            8,
            "#ff0000",
            "alphabetic",
            "left",
            new BrowserTextMetricValues(6, 0, 6, 6, 1, 7, 2, null, null, 0, 4, -2),
            Background: "#101018",
            Foreground: "#f0f0f0");
        BrowserVerticalMetrics verticalMetrics = new(6, 1, 7, 2, null, null, 0, 4, -2, 2, 9, 1, 10, null, null);
        FontReferenceOracleGlyphRow glyph = new(
            0,
            "W",
            "U+0057",
            0x0057,
            "CrimsonText-Regular:0057@32",
            6,
            0,
            0,
            6,
            6,
            null,
            null,
            2,
            2,
            2,
            8,
            3,
            3,
            5,
            4,
            0,
            0,
            0,
            5,
            4,
            0,
            0,
            1,
            1,
            0,
            -5,
            5,
            -1,
            4,
            1,
            false);

        return new FontReferenceOracleFixtureReport(
            "synthetic",
            "Wireframe",
            6,
            "CrimsonText-Regular",
            32,
            16,
            12,
            8,
            true,
            8,
            FontReferenceOracleWorkflow.BaselineColor,
            2,
            9,
            -5,
            -1,
            2,
            6,
            browser,
            verticalMetrics,
            [glyph]);
    }

    private static string WriteBrowserCaptureJson(string directory)
    {
        string path = Path.Combine(directory, FontReferenceOracleWorkflow.BrowserTextMetricsFileName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, CreateBrowserCaptureJson());
        return path;
    }

    private static string CreateBrowserCaptureJson()
    {
        List<object> fixtures = [];
        int offset = 0;

        foreach (FontReferenceOracleDefinition definition in FontReferenceOracleWorkflow.Definitions)
        {
            RgbaImage image = CreateFilledImage(
                FontReferenceOracleWorkflow.ProofWidth,
                FontReferenceOracleWorkflow.ProofHeight,
                FontReferenceOracleWorkflow.BackgroundColor);

            int left = 8 + offset;
            int top = 14 + (offset % 3);
            int right = left + 48;
            int bottom = top + 18;

            FillRect(image, left, top, right, bottom, FontReferenceOracleWorkflow.ForegroundColor);
            FillRect(image, 0, (int)FontReferenceOracleWorkflow.ProofBaselineY, image.Width - 1, (int)FontReferenceOracleWorkflow.ProofBaselineY, FontReferenceOracleWorkflow.BaselineColor);

            fixtures.Add(new
            {
                id = definition.Id,
                text = definition.Text,
                fontFamily = "CrimsonText-Regular",
                fontSize = FontReferenceOracleWorkflow.ProofEmSize,
                canvasWidth = FontReferenceOracleWorkflow.ProofWidth,
                canvasHeight = FontReferenceOracleWorkflow.ProofHeight,
                x = FontReferenceOracleWorkflow.ProofOriginX,
                baselineY = FontReferenceOracleWorkflow.ProofBaselineY,
                baselineGuideEnabled = true,
                baselineGuideY = FontReferenceOracleWorkflow.ProofBaselineY,
                baselineGuideColor = "#ff0000",
                textBaseline = "alphabetic",
                textAlign = "left",
                background = "#101018",
                foreground = "#f0f0f0",
                metrics = new
                {
                    width = 56d,
                    actualBoundingBoxLeft = 0d,
                    actualBoundingBoxRight = 56d,
                    actualBoundingBoxAscent = 20d,
                    actualBoundingBoxDescent = 4d,
                    fontBoundingBoxAscent = 22d,
                    fontBoundingBoxDescent = 6d,
                    emHeightAscent = (double?)null,
                    emHeightDescent = (double?)null,
                    alphabeticBaseline = 0d,
                    hangingBaseline = 16d,
                    ideographicBaseline = -6d,
                },
                capture = new
                {
                    width = image.Width,
                    height = image.Height,
                    pixelFormat = "rgba8",
                    rgbaBase64 = Convert.ToBase64String(ToRgbaBytes(image)),
                },
            });

            offset += 3;
        }

        return JsonSerializer.Serialize(new
        {
            generatedAtUtc = "2026-06-28T00:00:00Z",
            browserPath = "synthetic",
            fixtureHtmlPath = "synthetic",
            fixtures,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static byte[] ToRgbaBytes(RgbaImage image)
    {
        byte[] bytes = new byte[image.Width * image.Height * 4];
        int index = 0;

        foreach (Rgba32 pixel in image.Pixels)
        {
            bytes[index++] = pixel.R;
            bytes[index++] = pixel.G;
            bytes[index++] = pixel.B;
            bytes[index++] = pixel.A;
        }

        return bytes;
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        RgbaImage image = new(width, height);
        CpuDistanceFieldGlyphRenderer.Fill(image, color);
        return image;
    }

    private static void FillRect(RgbaImage image, int left, int top, int right, int bottom, Rgba32 color)
    {
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                image.SetPixel(x, y, color);
            }
        }
    }

    private static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8r-tests", Guid.NewGuid().ToString("N"));
    }
}
