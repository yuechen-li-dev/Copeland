using InputMan.Core;
using Xunit;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconApplicationTests
{
    [Fact]
    public void PauseAndControlsFreezeGameAndReturnToOrigin()
    {
        var app = new BeaconApplication();
        Assert.Equal(BeaconScreen.Title, app.Screen);
        app.Activate("new-game");
        app.Update(new(new(1, 0, 0, 0), false), 1f / 60);
        app.Update(new(default, true), 0.1f);
        var position = app.Game.Position;
        float time = app.Game.Time;
        app.Update(new(new(1, 0, 0, 0, Reload: true), false), 1);
        Assert.Equal(position, app.Game.Position);
        Assert.Equal(time, app.Game.Time);
        app.Activate("controls");
        app.Activate("movement");
        Assert.Equal(BeaconScreen.Controls, app.Screen);
        app.Activate("back");
        Assert.Equal(BeaconScreen.Paused, app.Screen);
        app.Activate("resume");
        Assert.Equal(BeaconScreen.Playing, app.Screen);
        Assert.Equal(time, app.Game.Time);
    }

    [Fact]
    public void RestartCreatesFreshGameAndTitleControlsReturnToTitle()
    {
        var app = new BeaconApplication();
        app.Activate("controls");
        app.Activate("back");
        Assert.Equal(BeaconScreen.Title, app.Screen);
        app.Activate("new-game");
        var old = app.Game;
        app.Update(new(default, true), 0.1f);
        app.Activate("restart");
        Assert.NotSame(old, app.Game);
        Assert.Equal(0, app.Game.Time);
        Assert.Equal(BeaconScreen.Playing, app.Screen);
    }

    [Fact]
    public void InputManMenuContextSuppressesGameActionsAndHeldConfirmDoesNotJump()
    {
        using var controls = new BeaconControls();
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.Space), true);
        var menu = controls.Tick(1f / 60, menuActive: true);
        Assert.True(menu.Menu.Confirm);
        Assert.Equal(0, menu.Movement.Forward);
        Assert.False(menu.Movement.Jump);
        Assert.False(menu.Movement.Reload);
        Assert.False(controls.Tick(1f / 60, menuActive: true).Menu.Confirm);
        Assert.False(controls.Tick(1f / 60).Movement.Jump);
    }
}
