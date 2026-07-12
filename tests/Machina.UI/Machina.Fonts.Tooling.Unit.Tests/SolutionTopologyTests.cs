using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class SolutionTopologyTests
{
    [Fact]
    public void FastSolution_DoesNotIncludeSlowToolingProject()
    {
        string repoRoot = ToolingUnitTestEnvironment.FindRepoRoot();
        string fastSolution = File.ReadAllText(Path.Combine(repoRoot, "Machina.UI.slnx"));

        Assert.DoesNotContain("tests/Machina.UI/Machina.Fonts.Tooling.Tests/Machina.Fonts.Tooling.Tests.csproj", fastSolution, StringComparison.Ordinal);
        Assert.Contains("tests/Machina.UI/Machina.Fonts.Tooling.Unit.Tests/Machina.Fonts.Tooling.Unit.Tests.csproj", fastSolution, StringComparison.Ordinal);
    }
}
