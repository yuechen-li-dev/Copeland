using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class AurelianPresenterSilkM14bTests
{
    [Fact]
    public void M14bDocs_Exist()
    {
        string root = GetRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "Aurelian", "history", "aurelian-presenter-silk-golden-triangle-m14b.md")));
        Assert.True(File.Exists(Path.Combine(root, "samples", "Aurelian", "Aurelian.VisibleTriangle", "README.md")));
    }

    [Fact]
    public void M14bManifest_RecordsPresenterBackendAndBoundaryGuards()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M14b", root.GetProperty("milestone").GetString());
        Assert.True(root.GetProperty("silkPresenterBackendImplemented").GetBoolean());
        Assert.True(root.GetProperty("visibleTriangleOptInPathImplemented").GetBoolean());
        Assert.True(root.GetProperty("usesPresenterBackend").GetBoolean());
        Assert.True(root.GetProperty("usesCompositorPassthrough").GetBoolean());
        Assert.True(root.GetProperty("defaultCompilerPathPreserved").GetBoolean());
        Assert.False(root.GetProperty("vdMirDefaultChanged").GetBoolean());
        Assert.False(root.GetProperty("copelandPackageExtractionPerformed").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("shaderKernelSplitIntroduced").GetBoolean());
        Assert.False(root.GetProperty("machinaIntegrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("oblivionIntegrationPerformed").GetBoolean());
    }

    [Fact]
    public void VisibleTriangleReadme_DocumentsPresenterSilkCommand()
    {
        string readme = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle", "README.md"));

        Assert.Contains("--presenter silk", readme, StringComparison.Ordinal);
        Assert.Contains("Presenter/Silk.NET backend", readme, StringComparison.Ordinal);
        Assert.Contains("Aurelian renders, Presenter owns window/frame/input/present", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void M14b_DoesNotCreateForbiddenPackages()
    {
        string root = GetRepositoryRoot();

        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Mir.Vd")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Mir.VdMir")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Backends.Ptx")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Copeland." + "Backends.Slang")));
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m14b", "presenter-silk-triangle-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
