using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class FontDiagnosticExportTests
{
    [Fact]
    public async Task FontDiagnosticsExport_UsesLayerPreset()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportOptions options = ToolingIntegrationTestEnvironment.CreateMinimalOptions(directory) with
        {
            PresetNames = ["cad-debug"],
        };

        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(options);

        Assert.Single(result.LayerCompositionReport.Artifacts);
        Assert.All(
            result.LayerCompositionReport.Artifacts,
            artifact =>
            {
                Assert.Equal("cad-debug", artifact.PresetName);
                Assert.True(artifact.PresetAvailability.Complete);
                Assert.Equal("DirectOutlineStatic", artifact.ReferenceRenderStrategy);
                Assert.Contains("DirectOutlineStatic", artifact.RenderStrategies);
                Assert.Contains(artifact.Layers, layer => layer.Id == "grid");
                Assert.Contains(artifact.Layers, layer => layer.Id == "axes");
                Assert.Contains(artifact.Layers, layer => layer.Id == "baseline");
            });
    }

    [Fact]
    public async Task FontDiagnosticsExport_WritesPresetArtifacts()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");

        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateDefaultIntegrationOptions(directory));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.LayerCompositionReportJsonPath));
        Assert.True(File.Exists(result.LayerCompositionReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-direct-vs-msdf-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-cad-debug-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9d-direct-vs-msdf-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9d-cad-debug-hello-machina.png")));
    }

    [Fact]
    public async Task FontDiagnosticsExport_IsDeterministic()
    {
        string firstDirectory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        string secondDirectory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportOptions firstOptions = ToolingIntegrationTestEnvironment.CreateMinimalOptions(firstDirectory);
        FontDiagnosticExportOptions secondOptions = ToolingIntegrationTestEnvironment.CreateMinimalOptions(secondDirectory);

        FontDiagnosticExportResult first = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(firstOptions);
        FontDiagnosticExportResult second = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(secondOptions);

        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ShapeDiffReportJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ShapeDiffReportJsonPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.LayerCompositionReportJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.LayerCompositionReportJsonPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ManifestJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ManifestJsonPath), secondDirectory));

        string firstRepresentativePng = Path.Combine(firstDirectory, "32", "m9d-direct-vs-msdf-hello-machina.png");
        string secondRepresentativePng = Path.Combine(secondDirectory, "32", "m9d-direct-vs-msdf-hello-machina.png");
        Assert.Equal(HashFile(firstRepresentativePng), HashFile(secondRepresentativePng));
    }

    [Fact]
    public async Task Export_AllowPartialWritesWarningWhenBrowserMissing()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMinimalOptions(directory) with
            {
                PresetNames = ["browser-vs-direct"],
                AllowPartial = true,
            });

        Assert.False(result.Manifest.Complete);
        Assert.Contains(result.Manifest.Warnings, warning => warning.Contains("degraded", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Manifest.Errors);
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-browser-vs-direct-hello-machina.png")));
    }

    [Fact]
    public async Task Export_DirectVsMsdfSucceedsWithoutBrowser()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMinimalOptions(directory) with
            {
                PresetNames = ["direct-vs-msdf"],
            });

        Assert.True(result.Manifest.Complete);
        Assert.Empty(result.Manifest.Errors);
        Assert.Contains(result.Manifest.PresetReports, report => report.PresetName == "direct-vs-msdf" && report.Complete);
    }

    [Fact]
    public async Task Export_WritesManifestJson()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMinimalOptions(directory));

        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.Contains("\"Kind\": \"machina-font-diagnostic-export\"", File.ReadAllText(result.ManifestJsonPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_WritesManifestText()
    {
        string directory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-font-tooling-slow-tests");
        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMinimalOptions(directory));

        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.Contains("Machina Font Toolkit M9d export manifest", File.ReadAllText(result.ManifestTextPath), StringComparison.Ordinal);
        Assert.Equal("DirectOutlineStatic", result.Manifest.TextBackend.StaticDefault);
        Assert.Equal("MsdfScalableExperimental", result.Manifest.TextBackend.ScalableExperimental);
    }

    private static string NormalizeReport(string content, string outputDirectory)
    {
        string fullPath = Path.GetFullPath(outputDirectory);
        string escapedPath = fullPath.Replace("\\", "\\\\", StringComparison.Ordinal);

        return content
            .Replace(fullPath, "<out>", StringComparison.OrdinalIgnoreCase)
            .Replace(escapedPath, "<out>", StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
