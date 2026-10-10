using System.Collections.Immutable;
using System.Numerics;
using Aurelian.NativeComposition;
using Aurelian.Physics3D;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Physics3D.Bepu.Tests;

public sealed class PhysicsAssemblyTests
{
    [Fact]
    public void RevolutePlacesPortsAndDriveUsesAuthoredPositiveDirection()
    {
        using var world = World();
        var plan = Rotor().Compile();
        Assert.Equal(new Vector3(0, 1, 0), plan.Bodies.Single(body => body.Id == "arm").Pose.Position);
        using var mounted = plan.Mount(world, "first", PhysicsPose3D.At(new(3, 0, 0)));
        Assert.Equal(new Vector3(3, 0, 0), mounted.PortPose("pivot").Position);
        for (int tick = 1; tick <= 60; tick++) world.Step(new(tick));
        Assert.InRange(world.GetBody("first/arm").Velocity.Angular.Z, 1.98f, 2.02f);
        var arm = world.GetBody("first/arm").Pose;
        Vector3 pivot = Vector3.Transform(-Vector3.UnitY, arm.Matrix);
        Assert.InRange(Vector3.Distance(pivot, new(3, 0, 0)), 0, .01f);
        Assert.Equal("first/anchor", Assert.IsType<PhysicsJoint3D.Hinge>(world.CaptureJoints()[0]).A);
    }

    [Fact]
    public void RepeatedDefinitionsRetainDistinctPathsAndExposeOnlyAliases()
    {
        using var world = World();
        var rotor = Rotor();
        var pair = new PhysicsAssembly3D("pair")
        {
            Children = [new("left", rotor, PhysicsPose3D.At(new(-3, 0, 0))),
                new("right", rotor, PhysicsPose3D.At(new(3, 0, 0)))],
            Expose = ImmutableDictionary<string, PhysicsEndpoint3D>.Empty.Add("leftPivot", new("left", "pivot")),
        };
        var plan = pair.Compile();
        Assert.Equal(4, plan.Bodies.Length);
        Assert.Equal("left/anchor", plan.Ports["leftPivot"].BodyPath);
        Assert.Equal("left/anchor", plan.Connections[0].SourceA.BodyPath);
        using (var mounted = plan.Mount(world, "pair", PhysicsPose3D.At(Vector3.Zero)))
        {
            Assert.Equal(4, mounted.Capture().Bodies.Length);
            Assert.Equal(new Vector3(-3, 0, 0), mounted.PortPose("leftPivot").Position);
            Assert.Throws<ArgumentException>(() => plan.Mount(world, "pair", PhysicsPose3D.At(Vector3.Zero)));
            Assert.Equal(4, world.BodyCount);
        }
        Assert.Equal(0, world.BodyCount);
        Assert.Equal(0, world.JointCount);
        Assert.Throws<ArgumentException>(() => (pair with
        {
            Expose = pair.Expose.Add("private", new("left", "arm")),
        }).Compile());
    }

