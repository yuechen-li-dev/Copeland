using System.Text.Json;
using Xunit;

namespace Copeland.Markdown.Tests;

public sealed class VdMirArchitectureDoctrineM13fTests
{
    [Fact]
    public void VdMirDoctrineDoc_Exists()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "vd-mir-architecture-doctrine-m13f.md")));
    }

    [Fact]
    public void VdMirManifest_RecordsArchitectureOnly()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("M13f", root.GetProperty("milestone").GetString());
        Assert.Equal("vd-mir-architecture-doctrine", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("vdMirImplemented").GetBoolean());
        Assert.False(root.GetProperty("sdslvMigrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("hlslBackendChanged").GetBoolean());
        Assert.False(root.GetProperty("slangBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("ptxBackendImplemented").GetBoolean());
        Assert.False(root.GetProperty("shaderKernelMirSplitPerformed").GetBoolean());
        Assert.False(root.GetProperty("visibleTriangleWiredToVdMir").GetBoolean());
        Assert.False(root.GetProperty("machinaAurelianBridgeImplemented").GetBoolean());
        Assert.False(root.GetProperty("vulkanPresenterIntegrationPerformed").GetBoolean());
        Assert.False(root.GetProperty("repoRenamed").GetBoolean());
        Assert.True(root.GetProperty("stagedArchitectureDocumented").GetBoolean());
    }

    [Fact]
    public void VdMirManifest_RecordsVisualDirectMirName()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.Equal("VD-MIR", root.GetProperty("name").GetString());
        Assert.Equal("Visual Direct MIR", root.GetProperty("expandedName").GetString());
        Assert.Equal("GPU MIR", root.GetProperty("priorWorkingName").GetString());
    }

    [Fact]
    public void VdMirManifest_RecordsOneCommonMirAssumption()
    {
        using JsonDocument document = LoadManifest();
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("oneCommonMirStartingAssumption").GetBoolean());
        Assert.True(root.GetProperty("aurelianVisibleTrianglePresent").GetBoolean());
    }

    [Fact]
    public void VdMirManifest_RecordsNoImplementation()
    {
        string text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "Copeland", "history", "vd-mir-architecture-doctrine-m13f.md"));

        Assert.Contains("M13f defines `VD-MIR` as the future common GPU-oriented MIR candidate for Visionary/Copeland.", text, StringComparison.Ordinal);
        Assert.Contains("M13f does not implement M0.", text, StringComparison.Ordinal);
        Assert.Contains("Use one `VD-MIR` for shader and compute GPU programs until evidence proves a split is necessary.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void M13f_DoesNotCreateVdMirPackage()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Mir.Vd")));
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Mir.VdMir")));
    }

    [Fact]
    public void M13f_DoesNotCreatePtxBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Ptx")));
    }

    [Fact]
    public void M13f_DoesNotCreateSlangBackend()
    {
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Backends.Slang")));
    }

    [Fact]
    public void M13f_DoesNotWireVisibleTriangleToVdMir()
    {
        Assert.True(Directory.Exists(Path.Combine(GetRepositoryRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle")));
        Assert.False(Directory.Exists(Path.Combine(GetRepositoryRoot(), "src", "Copeland." + "Frontends.Sdslv")));
    }

    [Fact]
    public void AurelianVisibleTriangleSample_IsPresent()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "samples", "Aurelian", "Aurelian.VisibleTriangle", "Aurelian.VisibleTriangle.csproj")));
    }

    private static JsonDocument LoadManifest()
    {
        string path = Path.Combine(GetRepositoryRoot(), "artifacts", "m13f", "vd-mir-architecture-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
