using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class MsdfAlignmentExportFixture : IAsyncLifetime
{
    private string? rootDirectory;

    public MsdfAlignmentExportPair ExportPair { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        rootDirectory = ToolingIntegrationTestEnvironment.CreateDirectory("machina-m11b-msdf-regression");
        string beforeDirectory = Path.Combine(rootDirectory, "before");
        string afterDirectory = Path.Combine(rootDirectory, "after");

        FontDiagnosticArtifactExporter exporter = ToolingIntegrationTestEnvironment.CreateExporter();
        FontDiagnosticExportResult before = await exporter.ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMsdfRegressionOptions(beforeDirectory, scaleExperimentalFieldWithEmSize: false));
        FontDiagnosticExportResult after = await exporter.ExportAsync(
            ToolingIntegrationTestEnvironment.CreateMsdfRegressionOptions(afterDirectory, scaleExperimentalFieldWithEmSize: true));

        ExportPair = new MsdfAlignmentExportPair(before, after);
    }

    public Task DisposeAsync()
    {
        if (rootDirectory is not null && Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }
}

public sealed record MsdfAlignmentExportPair(
    FontDiagnosticExportResult Before,
    FontDiagnosticExportResult After);
