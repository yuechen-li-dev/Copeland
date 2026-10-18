using System.Text.RegularExpressions;
using Xunit;

namespace Aurelian.Games.Tests;

/// <summary>
/// Aetheris enters Aurelian only through bridge projects. The engine core, the game shell and 2D games build without it.
/// See docs/Aurelian/aetheris-boundary.md.
/// </summary>
public sealed class AetherisBoundaryTests
{
    /// <summary>The only projects allowed to reference the sibling Aetheris repository directly.</summary>
    private static readonly string[] Bridges =
    [
        "src/Aurelian/Aurelian.Cloth3D/Aurelian.Cloth3D.csproj",
        "src/Integrations/Aurelian.Humanoid/Aurelian.Humanoid.csproj",
        "src/Integrations/Aurelian.Lighting.Aetheris/Aurelian.Lighting.Aetheris.csproj",
        "tools/Aurelian.AetherisBake/Aurelian.AetherisBake.csproj",
    ];

    /// <summary>Projects that must not reach Aetheris even transitively.</summary>
    private static readonly string[] AetherisFree =
    [
        "src/Aurelian/Aurelian.Spatial3D/Aurelian.Spatial3D.csproj",
        "src/Aurelian/Aurelian.World/Aurelian.World.csproj",
        "src/Aurelian/Aurelian.Graphics/Aurelian.Graphics.csproj",
        "src/Integrations/Aurelian.NativeComposition/Aurelian.NativeComposition.csproj",
        "src/Integrations/Aurelian.Games/Aurelian.Games.csproj",
        "Games/TinyFarm/TinyFarm/TinyFarm.csproj",
        "Games/Hearth/Hearth/Hearth.csproj",
        "Games/Cloudstep/Cloudstep/Cloudstep.csproj",
    ];

    private static readonly Regex Reference = new("<ProjectReference\\s+Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);

    [Fact]
    public void OnlyBridgesReferenceTheAetherisRepository()
    {
        string root = RepositoryRoot();
        var offenders = new List<string>();
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            string relative = Relative(root, project);
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.StartsWith("tests/", StringComparison.Ordinal))
            {
                continue;
            }
            if (References(project).Any(IsAetheris) && !Bridges.Contains(relative))
            {
                offenders.Add(relative);
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheCoreShellAndTwoDimensionalGamesNeverReachAetheris()
    {
        string root = RepositoryRoot();
        foreach (string project in AetherisFree)
        {
            string path = Path.Combine(root, project);
            Assert.True(File.Exists(path), project);
            string? chain = FindAetheris(path, [], [Relative(root, path)], root);
            Assert.True(chain is null, $"{project} reaches Aetheris: {chain}");
        }
    }

    private static string? FindAetheris(string project, HashSet<string> visited, List<string> chain, string root)
    {
        if (!visited.Add(project))
        {
            return null;
        }
        foreach (string reference in References(project))
        {
            if (IsAetheris(reference))
            {
                return string.Join(" -> ", chain.Append(Path.GetFileName(reference)));
            }
            chain.Add(Relative(root, reference));
            string? found = FindAetheris(reference, visited, chain, root);
            chain.RemoveAt(chain.Count - 1);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static IEnumerable<string> References(string project)
    {
        string directory = Path.GetDirectoryName(project)!;
        foreach (Match match in Reference.Matches(File.ReadAllText(project)))
        {
            string include = match.Groups[1].Value.Replace('\\', '/');
            yield return Path.GetFullPath(Path.Combine(directory, include));
        }
    }

    private static bool IsAetheris(string fullPath) =>
        Path.GetFileName(fullPath).StartsWith("Aetheris.", StringComparison.Ordinal);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
