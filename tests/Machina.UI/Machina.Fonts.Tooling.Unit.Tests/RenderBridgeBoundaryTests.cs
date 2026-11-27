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
        string[] projectFiles = [.. ProjectFiles(repoRoot)];
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

    /// <summary>
    /// Build-output and tool folders hold no project file that a build consumes, but they hold most of the checkout
    /// (artifacts alone passed 110k files). Walking them made this test grow with every render and build until it hung CI.
    /// </summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "artifacts", "bin", "obj", "outputs", ".git", ".vs", ".idea", "node_modules", "TestResults",
    };

    private static IEnumerable<string> ProjectFiles(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string pattern in new[] { "*.csproj", "*.props", "*.targets" })
                foreach (string file in Directory.EnumerateFiles(directory, pattern))
                    yield return file;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(child);
                if (SkippedDirectories.Contains(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFiles(IReadOnlyList<string> directories, params string[] patterns)
    {
        return directories
            .SelectMany(directory => patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
