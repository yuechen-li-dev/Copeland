using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaJsParityLayoutReconM17aTests
{
    [Fact]
    public void M17aReconDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina.UI", "history", "machina-js-parity-layout-recon-m17a.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina.UI", "history", "machina-layout-authoring-backlog-m17a.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17a", "machina-js-parity-layout-recon-manifest.txt")));
    }

    [Fact]
    public void M17aManifest_RecordsReconOnly()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M17a", root.GetProperty("milestone").GetString());
        Assert.Equal("machina-js-parity-layout-recon", root.GetProperty("kind").GetString());
        Assert.Equal("recon-only", root.GetProperty("validationStatus").GetString());
        Assert.True(root.GetProperty("auditInputReviewed").GetBoolean());
    }

    [Fact]
    public void M17aManifest_RecordsStackArrangeRecommendedFirst()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.True(root.GetProperty("stackArrangeRecommendedFirst").GetBoolean());
        Assert.True(root.GetProperty("gridArrangeRecommendedSecond").GetBoolean());
        Assert.True(root.GetProperty("migrationLadderDocumented").GetBoolean());
    }

    [Fact]
    public void M17aManifest_RecordsNoRuntimeBehaviorChange()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("runtimeBehaviorChanged").GetBoolean());
        Assert.False(root.GetProperty("stackArrangeImplemented").GetBoolean());
        Assert.False(root.GetProperty("gridArrangeImplemented").GetBoolean());
        Assert.False(root.GetProperty("guideFrameImplemented").GetBoolean());
        Assert.False(root.GetProperty("rowVariantsImplemented").GetBoolean());
        Assert.False(root.GetProperty("proportionalUiLengthImplemented").GetBoolean());
        Assert.False(root.GetProperty("deusMachineImplemented").GetBoolean());
    }

    [Fact]
    public void M17a_DoesNotImplementEditingOrExecution()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("editorImplemented").GetBoolean());
        Assert.False(root.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(root.GetProperty("aurelianWorkPerformed").GetBoolean());
        Assert.False(root.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17aReconDocs_RecordReconOnlyBoundary()
    {
        string reconDoc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina.UI", "history", "machina-js-parity-layout-recon-m17a.md"));
        string backlogDoc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina.UI", "history", "machina-layout-authoring-backlog-m17a.md"));

        Assert.Contains("M17a is a recon-only milestone", reconDoc, StringComparison.Ordinal);
        Assert.Contains("does not implement new layout primitives", reconDoc, StringComparison.Ordinal);
        Assert.Contains("without attempting a one-shot port", backlogDoc, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17a",
            "machina-js-parity-layout-recon-manifest.json")));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
