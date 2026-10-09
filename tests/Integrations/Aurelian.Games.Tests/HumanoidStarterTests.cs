using System.Numerics;
using System.Collections.Immutable;
using System.Text.Json;
using Aetheris.Humanoid;
using Aurelian.Games;
using Aurelian.Humanoid;
using Aurelian.Runtime;
using Aurelian.World.Scenes;
using Aurelian.World.Agents;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class HumanoidStarterTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);
    private static GameConcept[] Concepts => [.. GamePresets.FirstPersonShooter,
        GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl, GameConcept.HumanoidPresentation];

    [Fact]
    public void AuthoredHeadingAndHumanControlSurviveTheFirstMovementTick()
    {
        var document = StarterScenes.TrainingRange([]);
        document = document with
        {
            Children = document.Children.Select(node => node.Id == "player" ? node with
            {
                Transform = node.Transform with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f) },
            } : node).ToImmutableArray(),
        };
        using var game = Create(document: document);
        game.Activate("start");
        var player = Assert.IsType<SceneAgent<HumanoidCharacterState>>(game.HumanoidPlayer);
        Assert.Equal(AgentControl.Human, player.Definition.Template.Control);
        Assert.InRange(MathF.Abs(game.Observe().Yaw + .7f), 0, .00001f);
        game.Advance(Tick);
        Vector3 forward = Vector3.TransformNormal(-Vector3.UnitZ, player.WorldTransform);
        Assert.InRange(Vector3.Distance(forward, Camera3D.Direction(game.Observe().Yaw, 0)), 0, .00001f);
    }

    [Fact]
    public async Task ReplacingClipContentWithTheSameNameChangesSaveCompatibility()
    {
        var original = Profile();
        var pose = original.Animations.Initial.TransitionFrom;
        var changed = pose.SetItem(0, pose[0] with { FlexionDegrees = 10 });
        var replacement = original with
        {
            Animations = new(
                new("idle.v1", 1, true, [new(0, changed), new(1, changed)]),
                new("walk.v1", 1, true, [new(0, pose), new(1, pose)]),
                new("aim.v1", 1, false, [new(0, pose), new(1, pose)])),
        };
        string directory = Path.Combine(Path.GetTempPath(), "humanoid-starter-tests", Guid.NewGuid().ToString("N"));
        using var first = Create(directory, profile: original);
        await first.SaveAsync("old-clips");
        using var next = Create(directory, profile: replacement);
        Assert.NotEqual(first.Identity, next.Identity);
        await Assert.ThrowsAnyAsync<Exception>(() => next.LoadAsync("old-clips"));
        Assert.Equal("Idle", next.Observe().Motion);
    }

    [Fact]
    public void CollisionResolvedMovementDrivesAnimationAndAimingWinsWhileMoving()
    {
        var document = StarterScenes.TrainingRange([]);
        document = document with { Children = document.Children.Add(
            Scene.Box("wall", new(4, 2, 1), Vector4.One, at: new(0, 1, 6), collision: SceneCollision.Solid)) };
        using var game = Create(document: document);
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        game.Advance(Tick);
        game.Advance(Tick);
        Assert.Equal("Walk", game.Observe().Motion);
        for (int frame = 0; frame < 25; frame++)
        {
            game.Advance(Tick);
        }
        Assert.Equal("Idle", game.Observe().Motion);
        Assert.True(game.LastMovement!.Grounded);
        Assert.InRange(game.Capture().Position.Z, 6.8f, 7);
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), false);
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.D), true);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(Tick);
        game.Advance(Tick);
        Assert.Equal("Aim", game.Observe().Motion);
        Assert.NotEmpty(game.PresentationPolicies!.Inspector.Observe().Trace);
    }

    [Fact]
    public void ViewSwitchAndPausePreserveTheSameAgentAndBlend()
    {
        using var game = Create();
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        for (int frame = 0; frame < 5; frame++)
        {
            game.Advance(Tick);
        }
        var character = game.HumanoidPlayer;
        var state = character!.State;
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.V), true);
        game.Advance(TimeSpan.Zero);
        Assert.Equal(CameraView.ThirdPerson, game.View);
        Assert.Same(character, game.HumanoidPlayer);
        Assert.Equal(state, character.State);
        game.Pause();
        string paused = Json(game.Capture());
        game.Advance(TimeSpan.FromSeconds(.1));
        Assert.Equal(paused, Json(game.Capture()));
        var before = character.State;
        _ = game.BuildRenderScene();
        _ = game.CharacterPose;
        Assert.Equal(before, character.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(11)]
    public async Task DeliveranceRestorePreservesPendingSelectionAndEveryFollowingBlend(int cut)
    {
        string directory = Path.Combine(Path.GetTempPath(), "humanoid-starter-tests", Guid.NewGuid().ToString("N"));
        using var first = Create(directory);
        first.Activate("start");
        for (int frame = 0; frame < cut; frame++)
        {
            Drive(first, frame);
        }
        await first.SaveAsync("checkpoint");
        using var restored = Create(directory);
        restored.Activate("start");
        await restored.LoadAsync("checkpoint");
        Assert.Equal(Json(first.Capture()), Json(restored.Capture()));
        for (int frame = cut; frame < cut + 20; frame++)
        {
            Drive(first, frame);
            Drive(restored, frame);
            Assert.Equal(Json(first.Capture()), Json(restored.Capture()));
        }
        var oldPolicies = restored.PresentationPolicies;
        restored.Restore(first.Capture());
        Assert.Equal(0, oldPolicies!.Count);
        restored.Dispose();
        Assert.Equal(0, restored.PresentationPolicies!.Count);
    }

    [Fact]
    public void InvalidAnimationRestoreLeavesTheLiveSceneAndKernelUntouched()
    {
        using var game = Create();
        game.Activate("start");
        Drive(game, 0);
        var saved = game.Capture();
        var agent = game.HumanoidPlayer;
        var corrupt = saved with { Humanoid = saved.Humanoid! with
            { Animation = saved.Humanoid.Animation with { TransitionSeconds = double.NaN } } };
        Assert.Throws<InvalidDataException>(() => game.Restore(corrupt));
        Assert.Same(agent, game.HumanoidPlayer);
        Assert.Equal(Json(saved), Json(game.Capture()));
    }

    [Fact]
    public void InvalidPolicyRestoreLeavesTheLiveSceneAndKernelUntouched()
    {
        using var game = Create();
        game.Activate("start");
        Drive(game, 0);
        var saved = game.Capture();
        var agent = game.HumanoidPlayer;
        var character = saved.Humanoid!;
        var corrupt = saved with
        {
            Humanoid = character with { Policy = character.Policy with { Agents = [] } },
        };
        var error = Assert.Throws<InvalidDataException>(() => game.Restore(corrupt));
        Assert.Contains("live game has not been changed", error.Message);
        Assert.Same(agent, game.HumanoidPlayer);
        Assert.Equal(Json(saved), Json(game.Capture()));
    }

    [Fact]
    public void NamedJointAttachmentFollowsTheSolvedPoseAndActorWorld()
    {
        var profile = Profile();
        var attachment = new HumanoidJointAttachment("tool", HumanoidJointKind.RightWrist,
            Scene.Group("tool", [Scene.Box("shape", new(.1f), Vector4.One)]), SceneTransform.Identity);
        var idle = profile.Body.Solve("idle", profile.Animations.Sample(profile.Animations.Initial)).Pose!;
        var aimState = profile.Animations.Advance(profile.Animations.Initial, CharacterMotion.Aim, .3, 0);
        var aim = profile.Body.Solve("aim", profile.Animations.Sample(aimState)).Pose!;
        var world = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(2, 0, -3);
        var local = attachment.WorldTransform(profile.Body, aim, Matrix4x4.Identity);
        Assert.Equal(local * world, attachment.WorldTransform(profile.Body, aim, world));
        Assert.NotEqual(attachment.WorldTransform(profile.Body, idle, world), local * world);
        var frame = attachment.Project(profile.Body, aim, world);
        Assert.Equal("tool.shape", frame.Boxes.Single().Id);
    }

    private static void Drive(StarterGame game, int frame)
    {
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), frame % 9 < 5);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), frame % 7 >= 3);
        game.Advance(Tick);
    }

    private static HumanoidPlayerOptions Profile()
    {
        var canonical = HumanoidRestPoseNormalizer.Normalize(CanonicalAdultTemplate.Create());
        var body = new HumanoidGameplayBody(new(HumanoidGameplayBody.Schema, "fixture", canonical.Surface,
            canonical.Skeleton, [], canonical.Provenance));
        return new(body, "fixture.v1");
    }

    private static StarterGame Create(string? saves = null, SceneGroup? document = null, HumanoidPlayerOptions? profile = null)
    {
        return GameStarter.Create("humanoid-test", Concepts, saveDirectory: saves,
            sceneDocument: document, humanoid: profile ?? Profile());
    }

    private static string Json(StarterSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, StarterJsonContext.Default.StarterSnapshot);
}
