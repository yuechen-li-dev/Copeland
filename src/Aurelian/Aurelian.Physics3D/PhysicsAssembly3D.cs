using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Physics3D;

/// <summary>A direct member and its named frame. Child internals are accessible only through Expose.</summary>
public readonly record struct PhysicsEndpoint3D(string Member, string Port);

public sealed record PhysicsPart3D(PhysicsBody3D Body)
{
    public ImmutableDictionary<string, PhysicsPose3D> Ports { get; init; }
        = ImmutableDictionary<string, PhysicsPose3D>.Empty;
}

public sealed record PhysicsAssemblyOccurrence3D(string Id, PhysicsAssembly3D Definition, PhysicsPose3D Placement);

/// <summary>Immutable authoring data. Containment never creates a joint.</summary>
public sealed record PhysicsAssembly3D(string Id)
{
    public ImmutableArray<PhysicsPart3D> Parts { get; init; } = [];
    public ImmutableArray<PhysicsAssemblyOccurrence3D> Children { get; init; } = [];
    public ImmutableArray<PhysicsConnection3D> Interfaces { get; init; } = [];
    public ImmutableDictionary<string, PhysicsEndpoint3D> Expose { get; init; }
        = ImmutableDictionary<string, PhysicsEndpoint3D>.Empty;

    public CompiledPhysicsAssembly3D Compile() => PhysicsAssemblyCompiler3D.Compile(this);
}

/// <summary>Positive velocity rotates B relative to A around the port's local Z axis.</summary>
public readonly record struct PhysicsAngularDrive3D(float TargetVelocity, float MaximumTorque, float Softness = .0001f);

public readonly record struct PhysicsPortBinding3D(string BodyPath, PhysicsPose3D LocalFrame);
public sealed record PhysicsConnectionBinding3D(string Id, PhysicsEndpoint3D A, PhysicsEndpoint3D B,
    PhysicsPortBinding3D SourceA, PhysicsPortBinding3D SourceB);

public abstract record PhysicsConnection3D(string Id, PhysicsEndpoint3D A, PhysicsEndpoint3D B)
{
    public PhysicsSpring3D Spring { get; init; } = new();
    public bool CollideConnected { get; init; }
    public PhysicsAngularDrive3D? Drive { get; init; }
    internal abstract ImmutableArray<PhysicsJoint3D> Lower(PhysicsPortBinding3D a, PhysicsPortBinding3D b,
        PhysicsPose3D poseA, PhysicsPose3D poseB);
}

/// <summary>Closed, explicitly implemented joint concepts; no runtime type discovery.</summary>
public interface IPhysicsInterfaceKind3D
{
    static abstract ImmutableArray<PhysicsJoint3D> Lower(PhysicsConnection3D connection,
        PhysicsPortBinding3D a, PhysicsPortBinding3D b, PhysicsPose3D poseA, PhysicsPose3D poseB);
}

public sealed record PhysicsInterface3D<T> : PhysicsConnection3D where T : IPhysicsInterfaceKind3D
{
    public PhysicsInterface3D(string id, PhysicsEndpoint3D a, PhysicsEndpoint3D b) : base(id, a, b) { }

    internal override ImmutableArray<PhysicsJoint3D> Lower(PhysicsPortBinding3D a, PhysicsPortBinding3D b,
        PhysicsPose3D poseA, PhysicsPose3D poseB) => T.Lower(this, a, b, poseA, poseB);
}

public readonly struct Revolute3D : IPhysicsInterfaceKind3D
{
    public static ImmutableArray<PhysicsJoint3D> Lower(PhysicsConnection3D connection,
        PhysicsPortBinding3D a, PhysicsPortBinding3D b, PhysicsPose3D poseA, PhysicsPose3D poseB)
    {
        Vector3 axisA = Vector3.Transform(Vector3.UnitZ, a.LocalFrame.Orientation);
        Vector3 axisB = Vector3.Transform(Vector3.UnitZ, b.LocalFrame.Orientation);
        PhysicsJoint3D hinge = new PhysicsJoint3D.Hinge(connection.Id, a.BodyPath, b.BodyPath,
            a.LocalFrame.Position, b.LocalFrame.Position, axisA, axisB)
        {
            Spring = connection.Spring,
            CollideConnected = connection.CollideConnected,
        };
        if (connection.Drive is not { } drive)
        {
            return [hinge];
        }
        // BEPU's raw motor measures A minus B. The authored interface measures B relative to A.
        PhysicsJoint3D motor = new PhysicsJoint3D.AngularMotor(connection.Id + "/drive", a.BodyPath, b.BodyPath,
            axisA, -drive.TargetVelocity, drive.MaximumTorque, drive.Softness)
        {
            Spring = connection.Spring,
            CollideConnected = connection.CollideConnected,
        };
        return [hinge, motor];
    }
}

public readonly struct Spherical3D : IPhysicsInterfaceKind3D
{
    public static ImmutableArray<PhysicsJoint3D> Lower(PhysicsConnection3D connection,
        PhysicsPortBinding3D a, PhysicsPortBinding3D b, PhysicsPose3D poseA, PhysicsPose3D poseB)
    {
        RejectDrive(connection);
        return [new PhysicsJoint3D.BallSocket(connection.Id, a.BodyPath, b.BodyPath,
            a.LocalFrame.Position, b.LocalFrame.Position)
        {
            Spring = connection.Spring,
            CollideConnected = connection.CollideConnected,
        }];
    }

    internal static void RejectDrive(PhysicsConnection3D connection)
    {
        if (connection.Drive is not null)
        {
            throw new ArgumentException($"AUR-ASSEMBLY-001: '{connection.Id}' needs a revolute interface to use an angular drive.");
        }
    }
}

