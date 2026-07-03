using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PlaybackTomlParserM16aTests
{
    [Fact]
    public void PlaybackTomlParser_LoadsScenarioMetadata()
    {
        PresenterPlaybackScenario scenario = PlaybackTestEnvironment.LoadScenario("oblivion-expand-collapse.machina-playback.toml");

        Assert.Equal("oblivion-expand-collapse", scenario.Id);
        Assert.Equal("oblivion", scenario.Section);
        Assert.Equal("execution-roadmap", scenario.Tab);
        Assert.Equal(1280, scenario.Viewport.Width);
        Assert.Equal(720, scenario.Viewport.Height);
    }

    [Fact]
    public void PlaybackTomlParser_LoadsSteps()
    {
        PresenterPlaybackScenario scenario = PlaybackTestEnvironment.LoadScenario("oblivion-expanded-body-scroll.machina-playback.toml");

        Assert.Equal(2, scenario.Steps.Count);
        Assert.IsType<PresenterPlaybackClickStep>(scenario.Steps[0]);
        Assert.IsType<PresenterPlaybackWheelStep>(scenario.Steps[1]);
    }

    [Fact]
    public void PlaybackTomlParser_LoadsAssertions()
    {
        PresenterPlaybackScenario scenario = PlaybackTestEnvironment.LoadScenario("oblivion-main-stack-scroll.machina-playback.toml");

        Assert.Equal(2, scenario.Assertions.Count);
        Assert.IsType<PresenterPlaybackScrollOffsetGreaterThanAssertion>(scenario.Assertions[0]);
        Assert.All(scenario.Assertions, assertion => Assert.False(string.IsNullOrWhiteSpace(assertion.Reason)));
    }

    [Fact]
    public void PlaybackTomlParser_RejectsAssertionWithoutReason()
    {
        string toml = """
            [scenario]
            id = "missing-reason"
            name = "Missing reason"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "scroll-offset-changed"
            target = "main-stack"
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));

        Assert.Contains("non-empty reason", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_RejectsEmptyAssertionReason()
    {
        string toml = """
            [scenario]
            id = "empty-reason"
            name = "Empty reason"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "selected-card"
            value = "doc-machina-oblivion-phase-closeout-m11g"
            reason = "   "
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));

        Assert.Contains("non-empty reason", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_ReportsUnknownStepType()
    {
        string toml = """
            [scenario]
            id = "unknown-step"
            name = "Unknown step"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "teleport"

            [[assertions]]
            type = "shell-mode"
            value = "wide"
            reason = "The parser should reject unsupported step kinds instead of silently guessing."
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));

        Assert.Contains("Unknown playback step type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_ReportsUnknownAssertionType()
    {
        string toml = """
            [scenario]
            id = "unknown-assertion"
            name = "Unknown assertion"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "pixel-golden"
            reason = "The parser should reject unsupported assertion kinds rather than pretending pixel diffing exists."
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));

        Assert.Contains("Unknown playback assertion type", ex.Message, StringComparison.Ordinal);
    }
}
