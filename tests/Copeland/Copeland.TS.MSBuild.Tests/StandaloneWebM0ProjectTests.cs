using Xunit;

namespace Copeland.TS.MSBuild.Tests;

public sealed class StandaloneWebM0ProjectTests
{
    [Fact]
    public void Standalone_web_project_stages_generated_assets_and_exposes_the_host_contract()
    {
        string repositoryRoot = FindRepositoryRoot();
        string fixtureRoot = Path.Combine(repositoryRoot, "samples", "copeland-ts", "standalone-web-m0");
        string project = File.ReadAllText(Path.Combine(fixtureRoot, "StandaloneWebM0.csproj"));
        string host = File.ReadAllText(Path.Combine(fixtureRoot, "Program.cs"));
        string manifest = File.ReadAllText(Path.Combine(fixtureRoot, "frontend", "manifest.tsx"));

        Assert.Contains("Microsoft.NET.Sdk.Web", project, StringComparison.Ordinal);
        Assert.Contains("CopelandBuildStandaloneFrontend", project, StringComparison.Ordinal);
        Assert.Contains("CopelandCopyStandaloneAssetsToBuildOutput", project, StringComparison.Ordinal);
        Assert.Contains("CopelandCopyStandaloneAssetsToPublish", project, StringComparison.Ordinal);
        Assert.Contains("TSPackExecutable", project, StringComparison.Ordinal);
        Assert.Contains("--no-browser", host, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", host, StringComparison.Ordinal);
        Assert.Contains("COPELAND_STANDALONE_READY", host, StringComparison.Ordinal);
        Assert.Contains("COPE-HOST-0001", host, StringComparison.Ordinal);
        Assert.Contains("@copeland/browser-v1", manifest, StringComparison.Ordinal);
        Assert.Contains("react-dom/client", manifest, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Could not locate the Copeland repository root.");
    }
}
