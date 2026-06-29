using Xunit;

namespace Machina.Fonts.Tooling.Tests;

[Collection("EnvironmentVariable")]
public sealed class MsdfAlignmentSmokeTests
{
    [Fact]
    public async Task ScriptSmoke_M9fWorkflowExportsArtifacts()
    {
        string outputDirectory = ToolingIntegrationTestEnvironment.GetRequestedOutputDirectoryOrCreateTemp(
            "MACHINA_M9F_OUTPUT_DIR",
            "machina-m9f-smoke-tests");
        bool scaleExperimentalFieldWithEmSize = ReadBoolean("MACHINA_M9F_SCALE_FIELDS", defaultValue: true);

        FontDiagnosticExportResult result = await ToolingIntegrationTestEnvironment.CreateExporter().ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMsdfSmokeOptions(outputDirectory, scaleExperimentalFieldWithEmSize));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-settings.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-msdf-debug-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-cad-debug-hello-machina.png")));
    }

    private static bool ReadBoolean(string variableName, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return bool.TryParse(value, out bool parsed)
            ? parsed
            : defaultValue;
    }
}
