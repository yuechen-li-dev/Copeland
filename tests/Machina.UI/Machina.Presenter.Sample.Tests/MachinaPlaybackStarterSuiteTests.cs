using Xunit;

namespace Machina.Presenter.Sample.Tests;

[Collection(PlaybackXunitCollection.Name)]
public sealed class MachinaPlaybackStarterSuiteTests
{
    [Theory]
    [MemberData(nameof(PlaybackScenarioDiscovery.StarterScenarioMemberData), MemberType = typeof(PlaybackScenarioDiscovery))]
    public void PlaybackXunit_StarterScenarios_Pass(PlaybackScenarioFile scenarioFile)
    {
        PlaybackScenarioXunitRunner.AssertScenarioPasses(
            scenarioFile,
            $"{nameof(MachinaPlaybackStarterSuiteTests)}.{nameof(PlaybackXunit_StarterScenarios_Pass)}");
    }
}