    [Fact]
    public void ParentConnectionMovesEntireChildOccurrenceAndRetainsItsInternalJoint()
    {
        var root = new PhysicsAssembly3D("root")
        {
            Parts = [Part("base", PhysicsMotionType3D.Kinematic, new(4, 2, 0), Vector3.Zero) with
            {
                Body = Part("base", PhysicsMotionType3D.Kinematic, new(4, 2, 0), Vector3.Zero).Body with { Mask = 0 },
            }],
            Children = [new("module", Rotor() with
            {
                Parts = [Part("anchor", PhysicsMotionType3D.Dynamic, Vector3.Zero, Vector3.Zero),
                    Part("arm", PhysicsMotionType3D.Dynamic, new(99, 0, 0), -Vector3.UnitY)],
            }, PhysicsPose3D.At(new(100, 0, 0)))],
            Interfaces = [new PhysicsInterface3D<Fixed3D>("mount", new("base", "socket"), new("module", "pivot"))],
        };
        var plan = root.Compile();
        Assert.Equal(new Vector3(4, 2, 0), plan.Bodies.Single(body => body.Id == "module/anchor").Pose.Position);
        Assert.Equal(new Vector3(4, 3, 0), plan.Bodies.Single(body => body.Id == "module/arm").Pose.Position);
        using var world = World();
        using var mount = plan.Mount(world, "machine", new(Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f)));
        var initial = world.GetBody("machine/module/anchor").Pose;
        world.ApplyImpulse("machine/module/anchor", new(2, 3, 1));
        for (int tick = 1; tick <= 90; tick++) world.Step(new(tick));
        Assert.InRange(Vector3.Distance(world.GetBody("machine/module/anchor").Pose.Position, initial.Position), 0, .01f);
        Assert.Equal(3, world.JointCount);
    }

    [Fact]
    public void CyclesMultipleDriversAndInvalidJointBodiesFailBeforeWorldMutation()
    {
        var assembly = Rotor();
        Assert.Throws<ArgumentException>(() => (assembly with
        {
            Interfaces = [.. assembly.Interfaces, new PhysicsInterface3D<Fixed3D>("cycle", new("arm", "socket"), new("anchor", "socket"))],
        }).Compile());
        Assert.Throws<ArgumentException>(() => (assembly with
        {
            Interfaces = [.. assembly.Interfaces, new PhysicsInterface3D<Spherical3D>("duplicate", new("anchor", "socket"), new("arm", "socket"))],
        }).Compile());
        Assert.Throws<ArgumentException>(() => (assembly with
        {
            Parts = [assembly.Parts[0] with { Body = assembly.Parts[0].Body with { MotionType = PhysicsMotionType3D.Static } }, assembly.Parts[1]],
        }).Compile());
    }

    [Fact]
    public void SceneAgentsOwnAssemblyLifetimeAndPublishSolverOutput()
    {
        using var world = World();
        using var scene = SceneCompiler.Compile(Scene.World("lab", [])).Mount();
        var definition = new PhysicsAssemblyAgentDefinition3D(world, Rotor(), "rotor/v1")
        {
            Visuals = ImmutableDictionary<string, SceneGroup>.Empty.Add("arm",
                Scene.Group("shape", [Scene.Box("bar", new(.2f, 2, .2f), Vector4.One)])),
        };
        var agent = scene.Spawn("rotor", definition, SceneTransform.At(new(3, 0, 0)));
        Assert.NotEqual(definition.Identity, (definition with { Assembly = Rotor(3) }).Identity);
        var before = agent.State;
        world.Step(new(1));
        Assert.Same(before, agent.State);
        PhysicsScene3D.Publish(scene, world);
        Assert.Equal(world.Tick, agent.State.Snapshot.Tick);
        Assert.Equal(world.GetBody("rotor/arm").Pose.Position, scene.Project().Boxes.Single().WorldTransform.Translation);
        Assert.True(scene.Despawn("rotor"));
        Assert.Equal(0, world.BodyCount);
        Assert.Equal(0, world.JointCount);
    }

    [Fact]
    public void FixedInterfacePreservesAsymmetricPortFramesUnderRootRotation()
    {
        var frameA = new PhysicsPose3D(new(.7f, .2f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f));
        var frameB = new PhysicsPose3D(new(0, -.6f, .1f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .7f));
        var assembly = new PhysicsAssembly3D("fixed")
        {
            Parts =
            [
                Part("base", PhysicsMotionType3D.Kinematic, new(1, 2, 0), Vector3.Zero) with
                {
                    Ports = ImmutableDictionary<string, PhysicsPose3D>.Empty.Add("socket", frameA),
                },
                Part("arm", PhysicsMotionType3D.Dynamic, new(100, 0, 0), Vector3.Zero) with
                {
                    Ports = ImmutableDictionary<string, PhysicsPose3D>.Empty.Add("socket", frameB),
                },
            ],
            Interfaces = [new PhysicsInterface3D<Fixed3D>("join", new("base", "socket"), new("arm", "socket"))],
        };
        using var world = World();
        using var mounted = assembly.Compile().Mount(world, "unit", new(new(4, 0, 0),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, .8f)));
        world.ApplyImpulse("unit/arm", new(2, 3, 1), new(.2f, 0, .3f));
        for (int tick = 1; tick <= 120; tick++) world.Step(new(tick));
        Matrix4x4 a = frameA.Matrix * world.GetBody("unit/base").Pose.Matrix;
        Matrix4x4 b = frameB.Matrix * world.GetBody("unit/arm").Pose.Matrix;
        Assert.InRange(Vector3.Distance(a.Translation, b.Translation), 0, .001f);
        Assert.InRange(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitZ, a), Vector3.TransformNormal(Vector3.UnitZ, b)), 0, .001f);
    }

    [Fact]
    public void InterfaceWithUsesTheUpdatedEndpointRatherThanDuplicatedConstructorFields()
    {
        var original = new PhysicsInterface3D<Revolute3D>("join", new("anchor", "socket"), new("arm", "socket"));
        var changed = original with { B = new("missing", "socket") };
        Assert.Throws<ArgumentException>(() => (Rotor() with { Interfaces = [changed] }).Compile());
    }

    internal static PhysicsAssembly3D Rotor(float speed = 2)
    {
        return new("rotor")
        {
            Parts = [Part("anchor", PhysicsMotionType3D.Kinematic, Vector3.Zero, Vector3.Zero),
                Part("arm", PhysicsMotionType3D.Dynamic, new(99, 0, 0), -Vector3.UnitY)],
            Interfaces = [new PhysicsInterface3D<Revolute3D>("hinge", new("anchor", "socket"), new("arm", "socket"))
            {
                Drive = new(speed, 100),
            }],
            Expose = ImmutableDictionary<string, PhysicsEndpoint3D>.Empty.Add("pivot", new("anchor", "socket")),
        };
    }

    private static PhysicsPart3D Part(string id, PhysicsMotionType3D motion, Vector3 position, Vector3 port)
    {
        return new(new(id, new PhysicsShape3D.Box(new(.2f, 2, .2f)), PhysicsPose3D.At(position)) { MotionType = motion })
        {
            Ports = ImmutableDictionary<string, PhysicsPose3D>.Empty.Add("socket", PhysicsPose3D.At(port)),
        };
    }

    private static BepuPhysicsWorld3D World() => new(new() { Gravity = Vector3.Zero, Substeps = 2 });
}
