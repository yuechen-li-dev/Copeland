using System.Text.Json;
using Xunit;

namespace Machina.Core.Tests;

public sealed class M18bBoundaryAuditTests
{
    [Fact]
    public void M18bDocumentsAndManifest_RecordTheProductBoundary()
    {
        string repositoryRoot = GetRepositoryRoot();
        string productContractPath = Path.Combine(
            repositoryRoot,
            "docs",
            "Oblivion",
            "oblivion-product-contract-m18b.md");
        string responsibilityAuditPath = Path.Combine(
            repositoryRoot,
            "docs",
            "Machina",
            "machina-ui-responsibility-audit-m18b.md");
        string manifestPath = Path.Combine(
            repositoryRoot,
            "artifacts",
            "m18b",
            "m18b-oblivion-machina-boundary-manifest.json");

        Assert.True(File.Exists(productContractPath));
        Assert.True(File.Exists(responsibilityAuditPath));
        Assert.True(File.Exists(manifestPath));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;

        Assert.Equal("M18b", root.GetProperty("milestone").GetString());
        Assert.Equal("A", root.GetProperty("outcome").GetString());
        Assert.True(root.GetProperty("oblivionProductContractDefined").GetBoolean());
        Assert.True(root.GetProperty("machinaUiResponsibilityAuditCompleted").GetBoolean());
        Assert.True(root.GetProperty("presenterClassifiedAsDevTool").GetBoolean());
        Assert.True(root.GetProperty("oblivionClassifiedAsFirstClassProduct").GetBoolean());
        Assert.False(root.GetProperty("fullOblivionExtractionPerformed").GetBoolean());
        Assert.True(root.GetProperty("knownPreM18cViolations").GetInt32() > 0);
    }

    [Fact]
    public void MachinaUiProductionSource_DoesNotReferenceOblivion()
    {
        string machinaSourceRoot = Path.Combine(GetRepositoryRoot(), "src", "Machina.UI");
        string[] sourceFiles = Directory.GetFiles(machinaSourceRoot, "*.cs", SearchOption.AllDirectories);

        List<string> violations = sourceFiles
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("Oblivion", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(machinaSourceRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Machina.UI production source must remain product-neutral. Violations: {string.Join(", ", violations)}");
    }

    private static bool IsBuildOutput(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }
}
