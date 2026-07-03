using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaUiGridAuthoringM17dTests
{
    [Fact]
    public void M17dDocsAndArtifacts_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina", "machina-ui-grid-authoring-m17d.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17d", "machina-ui-grid-authoring-manifest.json")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17d", "machina-ui-grid-authoring-manifest.txt")));
    }

    [Fact]
    public void M17dManifest_RecordsGridAuthoringOnlyImplementation()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M17d", root.GetProperty("milestone").GetString());
        Assert.Equal("machina-ui-grid-authoring", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("uiGridImplemented").GetBoolean());
        Assert.True(root.GetProperty("usesExistingGridArrange").GetBoolean());
        Assert.True(root.GetProperty("usesExistingCellFrame").GetBoolean());
        Assert.False(root.GetProperty("newLowLevelLayoutEngineImplemented").GetBoolean());
        Assert.True(root.GetProperty("fixedTracksSupported").GetBoolean());
        Assert.True(root.GetProperty("fillTracksSupported").GetBoolean());
        Assert.True(root.GetProperty("explicitCellsSupported").GetBoolean());
        Assert.True(root.GetProperty("matrixCellsSupported").GetBoolean());
        Assert.False(root.GetProperty("cellSpansImplemented").GetBoolean());
        Assert.False(root.GetProperty("oblivionPageLayoutRefactored").GetBoolean());
        Assert.True(root.GetProperty("stackBehaviorPreserved").GetBoolean());
        Assert.False(root.GetProperty("guideFrameImplemented").GetBoolean());
        Assert.False(root.GetProperty("rowVariantsImplemented").GetBoolean());
        Assert.False(root.GetProperty("proportionalUiLengthImplemented").GetBoolean());
        Assert.False(root.GetProperty("deusMachineImplemented").GetBoolean());
        Assert.False(root.GetProperty("runtimeBehaviorChanged").GetBoolean());
        Assert.True(root.GetProperty("playbackSuitePassed").GetBoolean());
    }

    [Fact]
    public void M17dDocs_RecordExistingEngineReuseAndDeferredPageRefactor()
    {
        string doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina", "machina-ui-grid-authoring-m17d.md"));

        Assert.Contains("authoring surface over the existing Machina.Layout grid engine", doc, StringComparison.Ordinal);
        Assert.Contains("does not implement a second layout engine", doc, StringComparison.Ordinal);
        Assert.Contains("Regular grids should be authorable as a 2D/matrix shape", doc, StringComparison.Ordinal);
        Assert.Contains("Sparse or advanced grids should be authorable as explicit cells", doc, StringComparison.Ordinal);
        Assert.Contains("M17e will use the tool to refactor the Oblivion page shell", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void M17d_DoesNotRefactorOblivionPageLayout()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("oblivionPageLayoutRefactored").GetBoolean());
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17d",
            "machina-ui-grid-authoring-manifest.json")));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
