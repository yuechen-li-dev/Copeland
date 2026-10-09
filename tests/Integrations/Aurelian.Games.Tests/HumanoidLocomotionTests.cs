using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aetheris.Humanoid;
using Aurelian.Humanoid;
using Aurelian.World.Scenes;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class HumanoidLocomotionTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void RootTravelCrossesLoopsAndCollisionBlocksTheActor()
    {
        var profile = Profile();
        var bank = profile.Locomotion!;
        var initial = bank.Initial with { Gait = HumanoidGait.Walk, Seconds = .99 };
        var request = bank.Request(initial, new(0, 0, -1.6f), .02f);
        Assert.InRange(MathF.Abs(request.Displacement.Z + .032f), 0, .000001f);
        using var game = Create(profile, wall: true);
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        for (int frame = 0; frame < 100; frame++)
        {
            game.Advance(Tick);
        }
        var state = game.HumanoidPlayer!.State.Animation.Locomotion!;
        Assert.Equal(HumanoidGait.Idle, state.Gait);
        Assert.True(state.RequestedDisplacement.Length() > .001f);
        Assert.InRange(state.AcceptedDisplacement.Length(), 0, .00001f);
        Assert.InRange(game.HumanoidPlayer.State.Position.Z, -.41f, -.39f);
    }

    [Fact]
    public void FootLocksFollowStaticSlopeAndReleaseForJump()
    {
        var profile = Profile();
        using var game = Create(profile, slope: true);
        game.Activate("start");
        for (int frame = 0; frame < 40; frame++)
        {
            game.Advance(Tick);
        }
        var state = game.HumanoidPlayer!.State.Animation.Locomotion!;
        Assert.True(state.Left.Locked);
        Assert.True(state.Right.Locked);
        Assert.InRange(state.Left.ResidualMm, 0, 1);
        Assert.InRange(state.Right.ResidualMm, 0, 1);
        Assert.True(state.Left.NormalWorld.X < -.05f);
        string before = Json(game);
        _ = game.CharacterPose;
        _ = game.BuildRenderScene();
        Assert.Equal(before, Json(game));
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.Space), true);
        game.Advance(Tick);
        state = game.HumanoidPlayer.State.Animation.Locomotion!;
        Assert.False(state.Grounded);
        Assert.False(state.Left.Locked);
        Assert.False(state.Right.Locked);
        Assert.Equal(0, state.Left.Weight);
        Assert.Equal(0, state.Right.Weight);
    }

    [Fact]
    public async Task DeliveranceRestoresFootLocksGaitAndInterruptedBlends()
    {
        var profile = Profile();
        string directory = Path.Combine(Path.GetTempPath(), "locomotion-save", Guid.NewGuid().ToString("N"));
        using var original = Create(profile, saves: directory);
        original.Activate("start");
        for (int frame = 0; frame < 17; frame++)
        {
            Drive(original, frame);
        }
        await original.SaveAsync("stride");
        using var restored = Create(profile, saves: directory);
        restored.Activate("start");
        await restored.LoadAsync("stride");
        Assert.Equal(Json(original), Json(restored));
        for (int frame = 17; frame < 60; frame++)
        {
            Drive(original, frame);
            Drive(restored, frame);
            Assert.Equal(Json(original), Json(restored));
        }
    }

    private static void Drive(StarterGame game, int frame)
    {
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), frame < 35);
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.LeftShift), frame >= 15);
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), frame >= 10 && frame < 25);
        game.Advance(Tick);
    }

    [Fact]
    public void MismatchedSavedBlendChannelsLeaveTheLiveActorUntouched()
    {
        using var game = Create(Profile());
        game.Activate("start");
        Drive(game, 1);
        var before = game.Capture();
        var character = before.Humanoid!;
        var animation = character.Animation;
        var corrupt = before with
        {
            Humanoid = character with
            {
                Animation = animation with { Locomotion = animation.Locomotion! with { BlendFrom = [] } },
            },
        };
        string original = Json(game);
        Assert.Throws<InvalidDataException>(() => game.Restore(corrupt));
        Assert.Equal(original, Json(game));
    }

    private static StarterGame Create(HumanoidPlayerOptions profile, bool wall = false, bool slope = false, string? saves = null)
    {
        var children = new List<SceneNode>
        {
            Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, slope ? .05f : 0, 0)),
            Scene.Box("floor", new(20, .2f, 20), Vector4.One, at: new(0, -.1f, 0), collision: SceneCollision.Solid) with
            {
                Transform = new SceneTransform(new(0, -.1f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, slope ? .12f : 0), Vector3.One),
            },
        };
        if (wall)
        {
            children.Add(Scene.Box("wall", new(2, 2, .2f), Vector4.One, at: new(0, 1, -.8f), collision: SceneCollision.Solid));
        }
        return GameStarter.Create("locomotion-unit", [.. GamePresets.FirstPersonShooter, GameConcept.HumanoidPresentation],
            saveDirectory: saves, sceneDocument: Scene.World("test", children), humanoid: profile);
    }

    private static HumanoidPlayerOptions Profile()
    {
        var normalized = HumanoidRestPoseNormalizer.Normalize(CanonicalAdultTemplate.Create());
        var body = new HumanoidGameplayBody(new(HumanoidGameplayBody.Schema, "locomotion-fixture",
            normalized.Surface, normalized.Skeleton, [], normalized.Provenance));
        var idle = body.Skeleton.Joints.Select(joint => new JointPose(joint.Kind, Quaternion.Identity)).ToImmutableArray();
        var key = body.Solve("stride", [new(HumanoidJointKind.LeftHip, 20), new(HumanoidJointKind.LeftKnee, 30)]).Pose!;
        var stride = idle.Select(joint => key.PoseState.LocalRotations.FirstOrDefault(item => item.Joint == joint.Joint) ?? joint).ToImmutableArray();
        var clips = new[] { HumanoidGait.Idle, HumanoidGait.Walk, HumanoidGait.Run }.Select(gait =>
        {
            float travel = 0;
            if (gait == HumanoidGait.Walk) travel = 1.6f;
            else if (gait == HumanoidGait.Run) travel = 4;
            return new HumanoidLocomotionClip(gait, "fixture." + gait, 1,
                [new(0, idle, Vector3.Zero, 0, true, true), new(.5, stride, Vector3.Zero, travel / 2, true, true),
                    new(1, idle, Vector3.Zero, travel, true, true)], "synthetic", 1, 0, travel);
        }).ToImmutableArray();
        var bank = new HumanoidLocomotionBank(body,
            new("aurelian.humanoid.locomotion.v1", "fixture.v1", body.Skeleton.SkeletonId, body.Skeleton.RestPoseId, clips), "fixture-motion.v1");
        return new(body, "fixture.v1") { Locomotion = bank };
    }

    private static string Json(StarterGame game) => JsonSerializer.Serialize(game.Capture(), StarterJsonContext.Default.StarterSnapshot);
}
