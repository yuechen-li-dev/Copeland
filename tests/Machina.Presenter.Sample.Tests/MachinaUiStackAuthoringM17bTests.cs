using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaUiStackAuthoringM17bTests
{
    [Fact]
    public void M17bDocsAndManifest_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina", "machina-ui-stack-authoring-m17b.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17b", "machina-ui-stack-authoring-manifest.json")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17b", "machina-ui-stack-authoring-manifest.txt")));
    }

    [Fact]
    public void M17bManifest_RecordsAuthoringOnlyStackImplementation()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M17b", root.GetProperty("milestone").GetString());
        Assert.Equal("machina-ui-stack-authoring", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("uiStackImplemented").GetBoolean());
        Assert.True(root.GetProperty("usesExistingStackArrange").GetBoolean());
        Assert.True(root.GetProperty("usesExistingFillFrame").GetBoolean());
        Assert.False(root.GetProperty("newLowLevelLayoutEngineImplemented").GetBoolean());
        Assert.False(root.GetProperty("oblivionCardRendererRefactored").GetBoolean());
        Assert.False(root.GetProperty("gridImplemented").GetBoolean());
    }

    [Fact]
    public void M17bDocs_RecordNoOblivionMigrationYet()
    {
        string doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina", "machina-ui-stack-authoring-m17b.md"));
        string backlog = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina", "machina-layout-authoring-backlog-m17a.md"));

        Assert.Contains("authoring-level stack surface", doc, StringComparison.Ordinal);
        Assert.Contains("no Oblivion renderer migration yet", doc, StringComparison.Ordinal);
        Assert.Contains("M17b implementation landed", backlog, StringComparison.Ordinal);
    }

    [Fact]
    public void M17b_DoesNotRefactorOblivionCardRenderer()
    {
        string renderer = File.ReadAllText(Path.Combine(
            RepoRoot,
            "samples",
            "Machina.Presenter.Sample",
            "OblivionCardRenderer.cs"));

        Assert.DoesNotContain("UI.Stack(", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("UI.VStack(", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("UI.HStack(", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void M17b_DoesNotImplementGrid()
    {
        string ui = File.ReadAllText(Path.Combine(RepoRoot, "src", "Machina.Core", "Authoring", "UI.cs"));
        Assert.DoesNotContain("public static UiNode Grid(", ui, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17b",
            "machina-ui-stack-authoring-manifest.json")));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
