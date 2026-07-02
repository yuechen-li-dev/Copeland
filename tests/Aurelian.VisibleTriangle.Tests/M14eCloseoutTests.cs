using System.Text.Json;
using Xunit;

namespace Aurelian.VisibleTriangle.Tests;

public sealed class M14eCloseoutTests
{
    [Fact]
    public void M14eCloseoutDocs_Exist()
    {
        var repoRoot = GetRepoRoot();

        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "Aurelian", "aurelian-migration-closeout-m14e.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "Visionary", "visionary-subsystem-handoff-m14e.md")));
    }

    [Fact]
    public void M14eManifest_RecordsAurelianArcClosed()
    {
        using JsonDocument document = LoadManifest();

        Assert.Equal("M14e", document.RootElement.GetProperty("milestone").GetString());
        Assert.True(document.RootElement.GetProperty("aurelianMigrationArcClosed").GetBoolean());
        Assert.True(document.RootElement.GetProperty("goldenPathDocumented").GetBoolean());
    }

    [Fact]
    public void M14eManifest_RecordsVdMirDoctrineOnly()
    {
        using JsonDocument document = LoadManifest();

        Assert.Equal("doctrine-only", document.RootElement.GetProperty("vdMirStatus").GetString());
        Assert.False(document.RootElement.GetProperty("vdMirImplemented").GetBoolean());
        Assert.False(document.RootElement.GetProperty("visibleTriangleThroughVdMir").GetBoolean());
    }

    [Fact]
    public void M14eManifest_RecordsReturnToMachinaOblivion()
    {
        using JsonDocument document = LoadManifest();

        Assert.True(document.RootElement.GetProperty("returnToMachinaOblivionRecommended").GetBoolean());
        Assert.True(document.RootElement.GetProperty("futureAurelianReviewerLaneDocumented").GetBoolean());
        Assert.True(document.RootElement.GetProperty("futureCopelandVdMirReviewerLaneDocumented").GetBoolean());
    }

    [Fact]
    public void M14e_DoesNotAddAurelianToCopelandSolutions()
    {
        var repoRoot = GetRepoRoot();
        var copelandSolution = File.ReadAllText(Path.Combine(repoRoot, "Copeland.slnx"));
        var copelandSlowSolution = File.ReadAllText(Path.Combine(repoRoot, "Copeland.Slow.slnx"));

        Assert.DoesNotContain("Aurelian", copelandSolution, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian", copelandSlowSolution, StringComparison.Ordinal);
    }

    private static JsonDocument LoadManifest()
    {
        var repoRoot = GetRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "artifacts", "m14e", "aurelian-migration-closeout-manifest.json");
        return JsonDocument.Parse(File.ReadAllText(manifestPath));
    }

    private static string GetRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Copeland.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                Directory.Exists(Path.Combine(current.FullName, "artifacts")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
