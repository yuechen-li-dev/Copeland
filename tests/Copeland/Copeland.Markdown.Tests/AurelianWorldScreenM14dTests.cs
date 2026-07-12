using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class AurelianWorldScreenM14dTests
{
    [Fact]
    public void M14dDocs_Exist()
    {
        string root = GetRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "Aurelian", "history", "aurelian-world-screen-m14d.md")));
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "m14d", "aurelian-world-screen-manifest.json")));
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "m14d", "aurelian-world-screen-manifest.txt")));
    }

    [Fact]
    public void M14dManifest_RecordsWorldScreenAndBoundaryGuards()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M14d", root.GetProperty("milestone").GetString());
        Assert.True(root.GetProperty("aurelianWorldScreenImplemented").GetBoolean());
        Assert.True(root.GetProperty("usesPresenterScreenStack").GetBoolean());
        Assert.Equal("world", root.GetProperty("worldLayer").GetString());
        Assert.True(root.GetProperty("collectionExpressionLayerOrderUsed").GetBoolean());
        Assert.True(root.GetProperty("visibleTriangleRuntimePathPreserved").GetBoolean());
        Assert.False(root.GetProperty("visibleTriangleRenderedLocally").GetBoolean());
        Assert.False(root.GetProperty("machinaOverlayImplemented").GetBoolean());
        Assert.False(root.GetProperty("vdMirDefaultChanged").GetBoolean());
        Assert.True(root.GetProperty("directHlslPathPreserved").GetBoolean());
        Assert.False(root.GetProperty("copelandPackageExtractionPerformed").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("shaderKernelSplitIntroduced").GetBoolean());
        Assert.False(root.GetProperty("oblivionIntegrationPerformed").GetBoolean());
    }

    [Fact]
    public void M14dDocs_DescribePresenterWorldScreenAndNoMachinaOverlay()
    {
        string doc = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "Aurelian", "history", "aurelian-world-screen-m14d.md"));

        Assert.Contains("VisibleTriangleWorldScreen", doc, StringComparison.Ordinal);
        Assert.Contains("PresenterScreenStack", doc, StringComparison.Ordinal);
        Assert.Contains("ScreenLayers.World", doc, StringComparison.Ordinal);
        Assert.Contains("No Machina HUD or overlay is implemented yet", doc, StringComparison.Ordinal);
        Assert.Contains("ScreenLayerOrder order =", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibleTriangleReadme_DocumentsWorldScreenPath()
    {
        string readme = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle", "README.md"));

        Assert.Contains("VisibleTriangleWorldScreen", readme, StringComparison.Ordinal);
        Assert.Contains("PresenterScreenStack", readme, StringComparison.Ordinal);
        Assert.Contains("ScreenLayerOrder order =", readme, StringComparison.Ordinal);
        Assert.Contains("No Machina HUD or overlay is implemented yet", readme, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m14d", "aurelian-world-screen-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
