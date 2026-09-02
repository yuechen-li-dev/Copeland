using Xunit;

namespace Machina.Core.Tests;

public sealed class MachinaProductNeutralityTests
{
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
