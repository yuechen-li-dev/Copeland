using Aurelian.Games;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class InputContextTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void AModalContextTakesSharedKeysAndEscapeFromPlayAndThePauseMenu()
    {
        using var session = new GameSession(new TalkGame(), TemporaryDirectory());
        var game = (TalkGame)session.Game;
        session.Activate("start");

        Tap(session, KeyboardKey.Space);
        session.Advance(Tick);
        Assert.Equal(1, game.Jumps);

        Tap(session, KeyboardKey.T);
        session.Advance(Tick);
        Assert.True(game.Talking);

        // Space means "advance" while talking, and must not also jump.
        Tap(session, KeyboardKey.Space);
        session.Advance(Tick);
        Assert.Equal(1, game.Lines);
        Assert.Equal(1, game.Jumps);

        // Escape closes the dialogue instead of pausing the game.
        Tap(session, KeyboardKey.Escape);
        session.Advance(Tick);
        Assert.False(game.Talking);
        Assert.Equal("playing", session.Screen);

        // Back in play, Escape pauses as usual.
        Tap(session, KeyboardKey.Escape);
        Assert.Equal("pause", session.Screen);
    }

    [Fact]
    public void LayoutsRejectUnknownContextsAndEscapeOutsideModalContexts()
    {
        Assert.Contains("unknown input context", Assert.Throws<ArgumentException>(() => new GameSession(new TalkGame(
            new GameInputLayout([new("jump", "Jump", GameControl.Key(KeyboardKey.Space)) { Context = "nowhere" }])),
            TemporaryDirectory())).Message);
        Assert.Contains("Escape", Assert.Throws<ArgumentException>(() => new GameSession(new TalkGame(
            new GameInputLayout([new("close", "Close", GameControl.Key(KeyboardKey.Escape)) { Context = "menu" }])
            {
                Contexts = [new GameInputContext(GameInputContext.Play), new GameInputContext("menu")],
            }), TemporaryDirectory())).Message);
    }

    [Fact]
    public void RebindingConflictsAreResolvedWithinAContextOnly()
    {
        using var session = new GameSession(new TalkGame(), TemporaryDirectory());
        // K is free in play even though nothing else uses it; Space stays the dialogue's advance key.
        session.Input.Rebind("jump", GameControl.Key(KeyboardKey.K));
        Assert.Equal(GameControl.Key(KeyboardKey.K), session.Input.Binding("jump"));
        Assert.Equal(GameControl.Key(KeyboardKey.Space), session.Input.Binding("advance"));
        // Taking T (talk) for jump swaps the two play actions and leaves dialogue untouched.
        session.Input.Rebind("jump", GameControl.Key(KeyboardKey.T));
        Assert.Equal(GameControl.Key(KeyboardKey.K), session.Input.Binding("talk"));
        Assert.Equal(GameControl.Key(KeyboardKey.Space), session.Input.Binding("advance"));
    }

    private static void Tap(GameSession session, KeyboardKey key)
    {
        session.Input.Adapter.RecordButton(Controls.Key(key), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Key(key), false);
        session.Advance(TimeSpan.Zero);
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-context-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A game with free play and a modal dialogue that shares Space and claims Escape.</summary>
    private sealed class TalkGame(GameInputLayout? layout = null) : IGame
    {
        public GameManifest Manifest { get; } = new("talk-game", "TALK", layout ?? new GameInputLayout(
        [
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
            new("talk", "Talk", GameControl.Key(KeyboardKey.T)),
            new("advance", "Next line", GameControl.Key(KeyboardKey.Space)) { Context = "dialogue" },
            new("close", "Close dialogue", GameControl.Key(KeyboardKey.Escape), Rebindable: false) { Context = "dialogue" },
        ])
        {
            Contexts = [new GameInputContext(GameInputContext.Play), new GameInputContext("dialogue", Modal: true)],
        });

        public bool Talking { get; private set; }
        public int Jumps { get; private set; }
        public int Lines { get; private set; }

        public IReadOnlyCollection<string> InputContexts => Talking ? ["dialogue"] : [GameInputContext.Play];

        public void NewGame()
        {
            Talking = false;
            Jumps = 0;
            Lines = 0;
        }

        public void FixedUpdate(GameTick tick)
        {
            if (Talking)
            {
                if (tick.Input.WasPressed("advance"))
                {
                    Lines++;
                }
                if (tick.Input.WasPressed("close"))
                {
                    Talking = false;
                }
                return;
            }
            if (tick.Input.WasPressed("jump"))
            {
                Jumps++;
            }
            if (tick.Input.WasPressed("talk"))
            {
                Talking = true;
            }
        }

        public void Dispose()
        {
        }
    }
}
