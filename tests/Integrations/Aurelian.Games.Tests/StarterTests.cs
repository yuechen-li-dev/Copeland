using System.Text.Json;
using Aurelian.Combat;
using Aurelian.Games;
using Aurelian.Playtesting;
using Aurelian.Runtime;
using Aurelian.Simulation;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class StarterTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);
    private static GameConcept[] BothViews => [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl];

    [Fact]
    public void PresetAndManualFragmentsUseTheSameCanonicalDefinition()
    {
        var preset = new GameDefinition(BothViews);
        var manual = new GameDefinition([GameConcept.ReloadableGuns, GameConcept.ThirdPersonControl,
            GameConcept.FirstPersonCamera, GameConcept.ThreeDimensional, GameConcept.ThirdPersonCamera,
            GameConcept.FirstPersonControl, GameConcept.FirstPersonControl]);
        Assert.Equal(preset.Identity, manual.Identity);
        Assert.Equal(preset.Concepts, manual.Concepts);
    }

    [Fact]
    public void CompositionRejectsMissingDependenciesBeforeCreatingAWindow()
    {
        Assert.Contains("ThirdPersonControl requires ThirdPersonCamera", Assert.Throws<ArgumentException>(() =>
            new GameDefinition([.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonControl])).Message);
        Assert.Throws<ArgumentException>(() => new GameDefinition([(GameConcept)999]));
        Assert.Throws<ArgumentException>(() => new GameDefinition([GameConcept.FirstPersonCamera]));
    }

    [Fact]
    public void MouseDeltasAreConsumedOnceAndViewSwitchPreservesTheActorAndWeapon()
    {
        using var game = Create();
        game.Activate("start");
        game.Controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), 100);
        game.Advance(TimeSpan.Zero);
        Assert.Equal(0.25f, game.Observe().Yaw, 5);
        game.Advance(Tick);
        Assert.Equal(0.25f, game.Observe().Yaw, 5);
        StarterSnapshot before = game.Capture();
        Key(game, KeyboardKey.V, true);
        Assert.Equal(CameraView.ThirdPerson, game.View);
        Assert.Equal(before.Position, game.Capture().Position);
        Assert.Equal(before.Gun, game.Capture().Gun);
        Assert.NotEqual(Camera3D.Eye(before.Position.ToVector(), before.Yaw, before.Pitch, CameraView.FirstPerson),
            Camera3D.Eye(before.Position.ToVector(), before.Yaw, before.Pitch, CameraView.ThirdPerson));
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        game.Advance(Tick);
        Assert.NotEqual(before.Position, game.Capture().Position);
    }

    [Fact]
    public void CameraOnlyCompositionDoesNotMoveTheActorOrShoot()
    {
        using var game = GameStarter.Create("camera-test", [GameConcept.ThreeDimensional, GameConcept.ThirdPersonCamera]);
        game.Activate("start");
        StarterSnapshot before = game.Capture();
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        Assert.Equal(before.Position, game.Capture().Position);
        Assert.Equal(0, game.Observe().Shots);
    }

    [Fact]
    public void MenuConfirmDoesNotLeakIntoJumpAndPauseFreezesGameplay()
    {
        using var game = Create();
        Key(game, KeyboardKey.Space, true);
        Assert.True(game.Playing);
        game.Advance(Tick);
        Assert.Equal(0, game.Capture().Position.Y);
        Key(game, KeyboardKey.Escape, true);
        StarterSnapshot paused = game.Capture();
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        game.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(Json(paused), Json(game.Capture()));
    }

    [Fact]
    public void FocusLossPausesReleasesHeldInputAndCancelsMousePress()
    {
        using var game = Create();
        var target = new StarterPlaytestTarget(game);
        game.Activate("start");
        target.Key(KeyboardKey.W, true);
        target.Focus(false);
        target.Focus(true);
        game.Activate("resume");
        var before = game.Capture().Position;
        target.Frame(Tick);
        Assert.Equal(before, game.Capture().Position);
    }

    [Fact]
    public void ReloadEdgeSurvivesFramesWithoutASimulationTick()
    {
        using var game = Create();
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        Key(game, KeyboardKey.R, true);
        Key(game, KeyboardKey.R, false);
        game.Advance(Tick);
        Assert.True(game.Observe().Reloading);
        for (int frame = 0; frame < 70; frame++) game.Advance(Tick);
        Assert.Equal(12, game.Observe().Ammo);
        Assert.False(game.Observe().Reloading);
    }

    [Fact]
    public void RebindingKeepsTheNativeInputAdapterAndChangesPhysicalBindings()
    {
        using var game = Create();
        var adapter = game.Controls.Adapter;
        game.Rebind(game.Controls.Keys with { Forward = KeyboardKey.I });
        Assert.Same(adapter, game.Controls.Adapter);
        game.Activate("start");
        var before = game.Capture().Position;
        adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        game.Advance(Tick);
        Assert.Equal(before, game.Capture().Position);
        adapter.RecordButton(Controls.Key(KeyboardKey.I), true);
        game.Advance(Tick);
        Assert.NotEqual(before, game.Capture().Position);
    }

    [Fact]
    public async Task DeliveranceSlotRestoresReloadCameraBindingsAudioAndCadenceInAFreshGame()
    {
        string directory = TemporaryDirectory();
        using var first = Create(directory);
        first.Activate("start");
        Key(first, KeyboardKey.V, true);
        first.Rebind(first.Controls.Keys with { Forward = KeyboardKey.I });
        first.SetVolume(0.25f);
        first.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        first.Advance(Tick);
        first.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), false);
        Key(first, KeyboardKey.R, true);
        first.Advance(TimeSpan.FromTicks(123456));
        StarterSnapshot saved = first.Capture();
        await first.SaveAsync("roundtrip");
        using var second = Create(directory);
        await second.LoadAsync("roundtrip");
        Assert.Equal(Json(saved), Json(second.Capture()));
        Assert.Equal(new[] { "roundtrip" }, await second.Saves.ListAsync());
        // State restoration preserves the phase of a partially accumulated simulation frame.
        first.Advance(Tick);
        second.Activate("start");
        second.Restore(saved);
        second.Advance(Tick);
        Assert.Equal(Json(first.Capture()), Json(second.Capture()));
        await second.Saves.DeleteAsync("roundtrip");
        Assert.False(await second.Saves.ExistsAsync("roundtrip"));
    }

    [Fact]
    public async Task IncompatibleCompositionCannotMutateTheLiveGame()
    {
        string directory = TemporaryDirectory();
        using var first = Create(directory);
        await first.SaveAsync("incompatible");
        using var second = GameStarter.Create("starter-test", GamePresets.FirstPersonShooter, saveDirectory: directory);
        StarterSnapshot before = second.Capture();
        await Assert.ThrowsAnyAsync<Exception>(() => second.LoadAsync("incompatible"));
        Assert.Equal(Json(before), Json(second.Capture()));
    }

    [Fact]
    public void RestoringAnEarlierShotDoesNotSuppressTheNextShotSound()
    {
        using var game = Create();
        game.Activate("start");
        StarterSnapshot before = game.Capture();
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        game.Restore(before);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        Assert.Equal(1, game.Observe().Shots);
        Assert.Equal(0, game.Audio.Inspect().DuplicateEvents);
    }

    [Fact]
    public async Task DeliverancePreferencesPersistWithoutSavingOrRestoringTheWorld()
    {
        string directory = TemporaryDirectory();
        using (var first = Create(directory))
        {
            first.Rebind(first.Controls.Keys with { Forward = KeyboardKey.I });
            first.SetVolume(0.25f);
            await first.SaveSettingsAsync();
            Assert.Empty(await first.Saves.ListAsync());
        }
        using var second = Create(directory);
        Assert.Equal(KeyboardKey.I, second.Controls.Keys.Forward);
        Assert.Equal(0.25f, second.Volume);
        Assert.Equal(0, second.Observe().Time);
        Assert.Equal("title", second.Screen);
    }

    [Fact]
    public void InvalidCandidatesAreRejectedAtomically()
    {
        using var game = Create();
        StarterSnapshot before = game.Capture();
        Assert.Throws<InvalidDataException>(() => game.Restore(before with { Gun = before.Gun with { Ammo = 100 } }));
        Assert.Throws<ArgumentException>(() => game.Restore(before with { Keys = before.Keys with { Forward = before.Keys.Backward } }));
        Assert.Throws<InvalidDataException>(() => game.Restore(before with { Cadence = [] }));
        Assert.Equal(Json(before), Json(game.Capture()));
    }

    [Fact]
    public void AuthoredTargetsRespectStableIdentityWhenTheCallerChangesDeclarationOrder()
    {
        using var game = GameStarter.Create("target-test", GamePresets.FirstPersonShooter, new StarterOptions(Objects:
            [new("z", new(0, 1, -3), new(1, 1, 1), 9), new("a", new(4, 1, -3), new(1, 1, 1), 2)]));
        Assert.Equal(new[] { "a", "z" }, game.Agents.Select(agent => agent.Id));
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        Assert.Equal(8, game.Agents.Single(agent => agent.Id == "z").State);
        Assert.Equal(2, game.Agents.Single(agent => agent.Id == "a").State);
    }

    [Fact]
    public void TypedScriptRewindsThroughTheSharedPlaytestingRunner()
    {
        using var game = Create();
        var target = new StarterPlaytestTarget(game);
        using var runner = new PlaytestRunner<StarterObservation>(target, TemporaryDirectory(), StarterJsonContext.Default.StarterObservation);
        runner.Run(new(1,
        [new("click-action", Text: "start"), new("hold", Keys: ["W"], Seconds: 0.2), new("checkpoint", Text: "walked"),
         new("hold", Keys: ["D"], Seconds: 0.2), new("rewind", Text: "walked")]));
        Assert.Equal(0, game.Observe().Position.X);
        Assert.True(game.Observe().Position.Z < 7);
    }

    [Fact]
    public void CadenceRestoreValidatesAllFactsBeforeReplacingAnyPhase()
    {
        var scheduler = new CadenceScheduler([new(new("slow"), RationalRate.PerSecond(2), 0),
            new(new("fast"), RationalRate.PerSecond(60), 1)], TimeSpan.FromSeconds(1));
        scheduler.Advance(Tick, SimulationExecutionRate.Normal);
        CadenceAccumulatorFact[] before = scheduler.InspectAccumulators().ToArray();
        var invalid = before.ToArray();
        invalid[1] = invalid[1] with { ScaledRemainder = -1 };
        Assert.Throws<InvalidDataException>(() => scheduler.RestoreAccumulators(invalid));
        Assert.Equal(before, scheduler.InspectAccumulators());
    }

    private static StarterGame Create(string? directory = null) => GameStarter.Create("starter-test", BothViews, saveDirectory: directory ?? TemporaryDirectory());
    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-starter-tests", Guid.NewGuid().ToString("N"));
    private static string Json(StarterSnapshot state) => JsonSerializer.Serialize(state, StarterJsonContext.Default.StarterSnapshot);
    private static void Key(StarterGame game, KeyboardKey key, bool down)
    {
        game.Controls.Adapter.RecordButton(Controls.Key(key), down);
        game.Advance(TimeSpan.Zero);
    }
}
