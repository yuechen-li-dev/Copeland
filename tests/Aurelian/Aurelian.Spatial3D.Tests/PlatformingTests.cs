using System.Numerics;
using Aurelian.Spatial3D;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

public sealed class PlatformingTests
{
    private const float Tick = 1f / 60;

    private static Collider3D Box(string id, Vector3 center, Vector3 half)
        => new(id, CollisionMesh3D.Box(half), Matrix4x4.CreateTranslation(center), SemanticOwnerId: id);

    private static SpatialWorld3D Floor(params Collider3D[] extra)
        => new([Box("floor", new(0, -0.5f, 0), new(20, 0.5f, 20)), .. extra]);

    [Theory]
    [InlineData(0.15f, 2f)]
    [InlineData(0.15f, 6f)]
    [InlineData(0.32f, 1f)]
    [InlineData(0.32f, 7f)]
    public void StepHeightClimbsLedgesAtAnySpeed(float ledge, float speed)
    {
        SpatialWorld3D world = Floor(Box("ledge", new(2.5f, ledge / 2, 0), new(1, ledge / 2, 3)));
        var motor = new CharacterMotor3D(new(StepHeight: 0.35f));
        CharacterState3D state = new(Vector3.Zero, 0);
        CharacterMove3D move = motor.Step(world, state, Vector3.Zero, false, Tick);
        float stepped = 0;
        for (int tick = 0; tick < 240 && state.Feet.X < 2.5f; tick++)
        {
            move = motor.Step(world, state, new(speed, 0, 0), false, Tick);
            state = move.State;
            stepped = MathF.Max(stepped, move.SteppedUp);
            Assert.False(move.Embedded);
        }
        Assert.True(state.Feet.X >= 2.5f, $"stalled at x={state.Feet.X}");
        Assert.True(move.Grounded);
        Assert.InRange(state.Feet.Y, ledge - 0.001f, ledge + 0.001f);
        Assert.True(stepped > 0);
    }

    [Fact]
    public void StepsAboveTheLimitStillBlockAndDefaultMotorNeverSteps()
    {
        SpatialWorld3D world = Floor(Box("wall", new(2.5f, 0.25f, 0), new(1, 0.25f, 3)));
        foreach (var options in new[] { new CharacterMotorOptions3D(StepHeight: 0.35f), new CharacterMotorOptions3D() })
        {
            var motor = new CharacterMotor3D(options);
            CharacterState3D state = new(Vector3.Zero, 0);
            for (int tick = 0; tick < 120; tick++)
            {
                state = motor.Step(world, state, new(4, 0, 0), false, Tick).State;
            }
            Assert.InRange(state.Feet.X, 1.1f, 1.21f);
            Assert.InRange(state.Feet.Y, -0.001f, 0.001f);
        }
        var low = Floor(Box("kerb", new(2.5f, 0.1f, 0), new(1, 0.1f, 3)));
        var plain = new CharacterMotor3D();
        CharacterState3D walker = new(Vector3.Zero, 0);
        for (int tick = 0; tick < 120; tick++)
        {
            walker = plain.Step(low, walker, new(4, 0, 0), false, Tick).State;
        }
        Assert.True(walker.Feet.X < 1.5f, "existing motors keep their no-step behaviour");
    }

    [Fact]
    public void StairsAreClimbedAndCeilingsPreventStepping()
    {
        var stairs = Enumerable.Range(0, 6).Select(index =>
            Box($"stair-{index}", new(2 + index * 0.4f, 0.1f + index * 0.1f, 0), new(0.2f, 0.1f + index * 0.1f, 2))).ToArray();
        // Six 0.2 m risers, 0.4 m treads, then a landing at 1.2 m.
        SpatialWorld3D world = Floor([.. stairs, Box("landing", new(6.2f, 0.6f, 0), new(2, 0.6f, 2))]);
        var motor = new CharacterMotor3D(new(StepHeight: 0.3f));
        CharacterState3D state = new(Vector3.Zero, 0);
        for (int tick = 0; tick < 240 && state.Feet.X < 5; tick++)
        {
            state = motor.Step(world, state, new(3, 0, 0), false, Tick).State;
        }
        Assert.True(state.Feet.X >= 5, $"stalled at x={state.Feet.X}, y={state.Feet.Y}");
        Assert.InRange(state.Feet.Y, 1.199f, 1.201f);

        SpatialWorld3D low = Floor(Box("ledge", new(2.5f, 0.1f, 0), new(1, 0.1f, 3)), Box("roof", new(0, 2.05f, 0), new(10, 0.1f, 10)));
        CharacterState3D crouched = new(Vector3.Zero, 0);
        for (int tick = 0; tick < 120; tick++)
        {
            crouched = motor.Step(low, crouched, new(3, 0, 0), false, Tick).State;
        }
        Assert.True(crouched.Feet.X < 1.3f, $"no headroom above the ledge means no step: {crouched.Feet}");
    }