public readonly struct Fixed3D : IPhysicsInterfaceKind3D
{
    public static ImmutableArray<PhysicsJoint3D> Lower(PhysicsConnection3D connection,
        PhysicsPortBinding3D a, PhysicsPortBinding3D b, PhysicsPose3D poseA, PhysicsPose3D poseB)
    {
        Spherical3D.RejectDrive(connection);
        PhysicsPose3D relative = PhysicsAssemblyCompiler3D.Relative(poseB, poseA);
        return [new PhysicsJoint3D.Fixed(connection.Id, a.BodyPath, b.BodyPath, relative.Position, relative.Orientation)
        {
            Spring = connection.Spring,
            CollideConnected = connection.CollideConnected,
        }];
    }
}

public sealed record CompiledPhysicsAssembly3D(string DefinitionId, ImmutableArray<PhysicsBody3D> Bodies,
    ImmutableArray<PhysicsJoint3D> Joints, ImmutableDictionary<string, PhysicsPortBinding3D> Ports,
    ImmutableArray<PhysicsConnectionBinding3D> Connections)
{
    public PhysicsAssemblyLease3D Mount(IPhysicsWorld3D world, string occurrenceId, PhysicsPose3D placement)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceId);
        placement.Validate();
        string Prefix(string id) => occurrenceId + "/" + id;
        var bodies = Bodies.Select(body => body with
        {
            Id = Prefix(body.Id),
            SemanticOwnerId = occurrenceId,
            Pose = PhysicsAssemblyCompiler3D.Compose(body.Pose, placement),
            Velocity = new(Vector3.Transform(body.Velocity.Linear, placement.Orientation),
                Vector3.Transform(body.Velocity.Angular, placement.Orientation)),
        }).ToImmutableArray();
        var joints = Joints.Select(joint => PhysicsAssemblyCompiler3D.Remap(joint, Prefix, Prefix)).ToImmutableArray();
        PhysicsAssemblyCompiler3D.ValidatePlan(bodies, joints);
        foreach (var port in Ports.Values)
        {
            port.LocalFrame.Validate();
            if (!Bodies.Any(body => body.Id == port.BodyPath))
            {
                throw new ArgumentException($"Exposed port refers to unknown body '{port.BodyPath}'.");
            }
        }
        var existingJoints = world.CaptureJoints().Select(joint => joint.Id).ToHashSet(StringComparer.Ordinal);
        foreach (PhysicsBody3D body in bodies)
        {
            if (world.TryGetBody(body.Id, out _))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-002: Body occurrence '{body.Id}' already exists.");
            }
        }
        if (joints.Any(joint => existingJoints.Contains(joint.Id)))
        {
            throw new ArgumentException("AUR-ASSEMBLY-002: A joint occurrence already exists.");
        }
        var addedBodies = new List<string>();
        var addedJoints = new List<string>();
        try
        {
            foreach (PhysicsBody3D body in bodies)
            {
                world.AddBody(body);
                addedBodies.Add(body.Id);
            }
            foreach (PhysicsJoint3D joint in joints)
            {
                world.AddJoint(joint);
                addedJoints.Add(joint.Id);
            }
        }
        catch
        {
            foreach (string id in addedJoints) world.RemoveJoint(id);
            foreach (string id in addedBodies) world.RemoveBody(id);
            throw;
        }
        var ports = Ports.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { BodyPath = Prefix(pair.Value.BodyPath) }, StringComparer.Ordinal);
        return new(world, occurrenceId, bodies.Select(body => body.Id).ToImmutableArray(),
            joints.Select(joint => joint.Id).ToImmutableArray(), ports);
    }
}

/// <summary>Owns only this occurrence. Captures are ordinary data; no backend handles escape.</summary>
public sealed class PhysicsAssemblyLease3D : IDisposable
{
    private readonly IPhysicsWorld3D world;
    private bool disposed;
    public string Id { get; }
    public ImmutableArray<string> BodyIds { get; }
    public ImmutableArray<string> JointIds { get; }
    public ImmutableDictionary<string, PhysicsPortBinding3D> Ports { get; }

    internal PhysicsAssemblyLease3D(IPhysicsWorld3D world, string id, ImmutableArray<string> bodies,
        ImmutableArray<string> joints, ImmutableDictionary<string, PhysicsPortBinding3D> ports)
    {
        this.world = world;
        Id = id;
        BodyIds = bodies;
        JointIds = joints;
        Ports = ports;
    }

    public PhysicsSnapshot3D Capture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(world.Backend, world.Tick, BodyIds.Select(world.GetBody).ToImmutableArray());
    }

    public PhysicsPose3D PortPose(string alias)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        PhysicsPortBinding3D binding = Ports[alias];
        return PhysicsAssemblyCompiler3D.Compose(binding.LocalFrame, world.GetBody(binding.BodyPath).Pose);
    }

    public void Dispose()
    {
        if (disposed) return;
        foreach (string id in JointIds) world.RemoveJoint(id);
        foreach (string id in BodyIds) world.RemoveBody(id);
        disposed = true;
    }
}
