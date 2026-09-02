using Dominatus.Core.Runtime;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class DominatusProvenanceTests
{
    [Fact]
    public void SkyrimPath_LoadsDominatusOnePointZero()
    {
        Version? version = typeof(AiWorld).Assembly.GetName().Version;

        Assert.NotNull(version);
        Assert.Equal(1, version!.Major);
        Assert.Equal(0, version.Minor);
    }

    [Fact]
    public void SkyrimProjects_UseCentralOnePointZeroPackagesWithoutLegacyVersions()
    {
        string root = FindRepositoryRoot();
        string packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        string application = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Skyrim",
            "Aurelian.Marionette",
            "Aurelian.Marionette.csproj"));

        Assert.Contains("Dominatus.Core\" Version=\"1.0.0\"", packages, StringComparison.Ordinal);
        Assert.Contains("Dominatus.OptFlow\" Version=\"1.0.0\"", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("0.4.0", packages, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Dominatus.Core\"", application, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Dominatus.OptFlow\"", application, StringComparison.Ordinal);
        Assert.DoesNotContain("reference/dominatus", application, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AurelianSource_HasNoSkyrimOrMarionetteOwnership()
    {
        string root = FindRepositoryRoot();
        string aurelianRoot = Path.Combine(root, "src", "Aurelian");
        string[] sourceFiles = Directory.GetFiles(aurelianRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.All(sourceFiles, path =>
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("Skyrim", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Marionette", source, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Packages.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src", "Aurelian")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Copeland repository root was not found.");
    }
}
