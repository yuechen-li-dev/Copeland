using Xunit;

namespace Machina.Fonts.Tooling.Tests;

[Collection("EnvironmentVariable")]
public sealed class FontDiagnosticExportSmokeTests
{
    [Fact]
    public async Task ScriptSmoke_FontDiagnosticsExport_ExportsArtifacts()
    {
        string directory = ToolingIntegrationTestEnvironment.GetRequestedOutputDirectoryOrCreateTemp(
            "MACHINA_FONT_DIAGNOSTICS_OUTPUT_DIR",
            "machina-font-tooling-smoke-tests");
        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateScriptSmokeOptions(directory));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.LayerCompositionReportJsonPath));
        Assert.True(File.Exists(result.LayerCompositionReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));

        foreach (string presetName in result.LayerCompositionReport.PresetsGenerated)
        {
            Assert.True(File.Exists(Path.Combine(directory, "32", $"m9d-{presetName}-machina.png")));
        }
    }
}
