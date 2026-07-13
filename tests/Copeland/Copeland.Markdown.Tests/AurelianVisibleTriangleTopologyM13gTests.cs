using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class AurelianVisibleTriangleTopologyM13gTests
{
    [Fact]
    public void M13gDocs_Exist()
    {
        string root = GetRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "Aurelian", "history", "aurelian-visible-triangle-topology-audit-m13g.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", "Copeland", "history", "vd-mir-visible-triangle-proof-boundary-m13g.md")));
    }

    [Fact]
    public void AurelianVisibleTriangleSample_ProjectExists()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "samples", "Integrations", "Aurelian.VisibleTriangle", "Aurelian.VisibleTriangle.csproj")));
    }

    [Fact]
    public void AurelianVisibleTriangleSample_IsExcludedFromAurelianProductionTestSolution()
    {
        string text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "Aurelian.slnx"));
        Assert.DoesNotContain("Aurelian.VisibleTriangle", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AurelianVisibleTriangleManifest_RecordsNoVdMirWiring()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M13g", root.GetProperty("milestone").GetString());
        Assert.Equal("aurelian-visible-triangle-topology-audit", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("sampleProjectPresent").GetBoolean());
        Assert.True(root.GetProperty("sampleIncludedInAurelianSolution").GetBoolean());
        Assert.False(root.GetProperty("vdMirImplemented").GetBoolean());
        Assert.False(root.GetProperty("visibleTriangleWiredToVdMir").GetBoolean());
        Assert.False(root.GetProperty("sdslvMigrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("hlslBackendChanged").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("machinaAurelianBridgeImplemented").GetBoolean());
        Assert.False(root.GetProperty("vulkanPresenterIntegrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("repoRenamed").GetBoolean());
        Assert.True(root.GetProperty("futureProofPathDocumented").GetBoolean());
    }

    [Fact]
    public void M13g_DoesNotCreateVdMirPackage()
    {
        string root = GetRepositoryRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Mir.Vd")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Mir.VdMir")));
    }

    [Fact]
    public void M13g_DoesNotAddVisibleTriangleToCopelandSolution()
    {
        string root = GetRepositoryRoot();
        string fast = File.ReadAllText(Path.Combine(root, "Copeland.slnx"));
        string slow = File.ReadAllText(Path.Combine(root, "Machina.UI.Slow.slnx"));

        Assert.DoesNotContain("Aurelian.VisibleTriangle", fast, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.VisibleTriangle", slow, StringComparison.Ordinal);
    }

    [Fact]
    public void M13g_DoesNotCreatePtxBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Ptx")));
    }

    [Fact]
    public void M13g_DoesNotCreateSlangBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Slang")));
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m13g", "aurelian-visible-triangle-topology-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
