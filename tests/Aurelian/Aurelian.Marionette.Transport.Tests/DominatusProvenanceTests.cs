using Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Marionette.Transport.Tests;

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
        string transport = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Aurelian",
            "Aurelian.Marionette.Transport",
            "Aurelian.Marionette.Transport.csproj"));

        Assert.Contains("Dominatus.Core\" Version=\"1.0.0\"", packages, StringComparison.Ordinal);
        Assert.Contains("Dominatus.OptFlow\" Version=\"1.0.0\"", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("0.4.0", packages, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Dominatus.Core\"", transport, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Dominatus.OptFlow\"", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("reference/dominatus", transport, StringComparison.OrdinalIgnoreCase);
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
