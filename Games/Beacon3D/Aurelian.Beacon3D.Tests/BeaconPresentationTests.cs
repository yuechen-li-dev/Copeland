using Aurelian.Audio;
using Aurelian.Games;
using Aurelian.Runtime;
using InputMan.Core;
using Xunit;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconPresentationTests
{
    [Fact]
    public void WallAvoidanceRetractsTheSeparateLayerWithoutPuttingItInTheWorldOrMovingTheAimRay()
    {
        using var fixture = new Fixture();
        fixture.Target.Key(KeyboardKey.D, true);
        fixture.Advance(15);
        fixture.Target.Key(KeyboardKey.D, false);
        fixture.Target.Key(KeyboardKey.W, true);
        fixture.Advance(75);
        fixture.Target.Key(KeyboardKey.W, false);
        Assert.True(fixture.Game.Presentation.Pose.Obstruction > .99f);
        int steps = fixture.Game.Presentation.Motion.Footsteps;
        fixture.Advance(15);
        Assert.Equal(steps, fixture.Game.Presentation.Motion.Footsteps);
        fixture.Target.MouseButton(MouseButton.Secondary, true);
        fixture.Target.MouseButton(MouseButton.Primary, true);
        fixture.Advance(12);
        fixture.Target.MouseButton(MouseButton.Primary, false);
        fixture.Target.Key(KeyboardKey.R, true);
        fixture.Advance(12);
        var hash = fixture.Game.World.SemanticHash();
        var direction = fixture.Game.World.Direction;
        var view = fixture.Game.View(new(960, 600));
        Assert.NotNull(view.Viewmodel);
        Assert.NotEqual(view.Camera, view.Viewmodel.Camera);
        Assert.DoesNotContain(view.Scene.Instances, instance => instance.Identity.StartsWith("view/"));
        Assert.All(view.Viewmodel.Geometry, vertex => Assert.True(vertex.Position.Z >= 5,
            $"Weapon crossed the pillar face: {vertex.Position}"));
        fixture.Game.Presentation.Enabled = false;
        Assert.Equal(1, fixture.Game.Presentation.Pose.Obstruction);
        fixture.Game.View(new(960, 600));
        Assert.Equal(hash, fixture.Game.World.SemanticHash());
        Assert.Equal(direction, fixture.Game.World.Direction);
        fixture.Target.Key(KeyboardKey.S, true);
        fixture.Advance(30);
        Assert.True(fixture.Game.Presentation.Pose.Obstruction < .1f);
        fixture.Game.World.ToggleView();
        Assert.Null(fixture.Game.View(new(960, 600)).Viewmodel);
    }

    private sealed class Fixture : IDisposable
    {
        public BeaconShellGame Game { get; } = new();
        public CapturedOutput Output { get; } = new();
        public GameSession Session { get; }
        public GamePlaytestTarget Target { get; }
        public Fixture()
        {
            Session = new(Game, Path.Combine(AppContext.BaseDirectory, "fps-tests", Guid.NewGuid().ToString("N")), Output);
            Target = new(Session);
            Session.Activate("start");
        }
        public void Advance(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Target.Frame(TimeSpan.FromSeconds(1d / 60));
            }
        }
        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void MotionToggleDoesNotChangeAcceptedCombatAimOrSimulationHash()
    {
        using var moving = new Fixture();
        using var still = new Fixture();
        still.Game.Presentation.Enabled = false;
        foreach (var fixture in new[] { moving, still })
        {
            fixture.Target.Key(KeyboardKey.W, true);
            fixture.Target.MouseButton(MouseButton.Primary, true);
            fixture.Target.MouseDelta(30, -10);
            fixture.Advance(20);
        }
        Assert.Equal(moving.Game.World.SemanticHash(), still.Game.World.SemanticHash());
        Assert.Equal(moving.Game.World.Direction, still.Game.World.Direction);
        Assert.True(moving.Game.Presentation.Motion.Recoil > 0);
        Assert.NotEqual(moving.Game.Presentation.Pose, still.Game.Presentation.Pose);
        Assert.Equal(moving.Game.World.Shots, moving.Game.Presentation.Sounds["shot"]);
        Assert.Equal(moving.Game.Presentation.Sounds, still.Game.Presentation.Sounds);
        var hash = moving.Game.World.SemanticHash();
        var pose = moving.Game.Presentation.Pose;
        moving.Game.View(new(960, 600));
        moving.Game.View(new(960, 600));
        Assert.Equal(hash, moving.Game.World.SemanticHash());
        Assert.Equal(pose, moving.Game.Presentation.Pose);
    }

    [Fact]
    public void CoveredSpawnWetGroundAndPuddleDriveThePlayableFootstepsAndWaterEvents()
    {
        using var fixture = new Fixture();
        fixture.Advance(30);
        Assert.True(fixture.Game.Presentation.AudioState.Shelter > .7f);
        fixture.Target.Key(KeyboardKey.W, true);
        fixture.Advance(60);
        fixture.Target.Key(KeyboardKey.W, false);
        fixture.Target.Key(KeyboardKey.A, true);
        fixture.Advance(72);
        fixture.Target.Key(KeyboardKey.A, false);
        fixture.Advance(18);
        var sounds = fixture.Game.Presentation.Sounds;
        Assert.True(sounds.GetValueOrDefault("dry") > 0);
        Assert.True(sounds.GetValueOrDefault("wet") > 0);
        Assert.True(sounds.GetValueOrDefault("water") > 0);
        Assert.NotEmpty(fixture.Game.Presentation.Impacts);
        Assert.True(fixture.Game.Presentation.AudioState.ExteriorGain > .95f);
        Assert.True(fixture.Game.Presentation.AudioState.Shelter < .01f);
    }

    [Fact]
    public void AimPauseFocusSettingsAndRestartUseTheSharedShellLifecycle()
    {
        using var fixture = new Fixture();
        fixture.Target.MouseButton(MouseButton.Secondary, true);
        fixture.Advance(30);
        fixture.Output.Peak = 0;
        fixture.Advance(6);
        Assert.True(fixture.Output.Peak > .001f);
        Assert.True(fixture.Game.Presentation.Pose.Aim > .99f);
        var pose = fixture.Game.Presentation.Pose;
        var environment = fixture.Game.Presentation.AudioState;
        fixture.Session.Pause();
        fixture.Advance(30);
        Assert.Equal(pose, fixture.Game.Presentation.Pose);
        Assert.Equal(environment, fixture.Game.Presentation.AudioState);
        fixture.Output.Peak = 0;
        fixture.Advance(6);
        Assert.Equal(0, fixture.Output.Peak);
        fixture.Session.ApplySettings(fixture.Session.Settings with
        {
            Audio = fixture.Session.Settings.Audio with { Effects = 0 },
        });
        Assert.All(fixture.Session.Audio.Inspect().ActiveVoices.Where(voice => voice.Bus == AudioBusId.Ambient),
            voice => Assert.Equal(0, voice.EffectiveGain));
        fixture.Session.NewGame();
        Assert.Equal(FirstPersonPose3D.Neutral, fixture.Game.Presentation.Pose);
        Assert.Empty(fixture.Game.Presentation.Sounds);
        Assert.Equal(0, fixture.Game.World.Time);
        Assert.Equal(2, fixture.Session.Audio.Inspect().ActiveVoices.Count(voice => voice.Bus == AudioBusId.Ambient));
        fixture.Output.Peak = 0;
        fixture.Advance(30);
        Assert.Equal(0, fixture.Output.Peak);
        fixture.Session.ApplySettings(fixture.Session.Settings with
        {
            Audio = fixture.Session.Settings.Audio with { Effects = 1 },
        });
        fixture.Advance(6);
        Assert.True(fixture.Output.Peak > .001f);
    }

    private sealed class CapturedOutput : IAudioOutputBackend
    {
        public string Name => "Beacon captured PCM";
        public bool IsAvailable => true;
        public long SubmittedFrames { get; private set; }
        public float Peak { get; set; }

        public void Submit(ReadOnlySpan<float> samples, int sampleRateHz)
        {
            SubmittedFrames += samples.Length / 2;
            foreach (float sample in samples)
            {
                Peak = Math.Max(Peak, Math.Abs(sample));
            }
        }

        public void Dispose() { }
    }
}
