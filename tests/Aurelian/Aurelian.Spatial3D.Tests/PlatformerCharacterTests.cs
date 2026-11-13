using System.Numerics;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

public sealed class PlatformerCharacterTests
{
    private const float Tick = 1f / 60;
    private static SpatialWorld3D Floor(float halfSize = 10) => new([
        new("floor", CollisionMesh3D.Box(new(halfSize, .5f, halfSize)), Matrix4x4.CreateTranslation(0, -.5f, 0)),
    ]);

    [Fact]
    public void LateLedgeJumpIsForgivenOnceAndExpiresOutsideTheGraceWindow()
    {
        bool TryLateJump(int delay)
        {
            var character = new PlatformerCharacter3D();
            var world = Floor(2);
            character.Place(Vector3.Zero, 0);
            for (int frame = 0; frame < 90; frame++)
            {
                character.Step(world, new(Vector2.UnitX, false, true), Tick);
                if (!character.Grounded && character.Feet.X > 2)
                {
                    for (int wait = 0; wait < delay; wait++)
                    {
                        character.Step(world, new(Vector2.UnitX, false, true), Tick);
                    }
                    var jumped = character.Step(world, new(Vector2.UnitX, true, true), Tick);
                    Assert.False(character.Step(world, new(Vector2.Zero, true, true), Tick).Jumped);
                    return jumped.Jumped;
                }
            }
            throw new InvalidOperationException("Fixture never left the platform.");
        }
        Assert.True(TryLateJump(2));
        Assert.False(TryLateJump(12));
    }

    [Fact]
    public void BufferedLandingAndShortHopsSurviveExactMidairRestore()
    {
        var world = Floor();
        var character = new PlatformerCharacter3D();
        character.Place(new(0, .3f, 0), 0);
        character.Restore(character.Capture() with { VerticalVelocity = -5 });
        Assert.False(character.Step(world, new(Vector2.Zero, true, true), Tick).Jumped);
        var replay = new PlatformerCharacter3D();
        replay.Restore(character.Capture());
        int jumps = 0;
        float apex = 0;
        for (int frame = 0; frame < 90; frame++)
        {
            var input = new PlatformerInput3D(Vector2.Zero, false, frame < 8);
            var step = character.Step(world, input, Tick);
            Assert.Equal(step, replay.Step(world, input, Tick));
            Assert.Equal(character.Capture(), replay.Capture());
            jumps += step.Jumped ? 1 : 0;
            apex = Math.Max(apex, character.Feet.Y);
        }
        Assert.Equal(1, jumps);
        Assert.InRange(apex, .3f, 1.3f);
    }

    [Fact]
    public void PlatformJumpInheritsMomentumAndZeroGraceStillAllowsAnImmediateJump()
    {
        var world = new KinematicSpatialWorld3D(new SpatialWorld3D([]),
            [new("lift", CollisionMesh3D.Box(new(2, .2f, 2)))],
            new Dictionary<string, Matrix4x4> { ["lift"] = Matrix4x4.CreateTranslation(0, .2f, 0) });
        var character = new PlatformerCharacter3D(new() { CoyoteTime = 0, JumpBuffer = 0 });
        character.Place(new(0, .4f, 0), 0);
        character.Step(world, new(Vector2.Zero, false, false), Tick);
        Assert.Equal("lift", character.Support);
        world.BeginStep();
        world.SetTransform("lift", Matrix4x4.CreateTranslation(.1f, .24f, 0));
        Assert.True(character.Step(world, new(Vector2.Zero, true, true), .02f).Jumped);
        Assert.Equal(5, character.Velocity.X, 4);
        Assert.True(character.VerticalVelocity > character.Tuning.JumpSpeed);
        Assert.True(character.Feet.X > .1f);
    }

    [Fact]
    public void InvalidInputsAndSnapshotsAreRejectedBeforeChangingLiveState()
    {
        var character = new PlatformerCharacter3D();
        character.Place(Vector3.Zero, 0);
        var before = character.Capture();
        Assert.Throws<ArgumentOutOfRangeException>(() => character.Step(Floor(), new(new(float.NaN, 0), true, true), Tick));
        Assert.Throws<ArgumentOutOfRangeException>(() => character.Step(Floor(), default, .1f));
        Assert.Throws<InvalidDataException>(() => character.Restore(before with { Buffer = float.NaN }));
        Assert.Equal(before, character.Capture());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformerCharacter3D(new() { Gravity = -1 }));
    }
}
