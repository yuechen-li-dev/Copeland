using System.Numerics;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using Xunit;

namespace Aurelian.Physics3D.Bepu.Tests;

public sealed class PhysicsJointTests
{
    [Fact]
    public void DistanceJointRetainsLengthAndBodyRemovalCleansItsJoint()
    {
        using var world = World();
        world.AddJoint(new PhysicsJoint3D.DistanceLimit("rope", "anchor", "bob", 1, 1));
        world.ApplyImpulse("bob", new(3, 0, 0));
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Step(new(tick));
        }
        Assert.InRange(Vector3.Distance(world.GetBody("anchor").Pose.Position, world.GetBody("bob").Pose.Position), .99f, 1.01f);
        Assert.Single(world.CaptureJoints());
        world.RemoveBody("bob");
        Assert.Equal(0, world.JointCount);
        Assert.Empty(world.CaptureJoints());
    }

    [Fact]
    public void BallSocketPreservesItsAuthoredLocalAnchors()
    {
        using var world = World();
        world.AddJoint(new PhysicsJoint3D.BallSocket("socket", "anchor", "bob", Vector3.Zero, Vector3.UnitY));
        world.ApplyImpulse("bob", new(2, 0, 0));
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Step(new(tick));
        }
        PhysicsPose3D bob = world.GetBody("bob").Pose;
        Vector3 attachment = bob.Position + Vector3.Transform(Vector3.UnitY, bob.Orientation);
        Assert.InRange(Vector3.Distance(attachment, world.GetBody("anchor").Pose.Position), 0, .015f);
    }

    [Fact]
    public void HingeAndMotorTrackRelativeAngularVelocity()
    {
        using var world = World(Vector3.Zero);
        world.SetMotion("bob", PhysicsPose3D.At(Vector3.Zero), default);
        world.AddJoint(new PhysicsJoint3D.Hinge("hinge", "anchor", "bob", Vector3.Zero, Vector3.Zero, Vector3.UnitY, Vector3.UnitY));
        world.AddJoint(new PhysicsJoint3D.AngularMotor("drive", "anchor", "bob", Vector3.UnitY, 2, 100));
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Step(new(tick));
        }
        Assert.InRange(world.GetBody("bob").Velocity.Angular.Y, -2.02f, -1.98f);
        Assert.InRange(world.GetBody("bob").Pose.Position.Length(), 0, .001f);
        Assert.True(world.RemoveJoint("drive"));
        Assert.False(world.RemoveJoint("drive"));
        Assert.Equal(1, world.JointCount);
    }

    [Fact]
    public void ExclusionsAreMutualAndReusedBodyIdsDoNotInheritThem()
    {
        using var world = World(Vector3.Zero);
        world.SetMotion("bob", PhysicsPose3D.At(Vector3.Zero), default);
        world.SetCollisionEnabled("bob", "anchor", false);
        Assert.Empty(world.Step(new(1)).Contacts);
        world.RemoveBody("bob");
        world.AddBody(new("bob", new PhysicsShape3D.Sphere(.2f), PhysicsPose3D.At(Vector3.Zero)));
        Assert.NotEmpty(world.Step(new(2)).Contacts);
    }

    [Fact]
    public void InvalidJointsAndContinuityAreRejectedBeforeMutation()
    {
        using var world = World();
        Assert.Throws<ArgumentException>(() => world.AddJoint(new PhysicsJoint3D.Hinge("bad", "anchor", "bob",
            Vector3.Zero, Vector3.Zero, new(2, 0, 0), Vector3.UnitY)));
        Assert.Throws<ArgumentException>(() => world.AddJoint(new PhysicsJoint3D.DistanceLimit("bad", "anchor", "bob", 2, 1)));
        Assert.Throws<ArgumentException>(() => world.AddBody(new("bad", new PhysicsShape3D.Sphere(.1f), PhysicsPose3D.At(Vector3.Zero))
        {
            Continuity = (PhysicsContinuity3D)99,
        }));
        Assert.Equal(0, world.JointCount);
        Assert.Equal(2, world.BodyCount);
    }

    [Theory]
    [InlineData(30, 10)]
    [InlineData(30, 100)]
    [InlineData(30, 500)]
    [InlineData(60, 10)]
    [InlineData(60, 100)]
    [InlineData(60, 500)]
    [InlineData(120, 10)]
    [InlineData(120, 100)]
    [InlineData(120, 500)]
    public void QualifiedSweptProfileStopsAtThinWall(int frequency, float speed)
    {
        float delta = 1f / frequency;
        using var world = new BepuPhysicsWorld3D(new()
        {
            Gravity = Vector3.Zero,
            FixedDeltaSeconds = delta,
            Substeps = 4,
            ContactSpring = new(1000, 1),
        });
        world.AddBody(new("wall", new PhysicsShape3D.Box(new(.01f, 10, 10)), PhysicsPose3D.At(Vector3.Zero))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        world.AddBody(new("shot", new PhysicsShape3D.Sphere(.05f), PhysicsPose3D.At(new(-1, 0, 0)))
        {
            Continuity = PhysicsContinuity3D.Continuous,
            MinimumSweepSeconds = .000001f,
            SweepConvergenceSeconds = .000001f,
            Velocity = new(new(speed, 0, 0), Vector3.Zero),
        });
        int ticks = (int)Math.Ceiling(2 / (speed * delta)) + 8;
        for (int tick = 1; tick <= ticks; tick++)
        {
            world.Step(new(tick));
            Assert.True(world.GetBody("shot").Pose.Position.X <= -.045f);
        }
        Assert.True(world.GetBody("shot").Velocity.Linear.X < 1);
    }

    [Fact]
    public void MultipleJointExclusionsRemainUntilLastJointIsRemoved()
    {
        using var world = World(Vector3.Zero);
        world.SetMotion("bob", PhysicsPose3D.At(Vector3.Zero), default);
        world.AddJoint(new PhysicsJoint3D.Hinge("hinge", "anchor", "bob", Vector3.Zero, Vector3.Zero, Vector3.UnitY, Vector3.UnitY));
        world.AddJoint(new PhysicsJoint3D.AngularMotor("motor", "anchor", "bob", Vector3.UnitY, 1, 10));
        Assert.Empty(world.Step(new(1)).Contacts);
        world.RemoveJoint("motor");
        world.SetCollisionEnabled("anchor", "bob", true);
        Assert.Empty(world.Step(new(2)).Contacts);
        world.RemoveJoint("hinge");
        Assert.NotEmpty(world.Step(new(3)).Contacts);
    }

    [Fact]
    public void JointCommandsRespectOwnerThreadAndDisposedWorld()
    {
        using var world = World(Vector3.Zero);
        var joint = new PhysicsJoint3D.DistanceLimit("rope", "anchor", "bob", 1, 1);
        Assert.IsType<InvalidOperationException>(PhysicsTestThreads.RunOffOwnerThread(() => world.AddJoint(joint)));
        Assert.Equal(0, world.JointCount);
        world.AddJoint(joint);
        Assert.IsType<InvalidOperationException>(PhysicsTestThreads.RunOffOwnerThread(() => world.RemoveJoint("rope")));
        Assert.IsType<InvalidOperationException>(PhysicsTestThreads.RunOffOwnerThread(() => world.SetCollisionEnabled("anchor", "bob", false)));
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.AddJoint(joint));
        Assert.Throws<ObjectDisposedException>(() => world.CaptureJoints());
    }

    private static BepuPhysicsWorld3D World(Vector3? gravity = null)
    {
        var world = new BepuPhysicsWorld3D(new() { Gravity = gravity ?? new(0, -9.81f, 0), Substeps = 2 });
        world.AddBody(new("anchor", new PhysicsShape3D.Sphere(.2f), PhysicsPose3D.At(Vector3.Zero))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
        });
        world.AddBody(new("bob", new PhysicsShape3D.Sphere(.2f), PhysicsPose3D.At(new(0, -1, 0))));
        return world;
    }
}
