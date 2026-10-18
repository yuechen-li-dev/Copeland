using System.Numerics;
using Aurelian.Games;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class PointerInputTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void ClicksBetweenTicksReachTheNextTickInSurfacePixels()
    {
        using var session = Start(out ClickGame game);
        // A HiDPI window: the device reports 960x600 window units over a 1920x1200 surface.
        session.Input.SetPointerSurface(new Vector2(960, 600), new Vector2(1920, 1200));
        session.Input.Adapter.RecordPointerPosition(new Vector2(480, 150));
        session.Input.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        session.Advance(TimeSpan.Zero);
        Assert.Empty(game.Clicks);

        session.Advance(Tick);
        Assert.Equal([new Vector2(960, 300)], game.Clicks);
        Assert.Equal(new Vector2(1920, 1200), game.LastSurface);
        // Released and pressed in the same tick: the click is seen once, not repeated.
        session.Advance(Tick);
        Assert.Single(game.Clicks);
    }

    [Fact]
    public void MouseButtonsAlsoDriveBoundActionsAndTheWheelAccumulates()
    {
        using var session = Start(out ClickGame game);
        session.Input.Adapter.RecordPointerPosition(new Vector2(10, 20));
        session.Input.Adapter.RecordButton(Controls.Mouse(MouseButton.Secondary), true);
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), 1);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), 2);
        session.Advance(Tick);
        Assert.Equal(1, game.Uses);
        Assert.Equal(3, game.Wheel);
        Assert.True(game.SecondaryHeld);
    }

    [Fact]
    public void TypingArrivesInOrderOnlyWhilePlaying()
    {
        using var session = Start(out ClickGame game);
        session.Input.EnterText("ab");
        session.Input.EditText(GameTextEdit.Backspace);
        session.Input.EnterText("c\n");
        session.Advance(Tick);
        Assert.Equal("ac", game.Text);

        Tap(session, KeyboardKey.Escape);
        Assert.Equal("pause", session.Screen);
        session.Input.EnterText("lost");
        session.Advance(Tick);
        session.Activate("resume");
        session.Advance(Tick);
        Assert.Equal("ac", game.Text);
    }

    [Fact]
    public void PointerPositionSurvivesScreenChangesAndIsAbsentWithoutTheLayoutFlag()
    {
        using var session = Start(out ClickGame game);
        session.Input.Adapter.RecordPointerPosition(new Vector2(300, 200));
        session.Advance(Tick);
        Assert.True(game.Present);
        Tap(session, KeyboardKey.Escape);
        session.Activate("resume");
        session.Advance(Tick);
        Assert.True(game.Present);
        Assert.Equal(new Vector2(300, 200), game.LastPosition);

        using var plain = new GameSession(new ClickGame(pointer: false), TemporaryDirectory());
        var other = (ClickGame)plain.Game;
        plain.Activate("start");
        plain.Input.Adapter.RecordPointerPosition(new Vector2(300, 200));
        plain.Advance(Tick);
        Assert.False(other.Present);
        Assert.Contains("capture the mouse", Assert.Throws<ArgumentException>(() => new GameInputLayout([], GameLookMode.CapturedMouse)
        {
            Pointer = true,
        }.Validate()).Message);
    }

    [Fact]
    public void PlaytestPointerStepsUseSurfaceFractionsAndGameActionPoints()
    {
        using var session = Start(out ClickGame game);
        var target = new GamePlaytestTarget(session);
        (double x, double y) = target.ActionPoint("button");
        target.Pointer(x, y, true);
        target.Frame(TimeSpan.Zero);
        target.Pointer(x, y, false);
        target.Frame(Tick);
        // The headless surface is 960x600; the button's centre is a quarter across and half way down.
        Assert.Equal([new Vector2(240, 300)], game.Clicks);
        target.Text("hi");
        target.Scroll(.5, .5, -1);
        target.Frame(Tick);
        Assert.Equal("hi", game.Text);
        Assert.Equal(-1, game.Wheel);
    }

    private static GameSession Start(out ClickGame game)
    {
        var session = new GameSession(new ClickGame(), TemporaryDirectory());
        game = (ClickGame)session.Game;
        session.Activate("start");
        return session;
    }

    private static void Tap(GameSession session, KeyboardKey key)
    {
        session.Input.Adapter.RecordButton(Controls.Key(key), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Key(key), false);
        session.Advance(TimeSpan.Zero);
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-pointer-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A game that records clicks, wheel notches and typing, with right-click bound to an action.</summary>
    private sealed class ClickGame(bool pointer = true) : IGame
    {
        public GameManifest Manifest { get; } = new("click-game", "CLICK", new GameInputLayout(
        [
            new("use", "Use", GameControl.Key(KeyboardKey.E), GameControl.Mouse(MouseButton.Secondary)),
        ])
        {
            Pointer = pointer,
        });

        public List<Vector2> Clicks { get; } = [];
        public Vector2 LastPosition { get; private set; }
        public Vector2 LastSurface { get; private set; }
        public bool Present { get; private set; }
        public bool SecondaryHeld { get; private set; }
        public int Uses { get; private set; }
        public float Wheel { get; private set; }
        public string Text { get; private set; } = "";

        public void NewGame()
        {
        }

        public void FixedUpdate(GameTick tick)
        {
            GamePointer cursor = tick.Input.Pointer;
            Present = cursor.Present;
            LastPosition = cursor.Position;
            LastSurface = cursor.Surface;
            SecondaryHeld = cursor.IsDown(GamePointerButton.Secondary);
            if (cursor.WasReleased(GamePointerButton.Primary))
            {
                Clicks.Add(cursor.Position);
            }
            Wheel += cursor.Wheel.Y;
            if (tick.Input.WasPressed("use"))
            {
                Uses++;
            }
            foreach (GameTyping typing in tick.Input.Typing)
            {
                if (typing.Edit == GameTextEdit.Backspace)
                {
                    Text = Text.Length > 0 ? Text[..^1] : Text;
                }
                else
                {
                    Text += typing.Text;
                }
            }
        }

        public (double X, double Y)? ActionPoint(string action, GameViewRequest surface) => action == "button" ? (.25, .5) : null;

        public void Dispose()
        {
        }
    }
}
