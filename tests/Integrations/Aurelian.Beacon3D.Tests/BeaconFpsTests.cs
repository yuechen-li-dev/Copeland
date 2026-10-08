using System.Numerics;
using Aurelian.World.Agents;
using InputMan.Core;
using Xunit;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconFpsTests
{
    private const float Seconds = 1f / 60;

    [Fact]
    public void MouseEventsAccumulateWithoutClampingAndAreConsumedOnce()
    {
        using var controls = new BeaconControls();
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 200);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 400);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), 100);
        var commands = controls.Tick(Seconds);
        Assert.Equal(1.5f, commands.Movement.MouseYaw, 5);
        Assert.Equal(-0.25f, commands.Movement.MousePitch, 5);
        Assert.Equal(0, controls.Tick(Seconds).Movement.MouseYaw);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 10);
        controls.Adapter.OnFocusChanged(false);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 200);
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        controls.Adapter.OnFocusChanged(true);
        var restored = controls.Tick(Seconds);
        Assert.Equal(0, restored.Movement.MouseYaw);
        Assert.False(restored.Movement.Fire);
    }

    [Fact]
    public void MouseAngleDoesNotDependOnTickDurationAndMenusConsumeMotion()
    {
        var first = new BeaconGame(false);
        var second = new BeaconGame(false);
        first.Step(new(0, 0, 0, 0, MouseYaw: 0.4f, MousePitch: 2), Seconds);
        second.Step(new(0, 0, 0, 0, MouseYaw: 0.4f, MousePitch: 2), 0.03f);
        Assert.Equal(first.Yaw, second.Yaw);
        Assert.Equal(1.3f, first.Pitch);
        using var controls = new BeaconControls();
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 50);
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        var menu = controls.Tick(Seconds, true);
        Assert.Equal(0, menu.Movement.MouseYaw);
        Assert.False(menu.Movement.Fire);
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        Assert.Equal(0, controls.Tick(Seconds).Movement.MouseYaw);
    }

    [Fact]
    public void CreatureBrainSwitchesFromChaseToAttackAndKeepsItsLiveState()
    {
        var brains = new BeaconCreatureBrains();
        brains.Add("stalker");
        brains.Observe("stalker", 10);
        for (int tick = 0; tick < 16; tick++)
        {
            brains.Tick(Seconds);
        }
        Assert.Equal(CreatureIntent.Chase, brains.Intent("stalker"));
        brains.Observe("stalker", 0.5f);
        for (int tick = 0; tick < 16; tick++)
        {
            brains.Tick(Seconds);
        }
        Assert.Equal(CreatureIntent.Attack, brains.Intent("stalker"));
        Assert.Equal(1, brains.Count);
    }

    [Fact]
    public void BoltsStopAtPillarsAndIntersectSweptCreatureGeometry()
    {
        float wall = BeaconGame.WorldHit(new(1, 1, 9), -Vector3.UnitZ, 12);
        Assert.InRange(wall, 3.9f, 4.1f);
        Assert.Equal(float.PositiveInfinity, BeaconGame.RaySphere(new(0, 1, 9), -Vector3.UnitZ, new(5, 1, 0), 0.65f));
        Assert.InRange(BeaconGame.RaySphere(new(0, 1, 9), -Vector3.UnitZ, new(0, 1, 0), 0.65f), 8.34f, 8.36f);
        Assert.Equal(0, BeaconGame.RaySphere(new(0, 1, 0), Vector3.UnitX, new(0, 1, 0), 0.65f));
    }

    [Fact]
    public void WeaponRateMagazineAndReloadAreSimulationOwned()
    {
        var game = new BeaconGame();
        for (int tick = 0; tick < 120; tick++)
        {
            game.Step(new(0, 0, 0, 0, Fire: true), Seconds);
        }
        Assert.Equal(12, game.Shots);
        Assert.Equal(0, game.Ammo);
        game.Step(new(0, 0, 0, 0, Reload: true), Seconds);
        Assert.True(game.Reloading);
        for (int tick = 0; tick < 67; tick++)
        {
            game.Step(default, Seconds);
        }
        Assert.Equal(12, game.Ammo);
        Assert.False(game.Reloading);
    }

    [Fact]
    public void CompleteFightUsesInputManAndNineAuthoredCreatureAgents()
    {
        var game = new BeaconGame();
        using var controls = new BeaconControls();
        for (int tick = 0; tick < 18000 && !game.Dead && game.WavesCleared < 3; tick++)
        {
            BeaconProofDriver.Fight(game, controls);
            game.Step(controls.Tick(Seconds).Movement, Seconds);
        }
        Assert.False(game.Dead);
        Assert.Equal(3, game.WavesCleared);
        Assert.Equal(9, game.Kills);
        Assert.True(game.AgentTicks > 0);
        Assert.Equal(0, game.ActiveCreatureBrains);
        Assert.False(game.GateOpen);
        Assert.Single(game.Agents, agent => agent.Template.Kind == AgentKind.Character);
        Assert.Equal(3, game.Agents.Count(agent => agent.Template.Kind == AgentKind.Object));
    }

    [Fact]
    public void UnarmedRunnerCanDieAndApplicationOffersRetry()
    {
        var app = new BeaconApplication();
        app.Activate("new-game");
        for (int tick = 0; tick < 18000 && app.Screen == BeaconScreen.Playing; tick++)
        {
            app.Update(new(default, false), Seconds);
        }
        Assert.Equal(BeaconScreen.Lost, app.Screen);
        Assert.Equal(0, app.Game.Health);
        app.Activate("restart");
        Assert.Equal(BeaconScreen.Playing, app.Screen);
        Assert.Equal(100, app.Game.Health);
    }

    [Fact]
    public void FiredBoltIsRemovedByPillarCollisionBeforeAnyWaveSpawns()
    {
        var game = new BeaconGame();
        using var controls = new BeaconControls();
        BeaconProofDriver.Aim(game, controls, new Vector3(1, 1, 4));
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Step(controls.Tick(Seconds).Movement, Seconds);
        Assert.Single(game.Bolts);
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        for (int tick = 0; tick < 14; tick++)
        {
            game.Step(controls.Tick(Seconds).Movement, Seconds);
        }
        Assert.Empty(game.Bolts);
        Assert.Equal(1, game.Shots);
        Assert.Equal(0, game.Wave);
    }
}
