using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class RenderBridgeBoundaryTests
{
    [Fact]
    public void ProductionProjects_DoNotReferenceFontsToolingOrDiagnostics()
    {
        string repoRoot = FindRepoRoot();
        string[] directories =
        [
            Path.Combine(repoRoot, "src", "Machina.Standard"),
            Path.Combine(repoRoot, "src", "Machina.Core"),
            Path.Combine(repoRoot, "src", "Machina.Dominatus"),
            Path.Combine(repoRoot, "src", "Machina.Renderer.Raster"),
            Path.Combine(repoRoot, "src", "Machina.Pipeline"),
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
        string repoRoot = FindRepoRoot();
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

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
