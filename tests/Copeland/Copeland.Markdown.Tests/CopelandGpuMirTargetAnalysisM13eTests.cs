using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class CopelandGpuMirTargetAnalysisM13eTests
{
    [Fact]
    public void M13eDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Aurelian", "history", "aurelian-sdslv-lane-audit-m13e.md")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "copeland-gpu-mir-target-analysis-m13e.md")));
    }

    [Fact]
    public void M13eManifest_RecordsReconOnly()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M13e", root.GetProperty("milestone").GetString());
        Assert.Equal("sdslv-lane-audit-gpu-mir-target-analysis", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("sdslvMigrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("gpuMirImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("hlslBackendChanged").GetBoolean());
        Assert.False(root.GetProperty("machinaAurelianBridgeImplemented").GetBoolean());
        Assert.False(root.GetProperty("vulkanPresenterIntegrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("repoRenamed").GetBoolean());
    }

    [Fact]
    public void M13eManifest_RecordsOneGpuMirStartingAssumption()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("currentPipelineDocumented").GetBoolean());
        Assert.True(root.GetProperty("backendNeutralConceptsDocumented").GetBoolean());
        Assert.True(root.GetProperty("hlslDxcSpecificConceptsDocumented").GetBoolean());
        Assert.True(root.GetProperty("hiddenMirCandidatesDocumented").GetBoolean());
        Assert.True(root.GetProperty("gpuMirTargetAnalysisDocumented").GetBoolean());
        Assert.True(root.GetProperty("oneGpuMirStartingAssumption").GetBoolean());
        Assert.False(root.GetProperty("shaderKernelMirSplitPerformed").GetBoolean());
        Assert.True(root.GetProperty("splitDeferredUntilProvenNecessary").GetBoolean());
    }

    [Fact]
    public void M13e_DoesNotCreateCopelandMirGpuProject()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Mir.Gpu")));
    }

    [Fact]
    public void M13e_DoesNotCreatePtxBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Ptx")));
    }

    [Fact]
    public void M13e_DoesNotCreateSlangBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Slang")));
    }

    [Fact]
    public void M13e_DoesNotMoveAurelianShaders()
    {
        Assert.True(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Aurelian", "Aurelian.Shaders")));
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Frontends.Sdslv")));
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m13e", "sdslv-gpu-mir-audit-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
