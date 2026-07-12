using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class CopelandWorkshopArchitectureM13dTests
{
    [Fact]
    public void CopelandWorkshopDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "copeland-compiler-workshop-architecture-m13d.md")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "copeland-compiler-lane-taxonomy-m13d.md")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "README.md")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "architecture", "copeland-roadmap.md")));
    }

    [Fact]
    public void CopelandWorkshopDocs_StateNoUniversalIrMandate()
    {
        string text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "copeland-compiler-workshop-architecture-m13d.md"));

        Assert.Contains("It does not require every frontend to lower into one universal IR.", text, StringComparison.Ordinal);
        Assert.Contains("Do not turn Copeland into MLIR-lite.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CopelandWorkshopDocs_StateNoCopelandShadersMonolith()
    {
        string text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "copeland-compiler-workshop-architecture-m13d.md"));

        Assert.Contains("Why not only Copeland.Shaders", text, StringComparison.Ordinal);
        Assert.Contains("`Copeland.Shaders` may become a useful package name", text, StringComparison.Ordinal);
        Assert.Contains("it is too narrow to describe the architecture as a whole", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CopelandWorkshopManifest_RecordsNoMigration()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m13d", "copeland-compiler-workshop-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal("M13d", root.GetProperty("milestone").GetString());
        Assert.Equal("copeland-compiler-workshop-architecture", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("universalIrMandated").GetBoolean());
        Assert.False(root.GetProperty("copelandShadersMonolithChosen").GetBoolean());
        Assert.True(root.GetProperty("compilerWorkshopDoctrineDocumented").GetBoolean());
        Assert.True(root.GetProperty("laneTaxonomyDocumented").GetBoolean());
        Assert.False(root.GetProperty("sdslvMigrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("copelandShadersImplemented").GetBoolean());
        Assert.False(root.GetProperty("gpuTsImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("octReimplementationPerformed").GetBoolean());
        Assert.False(root.GetProperty("machinaAurelianBridgeImplemented").GetBoolean());
        Assert.False(root.GetProperty("vulkanPresenterIntegrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("repoRenamed").GetBoolean());
    }

    [Fact]
    public void CopelandWorkshopManifest_RecordsLaneCandidates()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m13d", "copeland-compiler-workshop-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        string[] candidates = document.RootElement
            .GetProperty("futureLaneCandidates")
            .EnumerateArray()
            .Select(element => element.GetString())
            .OfType<string>()
            .ToArray();

        Assert.Equal(
            [
                "markdown/document",
                "typescript/script",
                "sdslv/shader",
                "gpu-typescript/kernel",
                "oct/numeric",
            ],
            candidates);
    }

    [Fact]
    public void M13d_DoesNotCreateCopelandShadersProject()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland.Shaders")));
    }

    [Fact]
    public void M13d_DoesNotMoveAurelianShaders()
    {
        Assert.True(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Aurelian", "Aurelian.Shaders")));
    }

    [Fact]
    public void M13d_DoesNotAddPtxBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Ptx")));
    }

    [Fact]
    public void M13d_DoesNotAddGpuTsFrontend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Frontends.GpuTs")));
    }

    [Fact]
    public void M13d_DoesNotRenameRepo()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "Copeland.slnx")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "Machina.UI.Slow.slnx")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "Aurelian.slnx")));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
