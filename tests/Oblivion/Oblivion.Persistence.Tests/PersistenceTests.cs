using Oblivion.Persistence;
using Xunit;

namespace Oblivion.Persistence.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void Canonical_format_1_workspace_loads_with_stable_order()
    {
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OblivionSampleWorkspace", "workspace.oblivion.json");
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(manifestPath, useCache: false);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("machina-sample", result.Workspace!.Id.Value);
        Assert.Equal(["cards", "execution-roadmap", "artifacts", "docs"], result.Workspace.Pages.Select(page => page.Id.Value));
        Assert.Equal("oblivion-intro-note-card", result.Workspace.Pages[0].Cards[0].Id.Value);
        Assert.Equal("oblivion-substrate-status", result.Workspace.Pages[0].Cards[2].Id.Value);
    }

    [Fact]
    public void Json_and_toml_writes_are_deterministic_and_semantically_round_trip()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OblivionSampleWorkspace", "workspace.oblivion.json"));
        OblivionWorkspaceManifest manifest = Assert.IsType<OblivionWorkspaceManifest>(OblivionWorkspaceJsonReader.Read(json).Manifest);
        string first = OblivionWorkspaceJsonWriter.Write(manifest);
        string second = OblivionWorkspaceJsonWriter.Write(OblivionWorkspaceJsonReader.Read(first).Manifest!);
        Assert.Equal(first, second);

        string cardPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OblivionSampleWorkspace", "cards", "oblivion-substrate-status.card.toml");
        OblivionCardAssetDocument card = Assert.IsType<OblivionCardAssetDocument>(OblivionCardTomlReader.Read(File.ReadAllText(cardPath)).Document);
        Assert.Equal(OblivionCardTomlWriter.Write(card), OblivionCardTomlWriter.Write(card));
    }

    [Fact]
    public void Path_traversal_is_rejected()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "oblivion-path-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(testRoot, "workspace.oblivion.json"),
                """{"format":1,"kind":"oblivion-workspace","workspaceId":"w","title":"W","sections":[{"id":"s","title":"S","pages":[{"id":"p","title":"P","cards":["../escape.card.toml"]}]}]}""");
            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(testRoot, "workspace.oblivion.json"), useCache: false);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "path-traversal-not-allowed");
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}
