using System.Numerics;
using Aurelian.Physics3D;
using Aurelian.Spatial3D;
using Aurelian.NativeComposition;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Physics3D.Bepu.Tests;

public sealed class PhysicsCharacterTests
{
    [Fact]
    public void LiveQueriesUseAcceptedPosesWithoutAdvancingPhysics()
    {
        using var world = World();
        var query = world.CreateQueryWorld();
        Assert.Equal("platform", query.Raycast(new(new(0, 3, 0), -Vector3.UnitY, 10))!.Value.ColliderId);
        Assert.Empty(query.Overlap(Capsule3D.AtFeet(Vector3.Zero, .3f, 1.8f)));
        Assert.NotEmpty(query.Overlap(Capsule3D.AtFeet(new(0, -.2f, 0), .3f, 1.8f)));
        world.SetMotion("platform", PhysicsPose3D.At(new(5, -.25f, 0)), default);
        Assert.Null(query.Raycast(new(new(0, 3, 0), -Vector3.UnitY, 10)));
        Assert.Equal(0, world.Tick);
    }

    [Fact]
    public void TranslatingAndRotatingPlatformCarriesFeetInItsLocalFrame()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", new(.8f, 0, 0)));
        var state = motor.Initialize(world, "player");
        Assert.True(state.Grounded);
        for (int tick = 1; tick <= 60; tick++)
        {
            var pose = new PhysicsPose3D(new(tick * .01f, -.25f, 0),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, tick * .01f));
            world.SetMotion("platform", pose, default);
            world.Step(new(tick));
            state = motor.Move(world, state, new(tick, Vector3.Zero));
            Vector3 expected = Vector3.Transform(new(.8f, .25f, 0), pose.Matrix);
            Assert.InRange(Vector3.Distance(state.Motion.Feet, expected), 0, .001f);
            Assert.True(state.Grounded);
        }
    }

    [Fact]
    public void JumpInheritsPlatformVelocityAndDetaches()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var state = motor.Initialize(world, "player");
        world.SetMotion("platform", PhysicsPose3D.At(new(.02f, -.24f, 0)), default);
        world.Step(new(1));
        state = motor.Move(world, state, new(1, Vector3.Zero, true));
        Assert.False(state.Grounded);
        Assert.Null(state.SupportBodyId);
        Assert.InRange(state.AirborneVelocity.X, 1.19f, 1.21f);
        Assert.True(state.Motion.VerticalVelocity > motor.Options.Movement.JumpSpeed);
        float x = state.Motion.Feet.X;
        world.SetMotion("platform", PhysicsPose3D.At(new(2, -.24f, 0)), default);
        world.Step(new(2));
        state = motor.Move(world, state, new(2, Vector3.Zero));
        Assert.InRange(state.Motion.Feet.X - x, .019f, .021f);
    }

    [Fact]
    public void CadenceErrorsDoNotMoveBody()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var state = motor.Initialize(world, "player");
        var before = world.GetBody("player");
        Assert.Throws<ArgumentException>(() => motor.Move(world, state, new(1, Vector3.UnitX)));
        Assert.Equal(before, world.GetBody("player"));
    }

    [Fact]
    public void CarryStopsAtWallRatherThanTeleportingThroughIt()
    {
        using var world = World();
        world.AddBody(new("wall", new PhysicsShape3D.Box(new(.2f, 5, 5)), PhysicsPose3D.At(new(.9f, 2, 0)))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var state = motor.Initialize(world, "player");
        world.SetMotion("platform", PhysicsPose3D.At(new(1, -.25f, 0)), default);
        world.Step(new(1));
        state = motor.Move(world, state, new(1, Vector3.Zero));
        Assert.InRange(state.Motion.Feet.X, .4f, .51f);
        Assert.Contains(state.CarrierMovement!.Contacts, hit => hit.ColliderId == "wall");
    }

    [Fact]
    public void PushImpulseIsBoundedAndPropRespondsOnTheNextSolverStep()
    {
        using var world = World();
        world.AddBody(new("crate", new PhysicsShape3D.Box(Vector3.One), PhysicsPose3D.At(new(1, .5f, 0)))
        {
            Friction = 0,
        });
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var state = motor.Initialize(world, "player");
        bool pushed = false;
        for (int tick = 1; tick <= 30; tick++)
        {
            world.Step(new(tick));
            state = motor.Move(world, state, new(tick, new(4, 0, 0)));
            foreach (var push in state.Pushes)
            {
                pushed = true;
                Assert.Equal("crate", push.BodyId);
                Assert.InRange(push.Impulse.Length(), 0, motor.Options.MaximumPushForce * world.Options.FixedDeltaSeconds + .0001f);
            }
        }
        Assert.True(pushed);
        Assert.True(world.GetBody("crate").Pose.Position.X > 1.2f);
    }

    [Fact]
    public void RemovedSupportLeavesAnInspectableAirborneState()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var state = motor.Initialize(world, "player");
        world.SetMotion("platform", PhysicsPose3D.At(new(.02f, -.25f, 0)), default);
        world.Step(new(1));
        state = motor.Move(world, state, new(1, Vector3.Zero));
        world.RemoveBody("platform");
        world.Step(new(2));
        state = motor.Move(world, state, new(2, Vector3.Zero));
        Assert.False(state.Grounded);
        Assert.Null(state.SupportBodyId);
        Assert.InRange(state.AirborneVelocity.X, 1.19f, 1.21f);
        Assert.True(state.Motion.Feet.Y < 0);
    }

    [Fact]
    public void CharacterAgentUsesExplicitCadenceAndDespawnReleasesOnlyItsBody()
    {
        using var world = World();
        using var scene = SceneCompiler.Compile(Scene.World("lab", [])).Mount();
        var definition = new PhysicsCharacterAgentDefinition3D(world,
            Scene.Group("visual", [Scene.Box("marker", Vector3.One, Vector4.One)]), "character/v1");
        var agent = scene.Spawn("player", definition, SceneTransform.Identity);
        Assert.True(agent.State.Grounded);
        Assert.NotEqual(definition.Identity, (definition with
        {
            Options = definition.Options with { MaximumPushForce = 100 },
        }).Identity);
        world.Step(new(1));
        Assert.Throws<InvalidOperationException>(() => PhysicsScene3D.Publish(scene, world));
        definition.Move(agent, new(1, Vector3.UnitX));
        PhysicsScene3D.Publish(scene, world);
        Assert.Equal(agent.State.Motion.Feet, scene.Project().Boxes.Single().WorldTransform.Translation);
        Assert.True(scene.Despawn("player"));
        Assert.Equal(1, world.BodyCount);
    }

    [Fact]
    public void InvalidBodyAndEmbeddedSpawnFailBeforeActivationCanLeakBodies()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        Assert.Throws<InvalidOperationException>(() => motor.Initialize(world, "platform"));
        using var scene = SceneCompiler.Compile(Scene.World("lab", [])).Mount();
        var definition = new PhysicsCharacterAgentDefinition3D(world, Scene.Group("visual", []), "character/v1");
        Assert.Throws<InvalidOperationException>(() => scene.Spawn("bad", definition, SceneTransform.At(new(0, -.2f, 0))));
        Assert.Equal(1, world.BodyCount);
        Assert.Empty(scene.Agents);
    }

    [Fact]
    public void LiveQueriesRespectLayersSelfExclusionAndWorldLifetime()
    {
        var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", Vector3.Zero));
        var query = world.CreateQueryWorld("player");
        var ray = new Ray3D(new(0, 3, 0), -Vector3.UnitY, 10);
        Assert.Equal("platform", query.Raycast(ray)!.Value.ColliderId);
        Assert.Null(query.Raycast(ray, new(4, 4)));
        Assert.Empty(world.CreateQueryWorld("platform", "player").Overlap(Capsule3D.AtFeet(new(0, -.2f, 0), .3f, 1.8f)));
        Assert.IsType<InvalidOperationException>(PhysicsTestThreads.RunOffOwnerThread(() => query.Raycast(ray)));
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => query.Raycast(ray));
    }

    [Fact]
    public void GroundedMovementUsesLiveSlopeNormalsAndStopsAtCeiling()
    {
        using var world = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero });
        float angle = MathF.PI / 6;
        world.AddBody(new("slope", new PhysicsShape3D.Box(new(10, .5f, 10)),
            new(new(0, -.25f / MathF.Cos(angle), 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle)))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", new(0, .08f, 0)));
        var state = motor.Initialize(world, "player");
        for (int tick = 1; tick <= 30; tick++)
        {
            world.Step(new(tick));
            state = motor.Move(world, state, new(tick, Vector3.UnitX));
            Assert.True(state.Grounded);
        }
        Assert.InRange(state.Motion.Feet.X, .49f, .51f);
        Assert.InRange(state.Motion.Feet.Y, .32f, .35f);
        Assert.InRange(state.GroundNormal.Y, .86f, .87f);
        world.AddBody(new("ceiling", new PhysicsShape3D.Box(new(5, .2f, 5)), PhysicsPose3D.At(new(0, 2.3f, 0)))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        bool ceilingContact = false;
        for (int tick = 31; tick <= 45; tick++)
        {
            world.Step(new(tick));
            state = motor.Move(world, state, new(tick, Vector3.Zero, tick == 31));
            ceilingContact |= state.Movement!.Contacts.Any(contact => contact.ColliderId == "ceiling");
            Assert.True(state.Motion.Feet.Y + motor.Options.Movement.Height < 2.202f);
        }
        Assert.True(ceilingContact);
    }

    [Fact]
    public void TiltingPlatformCarriesTheUprightCapsuleWithoutEmbeddingItsBottomSphere()
    {
        using var world = World();
        var motor = new PhysicsCharacterMotor3D();
        world.AddBody(motor.Body("player", new(.8f, 0, 0)));
        var state = motor.Initialize(world, "player");
        Vector3 localCenter = state.SupportLocalFeet + Vector3.UnitY * motor.Options.Movement.Radius;
        for (int tick = 1; tick <= 60; tick++)
        {
            var pose = new PhysicsPose3D(new(0, -.25f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, tick * .005f));
            world.SetMotion("platform", pose, default);
            world.Step(new(tick));
            state = motor.Move(world, state, new(tick, Vector3.Zero));
            Assert.True(state.Grounded);
            Vector3 expected = Vector3.Transform(localCenter, pose.Matrix) - Vector3.UnitY * motor.Options.Movement.Radius;
            Assert.InRange(Vector3.Distance(state.Motion.Feet, expected), 0, .001f);
        }
    }

    private static BepuPhysicsWorld3D World()
    {
        var world = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero });
        world.AddBody(new("platform", new PhysicsShape3D.Box(new(4, .5f, 4)), PhysicsPose3D.At(new(0, -.25f, 0)))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
        });
        return world;
    }
}
