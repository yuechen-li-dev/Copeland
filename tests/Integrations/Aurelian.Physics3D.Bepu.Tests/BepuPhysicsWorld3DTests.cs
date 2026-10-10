using System.Numerics;
using Aurelian.NativeComposition;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using Aurelian.Spatial3D;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Physics3D.Bepu.Tests;

public sealed class BepuPhysicsWorld3DTests
{
    [Fact]
    public void FallingSphereSettlesAndReportsOwnedContactIds()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Floor());
        world.AddBody(Ball("ball", new(0, 5, 0)) with { SemanticOwnerId = "agent-ball" });
        bool touched = false;
        for (int tick = 1; tick <= 240; tick++)
        {
            touched |= world.Step(new(tick)).Contacts.Contains(new("ball", "floor"));
        }
        PhysicsBodyState3D state = world.GetBody("ball");
        Assert.InRange(state.Pose.Position.Y, .48f, .52f);
        Assert.InRange(state.Velocity.Linear.Length(), 0, .05f);
        Assert.Equal("agent-ball", state.SemanticOwnerId);
        Assert.True(touched);
        Assert.Equal(new Vector3(0, -.25f, 0), world.GetBody("floor").Pose.Position);
    }

    [Fact]
    public void FreeFallMatchesGravityAndImpulseUsesMassAndAngularOffset()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Ball("ball", new(0, 5, 0)) with { Mass = 2 });
        world.ApplyImpulse("ball", new(4, 0, 0), Vector3.UnitY);
        Assert.Equal(2, world.GetBody("ball").Velocity.Linear.X);
        Assert.True(world.GetBody("ball").Velocity.Angular.Z < 0);
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Step(new(tick));
        }
        Assert.InRange(world.GetBody("ball").Velocity.Linear.Y, -9.82f, -9.80f);
    }

    [Fact]
    public void CollisionFilteringIsMutualAndQueriesUseExistingFilterContract()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Floor() with { Layer = 1, Mask = 1 });
        world.AddBody(Ball("ghost", new(0, 2, 0)) with { Layer = 2, Mask = uint.MaxValue });
        for (int tick = 1; tick <= 90; tick++)
        {
            Assert.Empty(world.Step(new(tick)).Contacts);
        }
        Assert.True(world.GetBody("ghost").Pose.Position.Y < -5);
        Assert.Null(world.Raycast(new(new(0, 4, 0), -Vector3.UnitY, 20), new(4, 4)));
        Assert.Equal("floor", world.Raycast(new(new(0, 4, 0), -Vector3.UnitY, 20), new(1, 1))!.Value.BodyId);
    }

    [Fact]
    public void KinematicPlatformPushesBodyWithoutReceivingGravity()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Floor() with { MotionType = PhysicsMotionType3D.Kinematic, Velocity = new(Vector3.UnitY, Vector3.Zero) });
        world.AddBody(Ball("ball", new(0, .5f, 0)));
        for (int tick = 1; tick <= 60; tick++)
        {
            world.Step(new(tick));
        }
        Assert.InRange(world.GetBody("floor").Pose.Position.Y, .74f, .76f);
        Assert.InRange(world.GetBody("ball").Pose.Position.Y, 1.48f, 1.54f);
        Assert.Throws<InvalidOperationException>(() => world.ApplyImpulse("floor", Vector3.One));
    }

    [Fact]
    public void SceneCollisionMeshesAndAgentLifetimeUseTheRealBackend()
    {
        using var world = new BepuPhysicsWorld3D();
        var document = Scene.World("lab", [Scene.Box("floor", new(10, .5f, 10), Vector4.One,
            new(0, -.25f, 0), SceneCollision.Solid)]);
        ScenePlan plan = SceneCompiler.Compile(document);
        using IDisposable statics = PhysicsScene3D.AddStatics(world, SceneSpatial3D.Build(plan));
        using SceneInstance scene = plan.Mount();
        var body = new PhysicsAgentDefinition3D(world, Ball("prototype", Vector3.Zero),
            Scene.Group("body", [Scene.Box("cube", Vector3.One, Vector4.One)]), "test-body/v1");
        SceneAgent<PhysicsBodyState3D> agent = scene.Spawn("ball", body, SceneTransform.At(new(0, 3, 0)));
        for (int tick = 1; tick <= 180; tick++)
        {
            world.Step(new(tick));
            PhysicsScene3D.Publish(scene, world);
        }
        Assert.InRange(agent.State.Pose.Position.Y, .48f, .52f);
        Assert.Equal(agent.State.Pose.Matrix, agent.WorldTransform);
        Assert.Equal(agent.State.Pose.Position, scene.Project().Boxes.Single(box => box.Id == "ball.cube").WorldTransform.Translation);
        Assert.True(scene.Despawn("ball"));
        Assert.Equal(1, world.BodyCount);
    }

    [Fact]
    public void TeleportUpdatesBroadPhaseAndRejectsStaticMotion()
    {
        using var world = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero });
        world.AddBody(Ball("ball", Vector3.Zero));
        world.AddBody(Floor());
        world.SetMotion("ball", PhysicsPose3D.At(new(10, 3, 0)), default);
        PhysicsRayHit3D? hit = world.Raycast(new(new(10, 8, 0), -Vector3.UnitY, 10));
        Assert.Equal("ball", hit!.Value.BodyId);
        Assert.InRange(hit.Value.Distance, 4.49f, 4.51f);
        Assert.Throws<InvalidOperationException>(() => world.SetMotion("floor", PhysicsPose3D.At(Vector3.Zero), default));
    }

    [Fact]
    public void SnapshotIsImmutableSortedAndReplayAgreesOnSameRuntime()
    {
        using var first = new BepuPhysicsWorld3D();
        using var second = new BepuPhysicsWorld3D();
        foreach (IPhysicsWorld3D world in new[] { first, second })
        {
            world.AddBody(Floor());
            world.AddBody(Ball("z", new(0, 2, 0)));
            world.AddBody(Ball("a", new(0, 4, 0)));
        }
        PhysicsSnapshot3D before = first.CaptureSnapshot();
        for (int tick = 1; tick <= 150; tick++)
        {
            first.Step(new(tick));
            second.Step(new(tick));
        }
        Assert.Equal(first.CaptureSnapshot().Bodies.ToArray(), second.CaptureSnapshot().Bodies.ToArray());
        Assert.Equal(new[] { "a", "floor", "z" }, before.Bodies.Select(body => body.Id));
        Assert.Equal(4, before.Bodies[0].Pose.Position.Y);
        Assert.Equal(0, before.Tick);
        Assert.Equal(150, first.Tick);
    }

    [Fact]
    public void MultithreadedMixedPrimitiveStackRemainsFiniteAndSupported()
    {
        using var world = new BepuPhysicsWorld3D(new() { WorkerCount = 2, Substeps = 2 });
        world.AddBody(Floor());
        world.AddBody(new("box", new PhysicsShape3D.Box(Vector3.One), PhysicsPose3D.At(new(0, 4, 0))));
        world.AddBody(new("capsule", new PhysicsShape3D.Capsule(.3f, .8f), PhysicsPose3D.At(new(2, 4, 0))));
        for (int tick = 1; tick <= 240; tick++)
        {
            world.Step(new(tick));
        }
        Assert.InRange(world.GetBody("box").Pose.Position.Y, .48f, .52f);
        Assert.InRange(world.GetBody("capsule").Pose.Position.Y, .28f, .72f);
        foreach (PhysicsBodyState3D body in world.CaptureSnapshot().Bodies)
        {
            body.Pose.Validate();
            body.Velocity.Validate();
        }
    }

    [Fact]
    public void InvalidRequestsDoNotMutateAndRemovalReleasesIdentity()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Ball("ball", Vector3.Zero));
        Assert.Throws<ArgumentException>(() => world.AddBody(Ball("ball", Vector3.Zero)));
        Assert.Throws<ArgumentException>(() => world.Step(new(2)));
        Assert.Equal(0, world.Tick);
        Assert.Throws<ArgumentException>(() => world.AddBody(Ball("bad", new(float.NaN, 0, 0))));
        Assert.Throws<NotSupportedException>(() => world.AddBody(new("bad-mesh",
            new PhysicsShape3D.StaticMesh(CollisionMesh3D.Box(Vector3.One)), PhysicsPose3D.At(Vector3.Zero))));
        Assert.True(world.RemoveBody("ball"));
        Assert.False(world.RemoveBody("ball"));
        world.AddBody(Ball("ball", Vector3.One));
        Assert.Equal(1, world.BodyCount);
    }

    [Fact]
    public void ScaledPhysicsAgentsAreExplicitlyRejectedBeforeActivation()
    {
        using var world = new BepuPhysicsWorld3D();
        using SceneInstance scene = SceneCompiler.Compile(Scene.World("lab", [])).Mount();
        var body = new PhysicsAgentDefinition3D(world, Ball("prototype", Vector3.Zero),
            Scene.Group("body", []), "test-body/v1");
        Assert.Throws<NotSupportedException>(() => scene.Spawn("ball", body, SceneTransform.Identity with { Scale = new(2) }));
        Assert.Equal(0, world.BodyCount);
    }

    [Fact]
    public void WithCustomizationChangesDefinitionIdentityAndPublicationIsExplicit()
    {
        using var world = new BepuPhysicsWorld3D();
        using SceneInstance scene = SceneCompiler.Compile(Scene.World("lab", [])).Mount();
        var definition = new PhysicsAgentDefinition3D(world, Ball("prototype", Vector3.Zero),
            Scene.Group("body", [Scene.Box("visual", Vector3.One, Vector4.One)]), "body/v1");
        Assert.NotEqual(definition.Identity, (definition with { Body = definition.Body with { Mass = 2 } }).Identity);
        Assert.NotEqual(definition.Identity, (definition with { Body = definition.Body with { Continuity = PhysicsContinuity3D.Continuous } }).Identity);
        Assert.NotEqual(definition.Identity, (definition with { Body = definition.Body with { MinimumSweepSeconds = .000001f } }).Identity);
        Assert.NotEqual(definition.Identity, (definition with { Body = definition.Body with { Shape = new PhysicsShape3D.Box(Vector3.One) } }).Identity);
        SceneAgent<PhysicsBodyState3D> agent = scene.Spawn("ball", definition, SceneTransform.At(new(0, 4, 0)));
        PhysicsBodyState3D initial = agent.State;
        world.Step(new(1));
        Assert.Equal(initial, agent.State);
        PhysicsScene3D.Publish(scene, world);
        Assert.NotEqual(initial.Pose.Position, agent.State.Pose.Position);
        Assert.Equal(world.GetBody("ball"), agent.State);
    }

    [Fact]
    public void ContactCollectionCanBeDisabledWithoutDisablingCollisionResponse()
    {
        using var world = new BepuPhysicsWorld3D(new() { CollectContacts = false });
        world.AddBody(Floor());
        world.AddBody(Ball("ball", new(0, 2, 0)));
        for (int tick = 1; tick <= 180; tick++)
        {
            Assert.Empty(world.Step(new(tick)).Contacts);
        }
        Assert.InRange(world.GetBody("ball").Pose.Position.Y, .48f, .52f);
    }

    [Fact]
    public void CrossThreadCommandsFailBeforeMutation()
    {
        using var world = new BepuPhysicsWorld3D();
        world.AddBody(Ball("ball", Vector3.Zero));
        Assert.IsType<InvalidOperationException>(PhysicsTestThreads.RunOffOwnerThread(() => world.ApplyImpulse("ball", Vector3.One)));
        Assert.Equal(default, world.GetBody("ball").Velocity);
    }

    [Fact]
    public void DisposedWorldAndBadConfigurationFailExplicitly()
    {
        var world = new BepuPhysicsWorld3D();
        world.Dispose();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Step(new(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BepuPhysicsWorld3D(new() { WorkerCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BepuPhysicsWorld3D(new() { FixedDeltaSeconds = .2f }));
    }

    private static PhysicsBody3D Ball(string id, Vector3 at)
    {
        return new(id, new PhysicsShape3D.Sphere(.5f), PhysicsPose3D.At(at));
    }

    private static PhysicsBody3D Floor()
    {
        return new("floor", new PhysicsShape3D.Box(new(50, .5f, 50)), PhysicsPose3D.At(new(0, -.25f, 0)))
        {
            MotionType = PhysicsMotionType3D.Static,
        };
    }
}