    private static KinematicSpatialWorld3D Platforms(Vector3 at)
    {
        var statics = new SpatialWorld3D([Box("ground", new(0, -10, 0), new(40, 0.5f, 40))]);
        var deck = new KinematicBody3D("deck", CollisionMesh3D.Box(new(1.5f, 0.2f, 1.5f)));
        return new KinematicSpatialWorld3D(statics, [deck], new Dictionary<string, Matrix4x4> { ["deck"] = Matrix4x4.CreateTranslation(at) });
    }

    private static CharacterMove3D Ride(CharacterMotor3D motor, KinematicSpatialWorld3D world, ref CharacterState3D state,
        ref string? support, Vector3 velocity, bool jump = false)
    {
        KinematicCarry3D carry = world.Carry(Capsule3D.AtFeet(state.Feet), support);
        CharacterMove3D move = motor.Step(world, state, velocity, jump, Tick, carry: carry.Displacement, carryWorld: world.Statics);
        state = move.State;
        support = move.GroundColliderId;
        return move;
    }

    [Fact]
    public void MovingAndRotatingPlatformsCarryTheCharacterExactly()
    {
        KinematicSpatialWorld3D world = Platforms(Vector3.Zero);
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(new(0.5f, 0.2f, 0), 0);
        string? support = null;
        Ride(motor, world, ref state, ref support, Vector3.Zero);
        Assert.Equal("deck", support);
        for (int tick = 1; tick <= 60; tick++)
        {
            world.BeginStep();
            world.SetTransform("deck", Matrix4x4.CreateTranslation(tick * 0.05f, tick * 0.02f, 0));
            Ride(motor, world, ref state, ref support, Vector3.Zero);
            Assert.Equal("deck", support);
        }
        Assert.InRange(state.Feet.X, 3.499f, 3.501f);
        Assert.InRange(state.Feet.Y, 1.399f, 1.401f);

        float yaw = 0;
        for (int tick = 1; tick <= 90; tick++)
        {
            world.BeginStep();
            world.SetTransform("deck", Matrix4x4.CreateRotationY(tick * MathF.PI / 180) * Matrix4x4.CreateTranslation(3, 1.2f, 0));
            KinematicCarry3D carry = world.Carry(Capsule3D.AtFeet(state.Feet), support);
            yaw += carry.Yaw;
            state = motor.Step(world, state, Vector3.Zero, false, Tick, carry: carry.Displacement, carryWorld: world.Statics).State;
        }
        // A quarter turn about the deck centre takes the rider from +X to -Z (right-handed, +Y up).
        Assert.InRange(state.Feet.X, 2.99f, 3.01f);
        Assert.InRange(state.Feet.Z, -0.51f, -0.49f);
        Assert.InRange(yaw, MathF.PI / 2 - 0.001f, MathF.PI / 2 + 0.001f);
    }

    [Fact]
    public void PlatformsPushStandingCharactersAndCrushAgainstWallsIsReported()
    {
        var statics = new SpatialWorld3D([
            Box("ground", new(0, -0.5f, 0), new(40, 0.5f, 40)),
            Box("wall", new(6, 2, 0), new(0.5f, 2, 4)),
        ]);
        var pusher = new KinematicBody3D("pusher", CollisionMesh3D.Box(new(0.5f, 1, 1)));
        var world = new KinematicSpatialWorld3D(statics, [pusher],
            new Dictionary<string, Matrix4x4> { ["pusher"] = Matrix4x4.CreateTranslation(-2, 1, 0) });
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(Vector3.Zero, 0);
        string? support = null;
        bool pushed = false;
        bool crushed = false;
        for (int tick = 1; tick <= 240 && !crushed; tick++)
        {
            world.BeginStep();
            world.SetTransform("pusher", Matrix4x4.CreateTranslation(-2 + tick * 0.05f, 1, 0));
            KinematicCarry3D carry = world.Carry(Capsule3D.AtFeet(state.Feet), support);
            pushed |= carry.PushedBy.Contains("pusher");
            CharacterMove3D move = motor.Step(world, state, Vector3.Zero, false, Tick, carry: carry.Displacement, carryWorld: world.Statics);
            state = move.State;
            support = move.GroundColliderId;
            crushed = move.Embedded || !world.Overlap(Capsule3D.AtFeet(state.Feet)).IsEmpty;
        }
        Assert.True(pushed);
        Assert.True(crushed);
        Assert.InRange(state.Feet.X, 4.5f, 5.3f);
    }

