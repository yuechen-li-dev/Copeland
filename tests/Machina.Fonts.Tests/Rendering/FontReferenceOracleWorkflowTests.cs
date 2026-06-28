using Xunit;

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
}
