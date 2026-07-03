using Xunit;

namespace Machina.Presenter.Sample.Tests;

[Collection(PlaybackXunitCollection.Name)]
public sealed class MachinaPlaybackRegressionSuiteTests
{
    [Theory]
    [MemberData(nameof(PlaybackScenarioDiscovery.RegressionScenarioMemberData), MemberType = typeof(PlaybackScenarioDiscovery))]
    public void PlaybackXunit_RegressionScenarios_Pass(PlaybackScenarioFile scenarioFile)
    {
        PlaybackScenarioXunitRunner.AssertScenarioPasses(
            scenarioFile,
            $"{nameof(MachinaPlaybackRegressionSuiteTests)}.{nameof(PlaybackXunit_RegressionScenarios_Pass)}");
    }
}
