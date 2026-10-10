using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Physics3D;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

public sealed record PhysicsCharacterAgentDefinition3D(IPhysicsWorld3D World, SceneGroup Visual, string DefinitionId)
    : AgentDefinition<PhysicsCharacterState3D>(AgentTemplate.Character("physics-character"))
{
    public PhysicsCharacterOptions3D Options { get; init; } = new();

    public override string Identity
    {
        get
        {
            var motor = new PhysicsCharacterMotor3D(Options);
            string body = PhysicsAgentIdentity3D.Compute(new(World, motor.Body("prototype", Vector3.Zero), Visual, DefinitionId));
            return PhysicsCompositionIdentity3D.Hash(writer =>
            {
                writer.Write("character/v1");
                writer.Write(body);
                writer.Write(Options.Movement.Gravity);
                writer.Write(Options.Movement.JumpSpeed);
                writer.Write(Options.Movement.MaximumSlopeDegrees);
                writer.Write(Options.Movement.GroundSnap);
                writer.Write(Options.MaximumPushForce);
                writer.Write(Options.PushSpeed);
            });
        }
    }

    public override void ValidatePlacement(ScenePlacement placement)
    {
        Options.Validate();
        var pose = PhysicsRigidPlacement3D.From(placement.WorldTransform);
        if (Vector3.DistanceSquared(Vector3.Transform(Vector3.UnitY, pose.Orientation), Vector3.UnitY) > .0001f)
        {
            throw new NotSupportedException("Physics characters require an upright placement.");
        }
    }

    public override PhysicsCharacterState3D CreateState(ScenePlacement placement)
    {
        return new(placement.Id, World.Tick, new(placement.Position, 0), false, default, null, null,
            default, default, default);
    }

    public override IDisposable Activate(SceneAgent<PhysicsCharacterState3D> agent)
    {
        var motor = new PhysicsCharacterMotor3D(Options);
        World.AddBody(motor.Body(agent.Id, agent.Placement.Position));
        try
        {
            agent.State = motor.Initialize(World, agent.Id);
        }
        catch
        {
            World.RemoveBody(agent.Id);
            throw;
        }
        return new PhysicsBodyLease3D(World, [agent.Id]);
    }

    /// <summary>Call once after Step. The agent record owns all support and airborne state.</summary>
    public void Move(SceneAgent<PhysicsCharacterState3D> agent, PhysicsCharacterInput3D input)
    {
        if (!ReferenceEquals(agent.Definition, this)) throw new ArgumentException("Character belongs to another definition.");
        agent.State = new PhysicsCharacterMotor3D(Options).Move(World, agent.State, input);
    }

    public override Matrix4x4 WorldTransform(PhysicsCharacterState3D state, ScenePlacement placement)
        => placement.At(state.Motion.Feet);

    /// <summary>Visual origin is at the feet. It never defines collision.</summary>
    public override SceneGroup Present(PhysicsCharacterState3D state) => Visual;
}

public sealed record PhysicsAssemblyState3D(string OccurrenceId, PhysicsSnapshot3D Snapshot);

public sealed record PhysicsAssemblyAgentDefinition3D(IPhysicsWorld3D World, PhysicsAssembly3D Assembly,
    string DefinitionId) : AgentDefinition<PhysicsAssemblyState3D>(AgentTemplate.Object("physics-assembly"))
{
    /// <summary>Visuals keyed by relative compiled body path. Physical interfaces use exposed ports instead.</summary>
    public ImmutableDictionary<string, SceneGroup> Visuals { get; init; } = ImmutableDictionary<string, SceneGroup>.Empty;

    public override string Identity => PhysicsCompositionIdentity3D.Assembly(this);

    public override void ValidatePlacement(ScenePlacement placement)
    {
        PhysicsRigidPlacement3D.From(placement.WorldTransform);
        var bodies = Assembly.Compile().Bodies.Select(body => body.Id).ToHashSet(StringComparer.Ordinal);
        if (Visuals.Keys.Any(key => !bodies.Contains(key)))
        {
            throw new ArgumentException("Assembly visual refers to an unknown body path.");
        }
    }

    public override PhysicsAssemblyState3D CreateState(ScenePlacement placement)
        => new(placement.Id, new(World.Backend, World.Tick, []));

    public override IDisposable Activate(SceneAgent<PhysicsAssemblyState3D> agent)
    {
        PhysicsAssemblyLease3D lease = Assembly.Compile().Mount(World, agent.Id,
            PhysicsRigidPlacement3D.From(agent.Placement.WorldTransform));
        try
        {
            agent.State = new(agent.Id, lease.Capture());
        }
        catch
        {
            lease.Dispose();
            throw;
        }
        return lease;
    }

    public override Matrix4x4 WorldTransform(PhysicsAssemblyState3D state, ScenePlacement placement)
        => Matrix4x4.Identity;

    public override SceneGroup Present(PhysicsAssemblyState3D state)
    {
        var children = new List<SceneNode>();
        foreach (PhysicsBodyState3D body in state.Snapshot.Bodies)
        {
            string path = body.Id[(state.OccurrenceId.Length + 1)..];
            if (!Visuals.TryGetValue(path, out var visual)) continue;
            // Escape '_' first so a flat body name cannot collide with a nested body path.
            string visualId = path.Replace("_", "__", StringComparison.Ordinal).Replace("/", "_s", StringComparison.Ordinal);
            children.Add(Scene.Instance(visualId, visual) with
            {
                Transform = SceneTransform.At(body.Pose.Position) with { Rotation = body.Pose.Orientation },
            });
        }
        return Scene.Group("assembly", children);
    }
}

