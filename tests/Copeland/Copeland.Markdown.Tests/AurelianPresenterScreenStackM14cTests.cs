using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class AurelianPresenterScreenStackM14cTests
{
    [Fact]
    public void M14cDocs_Exist()
    {
        string root = GetRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "Aurelian", "history", "aurelian-presenter-screen-stack-m14c.md")));
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "m14c", "presenter-screen-stack-manifest.json")));
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "m14c", "presenter-screen-stack-manifest.txt")));
    }

    [Fact]
    public void M14cManifest_RecordsScreenStackAndBoundaryGuards()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M14c", root.GetProperty("milestone").GetString());
        Assert.True(root.GetProperty("screenLayerOrderImplemented").GetBoolean());
        Assert.True(root.GetProperty("collectionExpressionLayerDeclarationsSupported").GetBoolean());
        Assert.True(root.GetProperty("presenterScreenStackImplemented").GetBoolean());
        Assert.True(root.GetProperty("standardSemanticLayersImplemented").GetBoolean());
        Assert.False(root.GetProperty("aurelianWorldScreenImplemented").GetBoolean());
        Assert.False(root.GetProperty("machinaOverlayImplemented").GetBoolean());
        Assert.False(root.GetProperty("visibleTriangleBehaviorChanged").GetBoolean());
        Assert.False(root.GetProperty("vdMirDefaultChanged").GetBoolean());
        Assert.False(root.GetProperty("copelandPackageExtractionPerformed").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("shaderKernelSplitIntroduced").GetBoolean());
        Assert.False(root.GetProperty("oblivionIntegrationPerformed").GetBoolean());
    }

    [Fact]
    public void M14cDoc_DocumentsCollectionExpressionOrderingAndFutureWorldHudIntent()
    {
        string doc = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "Aurelian", "history", "aurelian-presenter-screen-stack-m14c.md"));

        Assert.Contains("ScreenLayerOrder order =", doc, StringComparison.Ordinal);
        Assert.Contains("Aurelian output should be treated as a Presenter screen/layer", doc, StringComparison.Ordinal);
        Assert.Contains("Aurelian will land in the `world` layer", doc, StringComparison.Ordinal);
        Assert.Contains("Machina HUD/overlay screens will land in upper layers", doc, StringComparison.Ordinal);
        Assert.Contains("unknown layer", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisibleTriangleReadme_NotesFutureScreenStackIntegration()
    {
        string readme = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle", "README.md"));

        Assert.Contains("future M14c/M14d layering direction", readme, StringComparison.Ordinal);
        Assert.Contains("world layer", readme, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m14c", "presenter-screen-stack-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
