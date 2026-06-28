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
        Assert.Contains("minInkTop", report);
        Assert.Contains("maxInkBottom", report);
        Assert.Contains("penX", report);
        Assert.Contains("drawWidth", report);
        Assert.Contains("drawHeight", report);
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
            Assert.Contains("\"BrowserVerticalMetrics\"", json);
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
        Assert.Contains("actualBoundingBoxAscent", script);
        Assert.Contains("fontBoundingBoxAscent", script);
        Assert.Contains("alphabeticBaseline", script);
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
              }
            }
          ]
        }
        """;
    }
}
