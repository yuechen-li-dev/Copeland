using System.Text.Json;

namespace Machina.Presenter.Sample;

public static class AurelianTestDocsDogfoodManifest
{
    public static (string jsonPath, string textPath) Write(
        string outputDirectory,
        AurelianTestDocsDogfoodManifestData data)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "aurelian-test-docs-dogfood-manifest.json");
        string textPath = Path.Combine(outputDirectory, "aurelian-test-docs-dogfood-manifest.txt");

        var manifest = new
        {
            milestone = "M13c",
            kind = "aurelian-test-normalization-docs-dogfood",
            aurelianTestNormalizationFixed = data.AurelianTestNormalizationFixed,
            aurelianSolutionRestoreStatus = data.AurelianSolutionRestoreStatus,
            aurelianSolutionBuildStatus = data.AurelianSolutionBuildStatus,
            aurelianSolutionTestStatus = data.AurelianSolutionTestStatus,
            shaderLineEndingNormalization = data.ShaderLineEndingNormalization,
            aurelianDocsLoaded = data.AurelianDocsLoaded,
            aurelianDocsDiagnostics = data.AurelianDocsDiagnostics,
            docsLoadedTotal = data.DocsLoadedTotal,
            diagnosticsTotal = data.DiagnosticsTotal,
            sdslvMigrationPerformed = false,
            machinaAurelianBridgeImplemented = false,
            vulkanPresenterIntegrationPerformed = false,
            repoRenamed = false,
            deferredWork = data.DeferredWork.ToArray(),
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M13c",
            "kind=aurelian-test-normalization-docs-dogfood",
            $"aurelianTestNormalizationFixed={FormatBoolean(data.AurelianTestNormalizationFixed)}",
            $"aurelianSolutionRestoreStatus={data.AurelianSolutionRestoreStatus}",
            $"aurelianSolutionBuildStatus={data.AurelianSolutionBuildStatus}",
            $"aurelianSolutionTestStatus={data.AurelianSolutionTestStatus}",
            $"shaderLineEndingNormalization={FormatBoolean(data.ShaderLineEndingNormalization)}",
            $"aurelianDocsLoaded={data.AurelianDocsLoaded}",
            $"aurelianDocsDiagnostics={data.AurelianDocsDiagnostics}",
            $"docsLoadedTotal={data.DocsLoadedTotal}",
            $"diagnosticsTotal={data.DiagnosticsTotal}",
            "sdslvMigrationPerformed=false",
            "machinaAurelianBridgeImplemented=false",
            "vulkanPresenterIntegrationPerformed=false",
            "repoRenamed=false",
            $"deferredWork={string.Join(" | ", data.DeferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }
}

public sealed record AurelianTestDocsDogfoodManifestData(
    bool AurelianTestNormalizationFixed,
    string AurelianSolutionRestoreStatus,
    string AurelianSolutionBuildStatus,
    string AurelianSolutionTestStatus,
    bool ShaderLineEndingNormalization,
    int AurelianDocsLoaded,
    int AurelianDocsDiagnostics,
    int DocsLoadedTotal,
    int DiagnosticsTotal,
    IReadOnlyList<string> DeferredWork);
