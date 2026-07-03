using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PlaybackTomlParserM16bTests
{
    [Fact]
    public void PlaybackTomlParser_RejectsConditionals()
    {
        string toml = """
            [scenario]
            id = "conditional"
            name = "Conditional"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"
            if = "x"

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "shell-mode"
            value = "wide"
            reason = "TOML playback must remain linear data."
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));
        Assert.Contains("not a scripting language", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_RejectsLoops()
    {
        string toml = """
            [scenario]
            id = "loop"
            name = "Loop"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "wait"
            ms = 0
            repeat = 5

            [[assertions]]
            type = "shell-mode"
            value = "wide"
            reason = "Playback must reject loop-like fields."
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));
        Assert.Contains("not a scripting language", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_RejectsScriptOrEvalFields()
    {
        string toml = """
            [scenario]
            id = "eval"
            name = "Eval"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [[steps]]
            type = "wait"
            ms = 0
            script = "alert('nope')"

            [[assertions]]
            type = "shell-mode"
            value = "wide"
            reason = "Playback must reject script-like fields."
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));
        Assert.Contains("not a scripting language", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackTomlParser_StillRequiresAssertionReasons()
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
            type = "shell-mode"
            value = "wide"
            """;

        PresenterPlaybackScenarioParseException ex = Assert.Throws<PresenterPlaybackScenarioParseException>(
            () => PresenterPlaybackTomlParser.LoadString(toml));
        Assert.Contains("non-empty reason", ex.Message, StringComparison.Ordinal);
    }
}
