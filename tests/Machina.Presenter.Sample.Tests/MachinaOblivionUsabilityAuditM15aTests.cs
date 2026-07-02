using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaOblivionUsabilityAuditM15aTests
{
    [Fact]
    public void M15aUsabilityAuditDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina", "machina-oblivion-usability-reentry-audit-m15a.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Oblivion", "oblivion-card-readability-audit-m15a.md")));
    }

    [Fact]
    public void M15aManifest_RecordsAuditOnly()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M15a", root.GetProperty("milestone").GetString());
        Assert.Equal("machina-oblivion-usability-reentry-audit", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("userFeedbackCaptured").GetBoolean());
        Assert.False(root.GetProperty("implementationFixesPerformed").GetBoolean());
        Assert.True(root.GetProperty("windowResizeIssueDocumented").GetBoolean());
        Assert.True(root.GetProperty("layoutRecompositionIssueDocumented").GetBoolean());
        Assert.True(root.GetProperty("cardPreviewReadabilityIssueDocumented").GetBoolean());
        Assert.True(root.GetProperty("wordWrapIssueDocumented").GetBoolean());
        Assert.True(root.GetProperty("contrastIssueDocumented").GetBoolean());
        Assert.True(root.GetProperty("inspectorReadabilityAudited").GetBoolean());
    }

    [Fact]
    public void M15aManifest_RecordsM15bRecommendation()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M15b", root.GetProperty("proposedNextMilestone").GetString());

        string[] proofArtifacts = root
            .GetProperty("proofArtifacts")
            .EnumerateArray()
            .Select(static element => element.GetString()!)
            .ToArray();

        Assert.Equal(
            [
                "artifacts/m15a/m15a-oblivion-cards-current.png",
                "artifacts/m15a/m15a-oblivion-docs-compact-current.png",
                "artifacts/m15a/m15a-oblivion-docs-current.png",
            ],
            proofArtifacts);
    }

    [Fact]
    public void M15aManifest_RecordsNoEditorExecutionWork()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("editorImplemented").GetBoolean());
        Assert.False(root.GetProperty("markdownEditingImplemented").GetBoolean());
        Assert.False(root.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(root.GetProperty("roslynExecutionImplemented").GetBoolean());
        Assert.False(root.GetProperty("aurelianWorkPerformed").GetBoolean());
        Assert.False(root.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static JsonDocument LoadManifest()
    {
        string manifestPath = Path.Combine(
            RepoRoot,
            "artifacts",
            "m15a",
            "machina-oblivion-usability-audit-manifest.json");

        return JsonDocument.Parse(File.ReadAllText(manifestPath));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
