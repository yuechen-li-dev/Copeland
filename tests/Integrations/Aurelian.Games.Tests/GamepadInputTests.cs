using System.Numerics;
using Aurelian.Games;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class GamepadInputTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void ButtonsDriveActionsAndSticksArriveWithYUpAndADeadZone()
    {
        using GameSession session = Start(out PadGame game);
        session.Input.Adapter.ConnectGamepad(0);
        Press(session, GamepadButton.South);
        Assert.Equal(1, game.Jumps);

        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftX), .1f);
        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), -.1f);
        session.Advance(Tick);
        Assert.Equal(Vector2.Zero, game.Stick);

        // Pads report Y down; pushing up gives a positive Y.
        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftX), 0);
        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), -1);
        session.Advance(Tick);
        Assert.Equal(1, game.Stick.Y, 3);
        Assert.Equal(0, game.Stick.X, 3);
    }

    [Fact]
    public void StartPausesAndTheMenusNavigateWithTheDpad()
    {
        using GameSession session = Start(out _);
        session.Input.Adapter.ConnectGamepad(0);
        Press(session, GamepadButton.Start);
        Assert.Equal("pause", session.Screen);
        string? first = session.Observe().Values["menu.selected"];
        Press(session, GamepadButton.DpadDown);
        Assert.NotEqual(first, session.Observe().Values["menu.selected"]);
        Press(session, GamepadButton.East);
        Assert.Equal("playing", session.Screen);
    }

    [Fact]
    public void AGamepadButtonMayOnlyMeanOneThingPerContext()
    {
        var layout = new GameInputLayout(
        [
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)) { Gamepad = GameControl.Gamepad(GamepadButton.South) },
            new("use", "Use", GameControl.Key(KeyboardKey.E)) { Gamepad = GameControl.Gamepad(GamepadButton.South) },
        ]);
        Assert.Contains("more than one", Assert.Throws<ArgumentException>(layout.Validate).Message);
    }

    private static void Press(GameSession session, GamepadButton button)
    {
        session.Input.Adapter.RecordButton(Controls.Gamepad(button), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Gamepad(button), false);
        session.Advance(Tick);
        session.Advance(Tick);
    }

    private static GameSession Start(out PadGame game)
    {
        var session = new GameSession(new PadGame(),
            Path.Combine(Path.GetTempPath(), "aurelian-pad-tests", Guid.NewGuid().ToString("N")));
        game = (PadGame)session.Game;
        session.Activate("start");
        return session;
    }

    private sealed class PadGame : IGame
    {
        public GameManifest Manifest { get; } = new("pad-game", "PAD", new GameInputLayout(
        [
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)) { Gamepad = GameControl.Gamepad(GamepadButton.South) },
        ]));

        public int Jumps { get; private set; }
        public Vector2 Stick { get; private set; }

        public void NewGame()
        {
        }

        public void FixedUpdate(GameTick tick)
        {
            if (tick.Input.WasPressed("jump"))
            {
                Jumps++;
            }
            Stick = tick.Input.LeftStick;
        }

        public void Dispose()
        {
        }
    }
}
