using Machina.Fonts.ReferenceRendering;
using Xunit;
using System.Text.Json;

namespace Machina.Fonts.Tests.Rendering;

public sealed class FontReferenceOracleWorkflowTests
{
    [Fact]
    public async Task FontReferenceOracleWorkflow_WritesExpectedArtifacts()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);

        Assert.True(File.Exists(result.TomlPath));
        Assert.True(File.Exists(result.PlacementReportTextPath));
        Assert.True(File.Exists(result.PlacementReportJsonPath));
        Assert.Equal(FontReferenceOracleWorkflow.Definitions.Count, result.Artifacts.Count);

        foreach (FontReferenceOracleArtifact artifact in result.Artifacts)
        {
            Assert.True(File.Exists(artifact.MachinaPpmPath));
            Assert.True(File.Exists(artifact.MachinaPngPath));
        }
    }

    [Fact]
    public async Task ReferenceOracle_MachinaComparisonArtifactsGenerate()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);

        Assert.Equal(FontReferenceOracleWorkflow.Definitions.Count, result.Artifacts.Count);
        Assert.True(File.Exists(result.PlacementReportTextPath));
        Assert.True(File.Exists(result.PlacementReportJsonPath));
    }

    [Fact]
    public async Task FontReferenceOracleWorkflow_PlacementReportContainsKerningRows()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
        string report = File.ReadAllText(result.PlacementReportTextPath);

        Assert.Contains("[kerning] AV To Ta Wa Yo", report);
        Assert.Contains("pairAdjustX", report);
        Assert.Contains("<space>", report);
        Assert.Contains("CrimsonText-Regular:0056", report);
    }

    [Fact]
    public async Task ReferenceOracle_PlacementReportIncludesPlacementFields()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
        string report = File.ReadAllText(result.PlacementReportTextPath);

        Assert.Contains("planeBounds", report);
        Assert.Contains("pixelRange", report);
        Assert.Contains("projectionScale", report);
    }

    [Fact]
    public async Task GlyphPlacementReport_IncludesVerticalMetrics()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
        string report = File.ReadAllText(result.PlacementReportTextPath);

        Assert.Contains("coordinateConvention", report);
        Assert.Contains("computedTextTop", report);
        Assert.Contains("computedTextBottom", report);
        Assert.Contains("minPlaneTop", report);
        Assert.Contains("maxPlaneBottom", report);
        Assert.Contains("inkTop", report);
        Assert.Contains("inkBottom", report);
        Assert.Contains("descentBelowBaseline", report);
        Assert.Contains("browserInkBottom", report);
        Assert.Contains("penX", report);
        Assert.Contains("drawWidth", report);
        Assert.Contains("drawHeight", report);
    }

    [Fact]
    public async Task ReferenceOracle_ReportIncludesCoverageMetrics()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
        string report = File.ReadAllText(result.PlacementReportTextPath);
        string json = File.ReadAllText(result.PlacementReportJsonPath);

        Assert.Contains("alphaCoverageCount_above_001", report);
        Assert.Contains("alphaCoverageCount_above_010", report);
        Assert.Contains("alphaCoverageCount_above_050", report);
        Assert.Contains("averageAlphaNonZero", report);
        Assert.Contains("\"AlphaCoverageCountAbove001\"", json);
        Assert.Contains("\"DescentBelowBaseline\"", json);
    }

    [Fact]
    public void ReferenceOracle_CoverageScanIgnoresBaselineGuideColor()
    {
        RgbaImage image = new(4, 3);
        Rgba32 background = new(16, 16, 24, 255);
        Rgba32 foreground = new(240, 240, 240, 255);
        Rgba32 baselineGuide = new(255, 0, 0, 255);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image.SetPixel(x, y, background);
            }
        }

        for (int x = 0; x < image.Width; x++)
        {
            image.SetPixel(x, 1, baselineGuide);
        }

        image.SetPixel(2, 0, foreground);

        CoverageScanResult coverage = FontReferenceOracleWorkflow.ScanCoverageForTest(image, baselineY: 1d, ignoredColor: baselineGuide);

        Assert.Equal(0, coverage.InkTop);
        Assert.Equal(0, coverage.InkBottom);
        Assert.Equal(2, coverage.InkLeft);
        Assert.Equal(2, coverage.InkRight);
        Assert.Equal(-1d, coverage.DescentBelowBaseline);
    }

    [Fact]
    public async Task MachinaCoverageMetrics_ReportsDescentBelowBaseline()
    {
        string directory = CreateDirectory();

        await FontReferenceOracleWorkflow.ExportAsync(directory);
        FontReferenceOraclePlacementReport report = FontReferenceOracleWorkflow.ReadPlacementReportForTest(directory);

        FontReferenceOracleFixtureReport fixture = Assert.Single(report.Fixtures, static item => item.Id == "machina");

        Assert.Equal(-1d, fixture.DescentBelowBaseline);
        Assert.True(fixture.AlphaCoverageCountAbove001 > fixture.AlphaCoverageCountAbove050);
    }

    [Fact]
    public async Task ReferenceOracle_ReportIncludesBrowserAndMachinaVerticalMetrics()
    {
        string directory = CreateDirectory();
        string metricsPath = Path.Combine(directory, FontReferenceOracleWorkflow.BrowserTextMetricsFileName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(metricsPath, CreateBrowserMetricsJson());

        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, metricsPath);

            FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
            string report = File.ReadAllText(result.PlacementReportTextPath);
            string json = File.ReadAllText(result.PlacementReportJsonPath);

            Assert.Equal(metricsPath, result.BrowserMetricsJsonPath);
            Assert.Contains("browserActualTop", report);
            Assert.Contains("browserFontBottom", report);
            Assert.Contains("browserInkBottom", report);
            Assert.Contains("\"BrowserVerticalMetrics\"", json);
            Assert.Contains("\"BrowserInkBottom\"", json);
            Assert.Contains("\"ComputedTextTop\"", json);
            Assert.Contains("\"CoordinateConvention\"", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ReferenceOracle_TextMetricsScriptContainsBaselineAndBoundsCapture()
    {
        string scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "tools",
            "font-reference",
            "reference-render.js");
        string script = File.ReadAllText(Path.GetFullPath(scriptPath));

        Assert.Contains("measureText", script);
        Assert.Contains("context.textBaseline = \"alphabetic\"", script);
        Assert.Contains("drawBaselineGuide", script);
        Assert.Contains("baselineGuideEnabled", script);
        Assert.Contains("actualBoundingBoxAscent", script);
        Assert.Contains("fontBoundingBoxAscent", script);
        Assert.Contains("alphabeticBaseline", script);
    }

    [Fact]
    public void ReferenceOracle_BaselineGuideIsEnabledInExport()
    {
        FontProofExportOptions options = FontReferenceOracleWorkflow.CreateOptions(CreateDirectory());
        string scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "tools",
            "Export-MachinaFontReferenceComparison.ps1");
        string script = File.ReadAllText(Path.GetFullPath(scriptPath));

        Assert.True(options.ShowBaselineGuide);
        Assert.Equal(new Rgba32(255, 0, 0, 255), options.BaselineGuideColor);
        Assert.Contains("showBaselineGuide = \"true\"", script);
        Assert.Contains("baselineGuideColor = \"#ff0000\"", script);
    }

    [Fact]
    public async Task ReferenceOracle_ReportIncludesBaselineGuideMetadata()
    {
        string directory = CreateDirectory();
        string metricsPath = Path.Combine(directory, FontReferenceOracleWorkflow.BrowserTextMetricsFileName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(metricsPath, CreateBrowserMetricsJson());

        string? previous = Environment.GetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, metricsPath);

            FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
            string report = File.ReadAllText(result.PlacementReportTextPath);
            string json = File.ReadAllText(result.PlacementReportJsonPath);

            Assert.Contains("baselineGuideEnabled: true", report);
            Assert.Contains("baselineGuideY: 40", report);
            Assert.Contains("browserBaselineGuideEnabled: true", report);
            Assert.Contains("browserBaselineGuideColor: #ff0000", report);
            Assert.Contains("\"BaselineGuideEnabled\": true", json);
            Assert.Contains("\"BaselineGuideY\": 40", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FontReferenceOracleWorkflow.BrowserMetricsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_CrimsonTextBaselineRegression()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);
        FontReferenceOraclePlacementReport report = JsonSerializer.Deserialize<FontReferenceOraclePlacementReport>(
            File.ReadAllText(result.PlacementReportJsonPath))!;

        FontReferenceOracleFixtureReport fixture = Assert.Single(report.Fixtures, static item => item.Id == "machina");
        FontReferenceOracleGlyphRow glyph = Assert.Single(
            fixture.Glyphs,
            static item => item.Character == "i" && !item.IsWhitespace);
        GlyphFieldPlacement placement = new(
            glyph.PlaneLeft!.Value,
            glyph.PlaneTop!.Value,
            glyph.PlaneRight!.Value,
            glyph.PlaneBottom!.Value,
            glyph.PixelRange!.Value,
            glyph.ProjectionScale!.Value);

        int baselineInOutput = CpuDistanceFieldGlyphRenderer.ComputeBaselineOffsetInOutput(
            placement,
            glyph.DrawHeight!.Value);

        Assert.Equal(40d, glyph.BaselineY);
        Assert.Equal(40, glyph.DrawY!.Value + baselineInOutput);
        Assert.Equal(16, glyph.DrawY.Value);
    }

    [Fact]
    public async Task FontReferenceOracleWorkflow_PngArtifactsHavePngSignature()
    {
        string directory = CreateDirectory();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);

        foreach (FontReferenceOracleArtifact artifact in result.Artifacts)
        {
            byte[] bytes = File.ReadAllBytes(artifact.MachinaPngPath);
            Assert.True(bytes.Length > 8);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
    }

    [Fact]
    public async Task FontReferenceOracleWorkflow_ScriptWorkflowExportsArtifacts()
    {
        string directory = FontReferenceOracleWorkflow.GetRequestedOutputDirectoryOrCreateTemp();

        FontReferenceOracleExportResult result = await FontReferenceOracleWorkflow.ExportAsync(directory);

        Assert.All(result.Artifacts, static artifact =>
        {
            Assert.True(File.Exists(artifact.MachinaPpmPath));
            Assert.True(File.Exists(artifact.MachinaPngPath));
        });
        Assert.True(File.Exists(result.PlacementReportTextPath));
        Assert.True(File.Exists(result.PlacementReportJsonPath));
        Assert.True(File.Exists(result.CoverageExperimentJsonPath));
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_ProofStringsRemainNonBlank()
    {
        string directory = CreateDirectory();

        await FontReferenceOracleWorkflow.ExportAsync(directory);
        FontReferenceOraclePlacementReport report = FontReferenceOracleWorkflow.ReadPlacementReportForTest(directory);

        Assert.All(report.Fixtures, static fixture => Assert.True(fixture.AlphaCoverageCountAbove001 > 0));
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_OutputIsDeterministic()
    {
        string firstDirectory = CreateDirectory();
        string secondDirectory = CreateDirectory();

        FontReferenceOracleExportResult first = await FontReferenceOracleWorkflow.ExportAsync(firstDirectory);
        FontReferenceOracleExportResult second = await FontReferenceOracleWorkflow.ExportAsync(secondDirectory);

        Assert.Equal(
            File.ReadAllText(first.PlacementReportJsonPath),
            File.ReadAllText(second.PlacementReportJsonPath));
        Assert.Equal(
            File.ReadAllText(first.CoverageExperimentJsonPath),
            File.ReadAllText(second.CoverageExperimentJsonPath));
    }

    [Fact]
    public async Task TypographyMsdfReferenceRender_LowerInkExtentDoesNotRegressAgainstM8q2Baseline()
    {
        string directory = CreateDirectory();

        await FontReferenceOracleWorkflow.ExportAsync(directory);
        FontReferenceOraclePlacementReport report = FontReferenceOracleWorkflow.ReadPlacementReportForTest(directory);
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "artifacts", "m8q2", FontReferenceOracleWorkflow.PlacementReportJsonFileName)));

        Dictionary<string, int> baselineDescentByFixture = baseline.RootElement
            .GetProperty("Fixtures")
            .EnumerateArray()
            .ToDictionary(
                static fixture => fixture.GetProperty("Id").GetString()!,
                static fixture => fixture.GetProperty("MaxInkBottom").GetInt32() - (int)Math.Round(fixture.GetProperty("BaselineY").GetDouble(), MidpointRounding.AwayFromZero),
                StringComparer.Ordinal);

        Assert.All(report.Fixtures, fixture =>
        {
            if (fixture.DescentBelowBaseline is null)
            {
                return;
            }

            Assert.True(
                fixture.DescentBelowBaseline.Value <= baselineDescentByFixture[fixture.Id],
                $"Fixture '{fixture.Id}' regressed below the M8q2 lower-ink extent baseline.");
        });
    }

    private static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8o-tests", Guid.NewGuid().ToString("N"));
    }

    private static string CreateBrowserMetricsJson()
    {
        return """
        {
          "generatedAtUtc": "2026-06-28T00:00:00Z",
          "browserPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
          "fixtureHtmlPath": "tools/font-reference/reference-render.html",
          "fixtures": [
            {
              "id": "machina",
              "text": "Machina",
              "fontFamily": "CrimsonText-Regular",
              "fontSize": 32,
              "canvasWidth": 320,
              "canvasHeight": 64,
              "x": 8,
              "baselineY": 40,
              "baselineGuideEnabled": true,
              "baselineGuideY": 40,
              "baselineGuideColor": "#ff0000",
              "textBaseline": "alphabetic",
              "textAlign": "left",
              "metrics": {
                "width": 109.875,
                "actualBoundingBoxLeft": 0,
                "actualBoundingBoxRight": 110.65625,
                "actualBoundingBoxAscent": 22,
                "actualBoundingBoxDescent": 0,
                "fontBoundingBoxAscent": 30,
                "fontBoundingBoxDescent": 11,
                "emHeightAscent": null,
                "emHeightDescent": null,
                "alphabeticBaseline": 0,
                "hangingBaseline": 24,
                "ideographicBaseline": -11
              },
              "coverage": {
                "inkTop": 18,
                "inkBottom": 39,
                "inkLeft": 8,
                "inkRight": 118,
                "inkHeight": 22,
                "inkWidth": 111,
                "alphaCoverageCountAbove001": 1059,
                "alphaCoverageCountAbove010": 1021,
                "alphaCoverageCountAbove050": 749,
                "maxAlpha": 1,
                "averageAlphaNonZero": 0.706,
                "baselineY": 40,
                "descentBelowBaseline": -1
              }
            }
          ]
        }
        """;
    }
}
