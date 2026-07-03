using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaLayoutAuthoringParityCloseoutM17fTests
{
    [Fact]
    public void M17fCloseoutDocs_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina", "machina-layout-authoring-parity-closeout-m17f.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Machina", "machina-layout-parity-next-steps-m17f.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17f", "machina-layout-authoring-parity-closeout-manifest.json")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17f", "machina-layout-authoring-parity-closeout-manifest.txt")));
    }

    [Fact]
    public void M17fManifest_RecordsM17ArcClosed()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M17f", root.GetProperty("milestone").GetString());
        Assert.Equal("machina-layout-authoring-parity-closeout", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("m17ArcClosed").GetBoolean());
        Assert.True(root.GetProperty("stackAuthoringBaselineDocumented").GetBoolean());
        Assert.True(root.GetProperty("gridAuthoringBaselineDocumented").GetBoolean());
        Assert.True(root.GetProperty("remainingParityGapsDocumented").GetBoolean());
        Assert.True(root.GetProperty("nextStepsDocumented").GetBoolean());
    }

    [Fact]
    public void M17fManifest_RecordsNoRuntimeBehaviorChange()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("runtimeBehaviorChanged").GetBoolean());
        Assert.False(root.GetProperty("newLayoutPrimitiveImplemented").GetBoolean());
        Assert.False(root.GetProperty("uiStackChanged").GetBoolean());
        Assert.False(root.GetProperty("uiGridChanged").GetBoolean());
        Assert.False(root.GetProperty("cardRendererChanged").GetBoolean());
        Assert.False(root.GetProperty("pageLayoutChanged").GetBoolean());
    }

    [Fact]
    public void M17fManifest_RecordsRemainingParityGaps()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("proportionalUiLengthImplemented").GetBoolean());
        Assert.False(root.GetProperty("rowVariantsImplemented").GetBoolean());
        Assert.False(root.GetProperty("guideFrameImplemented").GetBoolean());
        Assert.False(root.GetProperty("deusMachineImplemented").GetBoolean());
        Assert.Equal("Option E: Layout cleanup and bugfix pass", root.GetProperty("recommendedNextDirection").GetString());
    }

    [Fact]
    public void M17f_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();

        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17f_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();

        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17fDocs_RecordDocOnlyCloseoutAndCurrentBaseline()
    {
        string closeoutDoc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina", "machina-layout-authoring-parity-closeout-m17f.md"));
        string nextStepsDoc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "Machina", "machina-layout-parity-next-steps-m17f.md"));

        Assert.Contains("M17f is the doc-only closeout", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("The M17 Stack/Grid authoring parity arc is closed.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("M17f does not mean layout authoring is finished.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("Option E: Layout cleanup and bugfix pass.", closeoutDoc, StringComparison.Ordinal);
        Assert.Contains("The stack/grid authoring parity arc is now closed enough", nextStepsDoc, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17f",
            "machina-layout-authoring-parity-closeout-manifest.json")));
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
