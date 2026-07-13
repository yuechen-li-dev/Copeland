using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class RenderBridgeBoundaryTests
{
    [Fact]
    public void ProductionProjects_DoNotReferenceFontsToolingOrDiagnostics()
    {
        string repoRoot = ToolingUnitTestEnvironment.FindRepoRoot();
        string[] directories =
        [
            Path.Combine(repoRoot, "src", "Machina.UI", "Machina.Standard"),
            Path.Combine(repoRoot, "src", "Machina.UI", "Machina.Core"),
            Path.Combine(repoRoot, "src", "Machina.UI", "Machina.Dominatus"),
            Path.Combine(repoRoot, "src", "Machina.UI", "Machina.Pipeline"),
        ];

        List<string> matches = [];
        foreach (string file in EnumerateFiles(directories, "*.cs", "*.csproj", "*.props", "*.targets"))
        {
            string content = File.ReadAllText(file);
            if (content.Contains("Machina.Fonts.Tooling", StringComparison.Ordinal)
                || content.Contains("Machina.Fonts.Diagnostics", StringComparison.Ordinal))
            {
                matches.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.Empty(matches);
    }

    [Fact]
    public void ProjectFiles_DoNotIntroduceForbiddenFontDependencies()
    {
        string repoRoot = ToolingUnitTestEnvironment.FindRepoRoot();
        string[] projectFiles = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] forbiddenReferences =
        [
            "PackageReference Include=\"SixLabors",
            "PackageReference Include=\"ImageSharp",
            "PackageReference Include=\"FreeType",
            "PackageReference Include=\"SharpFont",
            "PackageReference Include=\"MSDF-Sharp.Extensions",
        ];

        List<string> matches = [];
        foreach (string file in projectFiles)
        {
            string content = File.ReadAllText(file);
            if (forbiddenReferences.Any(content.Contains))
            {
                matches.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.Empty(matches);
    }

    private static IReadOnlyList<string> EnumerateFiles(IReadOnlyList<string> directories, params string[] patterns)
    {
        return directories
            .SelectMany(directory => patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