internal static class PhysicsRigidPlacement3D
{
    public static PhysicsPose3D From(Matrix4x4 matrix)
    {
        SceneTransform.ValidateMatrix(matrix);
        Vector3 x = new(matrix.M11, matrix.M12, matrix.M13);
        Vector3 y = new(matrix.M21, matrix.M22, matrix.M23);
        Vector3 z = new(matrix.M31, matrix.M32, matrix.M33);
        if (MathF.Abs(x.LengthSquared() - 1) > .0001f || MathF.Abs(y.LengthSquared() - 1) > .0001f
            || MathF.Abs(z.LengthSquared() - 1) > .0001f || MathF.Abs(Vector3.Dot(x, y)) > .0001f
            || MathF.Abs(Vector3.Dot(x, z)) > .0001f || MathF.Abs(Vector3.Dot(y, z)) > .0001f)
        {
            throw new NotSupportedException("AUR-PHYSICS-002: Physics agents require rigid placement; author dimensions in the shape and visual.");
        }
        return new(matrix.Translation, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix)));
    }
}

internal static class PhysicsCompositionIdentity3D
{
    public static string Hash(Action<BinaryWriter> write)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        write(writer);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
    }

    public static string Assembly(PhysicsAssemblyAgentDefinition3D definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionId);
        var plan = definition.Assembly.Compile();
        return Hash(writer =>
        {
            writer.Write("assembly/v1");
            writer.Write(definition.DefinitionId);
            writer.Write(plan.DefinitionId);
            writer.Write(plan.Bodies.Length);
            foreach (PhysicsBody3D body in plan.Bodies)
            {
                writer.Write(body.Id);
                var visual = definition.Visuals.GetValueOrDefault(body.Id) ?? Scene.Group("empty", []);
                writer.Write(PhysicsAgentIdentity3D.Compute(new(definition.World, body, visual, definition.DefinitionId)));
            }
            writer.Write(plan.Joints.Length);
            foreach (PhysicsJoint3D joint in plan.Joints)
            {
                writer.Write(joint.Id);
                writer.Write(joint.BodyA);
                writer.Write(joint.BodyB);
                writer.Write(joint.Spring.Frequency);
                writer.Write(joint.Spring.DampingRatio);
                writer.Write(joint.CollideConnected);
                switch (joint)
                {
                    case PhysicsJoint3D.Hinge hinge:
                        writer.Write("hinge");
                        Vector(writer, hinge.AnchorA);
                        Vector(writer, hinge.AnchorB);
                        Vector(writer, hinge.AxisA);
                        Vector(writer, hinge.AxisB);
                        break;
                    case PhysicsJoint3D.AngularMotor motor:
                        writer.Write("motor");
                        Vector(writer, motor.AxisA);
                        writer.Write(motor.TargetVelocity);
                        writer.Write(motor.MaximumTorque);
                        writer.Write(motor.Softness);
                        break;
                    case PhysicsJoint3D.BallSocket socket:
                        writer.Write("spherical");
                        Vector(writer, socket.AnchorA);
                        Vector(writer, socket.AnchorB);
                        break;
                    case PhysicsJoint3D.Fixed weld:
                        writer.Write("fixed");
                        Pose(writer, new(weld.OffsetBInA, weld.OrientationBInA));
                        break;
                    default:
                        throw new NotSupportedException("Unsupported assembly joint identity.");
                }
            }
            writer.Write(plan.Ports.Count);
            foreach (var port in plan.Ports.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.Write(port.Key);
                writer.Write(port.Value.BodyPath);
                Pose(writer, port.Value.LocalFrame);
            }
        });
    }

    private static void Pose(BinaryWriter writer, PhysicsPose3D pose)
    {
        Vector(writer, pose.Position);
        writer.Write(pose.Orientation.X);
        writer.Write(pose.Orientation.Y);
        writer.Write(pose.Orientation.Z);
        writer.Write(pose.Orientation.W);
    }

    private static void Vector(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }
}
