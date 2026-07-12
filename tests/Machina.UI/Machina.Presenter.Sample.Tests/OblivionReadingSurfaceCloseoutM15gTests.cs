using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionReadingSurfaceCloseoutM15gTests
{
    [Fact]
    public void M15gCloseoutDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Oblivion", "oblivion-reading-surface-closeout-m15g.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina.UI", "history", "machina-oblivion-ux-backlog-m15g.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m15g", "oblivion-reading-surface-closeout-manifest.txt")));
    }

    [Fact]
    public void M15gManifest_RecordsM15ArcClosed()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M15g", root.GetProperty("milestone").GetString());
        Assert.Equal("oblivion-reading-surface-closeout", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("m15ArcClosed").GetBoolean());
        Assert.True(root.GetProperty("goldenPathDocumented").GetBoolean());
        Assert.True(root.GetProperty("uxBacklogDocumented").GetBoolean());
    }

    [Fact]
    public void M15gManifest_RecordsNoRuntimeBehaviorChange()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("runtimeBehaviorChanged").GetBoolean());
        Assert.False(root.GetProperty("newFeatureWorkPerformed").GetBoolean());
        Assert.False(root.GetProperty("markdownEditingImplemented").GetBoolean());
        Assert.False(root.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(root.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M15gManifest_RecordsM16aRecommendation()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M16a", root.GetProperty("recommendedNextMilestone").GetString());
        Assert.Equal("Oblivion reading navigation and focus affordances", root.GetProperty("recommendedNextMilestoneName").GetString());
    }

    [Fact]
    public void M15g_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();

        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15g_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();

        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("arbitrary2DLayoutSolverImplemented").GetBoolean());
    }

    [Fact]
    public void M15g_DoesNotImplementEditingOrExecution()
    {
        string closeoutDoc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Oblivion", "oblivion-reading-surface-closeout-m15g.md"));

        Assert.Contains("M15g is closeout/planning only.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("Selection couples main stack and inspector content.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("Scrolling does not.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("Inspector scroll is not composition-only yet.", closeoutDoc, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m15g",
            "oblivion-reading-surface-closeout-manifest.json")));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