    [Fact]
    public void JumpingFromALiftKeepsItsVelocityAvailableAndLandingRebinds()
    {
        KinematicSpatialWorld3D world = Platforms(Vector3.Zero);
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(new(0, 0.2f, 0), 0);
        string? support = null;
        Ride(motor, world, ref state, ref support, Vector3.Zero);
        world.BeginStep();
        world.SetTransform("deck", Matrix4x4.CreateTranslation(0.1f, 0, 0));
        Vector3 velocity = world.PointVelocity("deck", state.Feet, Tick);
        Assert.InRange(velocity.X, 5.99f, 6.01f);
        CharacterMove3D jump = Ride(motor, world, ref state, ref support, Vector3.Zero, jump: true);
        Assert.False(jump.Grounded);
        Assert.Null(support);
        for (int tick = 0; tick < 120 && support is null; tick++)
        {
            world.BeginStep();
            Ride(motor, world, ref state, ref support, Vector3.Zero);
        }
        Assert.Equal("deck", support);
    }

    [Fact]
    public void KinematicQueriesMatchAnEquivalentStaticWorld()
    {
        Matrix4x4 pose = Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(1, 2, -3);
        var mesh = CollisionMesh3D.Box(new(1, 0.5f, 2));
        var reference = new SpatialWorld3D([new Collider3D("deck", mesh, pose)]);
        var world = new KinematicSpatialWorld3D(new SpatialWorld3D([]), [new KinematicBody3D("deck", mesh)],
            new Dictionary<string, Matrix4x4> { ["deck"] = pose });
        var ray = new Ray3D(new(1, 10, -3), -Vector3.UnitY, 20);
        Assert.Equal(reference.Raycast(ray)!.Value.Distance, world.Raycast(ray)!.Value.Distance, 4);
        var capsule = Capsule3D.AtFeet(new(-4, 2, -3));
        var sweepA = reference.Sweep(capsule, new(8, 0, 0))!.Value;
        var sweepB = world.Sweep(capsule, new(8, 0, 0))!.Value;
        Assert.Equal(sweepA.TimeOfImpact, sweepB.TimeOfImpact, 4);
        Assert.True(Vector3.Distance(sweepA.Normal, sweepB.Normal) < 1e-3f);
        Assert.True(Vector3.Distance(sweepA.Point, sweepB.Point) < 1e-3f);
        Assert.Equal(reference.Overlap(Capsule3D.AtFeet(new(1, 1.5f, -3))).Length, world.Overlap(Capsule3D.AtFeet(new(1, 1.5f, -3))).Length);
        Assert.Throws<ArgumentException>(() => world.SetTransform("deck", Matrix4x4.CreateScale(2)));
        Assert.Throws<ArgumentException>(() => new KinematicSpatialWorld3D(reference, [new KinematicBody3D("deck", mesh)]));
    }

    [Fact]
    public void CameraBoomShortensBehindWallsAndEasesBackOut()
    {
        var world = new SpatialWorld3D([Box("wall", new(0, 1, 3), new(4, 4, 0.2f))]);
        var boom = new CameraBoom3D(radius: 0.2f, margin: 0.05f, extendRate: 4);
        Vector3 pivot = new(0, 1.5f, 0);
        Vector3 eye = boom.Update(world, pivot, Vector3.UnitZ, 6, Tick);
        Assert.InRange(eye.Z, 2.5f, 2.6f);
        Assert.Empty(world.Overlap(Capsule3D.Sphere(eye, 0.19f)));
        var open = new SpatialWorld3D([]);
        eye = boom.Update(open, pivot, Vector3.UnitZ, 6, 0.25f);
        Assert.InRange(eye.Z, 3.5f, 3.6f);
        for (int tick = 0; tick < 60; tick++)
        {
            eye = boom.Update(open, pivot, Vector3.UnitZ, 6, Tick);
        }
        Assert.Equal(6, eye.Z, 4);
    }
}
