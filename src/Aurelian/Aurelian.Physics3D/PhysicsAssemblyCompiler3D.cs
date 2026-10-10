using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Physics3D;

internal static class PhysicsAssemblyCompiler3D
{
    private sealed record Member(string Id, PhysicsPose3D Placement, ImmutableArray<PhysicsBody3D> Bodies,
        ImmutableArray<PhysicsJoint3D> Joints, ImmutableDictionary<string, PhysicsPortBinding3D> Ports,
        ImmutableArray<PhysicsConnectionBinding3D> Connections);

    public static CompiledPhysicsAssembly3D Compile(PhysicsAssembly3D definition)
    {
        var cache = new Dictionary<PhysicsAssembly3D, CompiledPhysicsAssembly3D>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<PhysicsAssembly3D>(ReferenceEqualityComparer.Instance);
        return CompileDefinition(definition, cache, active);
    }

    private static CompiledPhysicsAssembly3D CompileDefinition(PhysicsAssembly3D definition,
        Dictionary<PhysicsAssembly3D, CompiledPhysicsAssembly3D> cache, HashSet<PhysicsAssembly3D> active)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (cache.TryGetValue(definition, out var cached)) return cached;
        Segment(definition.Id);
        if (!active.Add(definition))
        {
            throw new ArgumentException($"AUR-ASSEMBLY-003: Recursive definition '{definition.Id}'.");
        }
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (PhysicsPart3D part in definition.Parts)
        {
            part.Body.Validate();
            Segment(part.Body.Id);
            foreach (var port in part.Ports)
            {
                Segment(port.Key);
                port.Value.Validate();
            }
            // A body's authored pose places the member. Its attachment frames remain body-local.
            var localBody = part.Body with { Pose = PhysicsPose3D.At(Vector3.Zero) };
            var ports = part.Ports.ToImmutableDictionary(pair => pair.Key,
                pair => new PhysicsPortBinding3D(part.Body.Id, pair.Value), StringComparer.Ordinal);
            AddMember(members, new(part.Body.Id, part.Body.Pose, [localBody], [], ports, []));
        }
        foreach (PhysicsAssemblyOccurrence3D child in definition.Children)
        {
            Segment(child.Id);
            child.Placement.Validate();
            var plan = CompileDefinition(child.Definition, cache, active);
            AddMember(members, new(child.Id, child.Placement, plan.Bodies, plan.Joints, plan.Ports, plan.Connections));
        }
        var incoming = new Dictionary<string, PhysicsConnection3D>(StringComparer.Ordinal);
        var interfaceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PhysicsConnection3D connection in definition.Interfaces)
        {
            Segment(connection.Id);
            if (!interfaceIds.Add(connection.Id))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-004: Duplicate interface '{connection.Id}'.");
            }
            Resolve(members, connection.A);
            Resolve(members, connection.B);
            if (connection.A.Member == connection.B.Member || !incoming.TryAdd(connection.B.Member, connection))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-005: '{connection.B.Member}' has a self-connection or multiple placement drivers.");
            }
        }
        var placements = new Dictionary<string, PhysicsPose3D>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        PhysicsPose3D Place(string id)
        {
            if (placements.TryGetValue(id, out var pose)) return pose;
            if (!visiting.Add(id))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-006: Placement cycle at '{id}'.");
            }
            Member member = members[id];
            if (!incoming.TryGetValue(id, out var connection))
            {
                pose = member.Placement;
            }
            else
            {
                var a = Resolve(members, connection.A);
                var b = Resolve(members, connection.B);
                PhysicsPose3D frameA = Compose(FrameInMember(members[connection.A.Member], a), Place(connection.A.Member));
                Matrix4x4.Invert(FrameInMember(member, b).Matrix, out var inverseB);
                pose = FromMatrix(inverseB * frameA.Matrix);
            }
            visiting.Remove(id);
            placements.Add(id, pose);
            return pose;
        }
        foreach (string id in members.Keys.Order(StringComparer.Ordinal)) Place(id);
        var bodies = ImmutableArray.CreateBuilder<PhysicsBody3D>();
        var joints = ImmutableArray.CreateBuilder<PhysicsJoint3D>();
        var connections = ImmutableArray.CreateBuilder<PhysicsConnectionBinding3D>();
        foreach (Member member in members.Values.OrderBy(member => member.Id, StringComparer.Ordinal))
        {
            string Path(string id) => member.Id + "/" + id;
            bool isPart = definition.Parts.Any(part => part.Body.Id == member.Id);
            string BodyPath(string id) => isPart ? id : Path(id);
            foreach (PhysicsBody3D body in member.Bodies)
            {
                bodies.Add(body with
                {
                    Id = BodyPath(body.Id),
                    Pose = Compose(body.Pose, placements[member.Id]),
                    Velocity = new(Vector3.Transform(body.Velocity.Linear, placements[member.Id].Orientation),
                        Vector3.Transform(body.Velocity.Angular, placements[member.Id].Orientation)),
                });
            }
            foreach (PhysicsJoint3D joint in member.Joints)
            {
                joints.Add(Remap(joint, Path, BodyPath));
            }
            foreach (PhysicsConnectionBinding3D connection in member.Connections)
            {
                connections.Add(connection with
                {
                    Id = Path(connection.Id),
                    SourceA = connection.SourceA with { BodyPath = BodyPath(connection.SourceA.BodyPath) },
                    SourceB = connection.SourceB with { BodyPath = BodyPath(connection.SourceB.BodyPath) },
                });
            }
        }
        PhysicsPortBinding3D Global(PhysicsEndpoint3D endpoint)
        {
            var binding = Resolve(members, endpoint);
            bool isPart = definition.Parts.Any(part => part.Body.Id == endpoint.Member);
            return binding with { BodyPath = isPart ? binding.BodyPath : endpoint.Member + "/" + binding.BodyPath };
        }
        var bodyMap = bodies.ToDictionary(body => body.Id, StringComparer.Ordinal);
        foreach (PhysicsConnection3D connection in definition.Interfaces)
        {
            var a = Global(connection.A);
            var b = Global(connection.B);
            joints.AddRange(connection.Lower(a, b, bodyMap[a.BodyPath].Pose, bodyMap[b.BodyPath].Pose));
            connections.Add(new(connection.Id, connection.A, connection.B, a, b));
        }
        var exposed = ImmutableDictionary.CreateBuilder<string, PhysicsPortBinding3D>(StringComparer.Ordinal);
        foreach (var alias in definition.Expose)
        {
            Segment(alias.Key);
            exposed.Add(alias.Key, Global(alias.Value));
        }
        var result = new CompiledPhysicsAssembly3D(definition.Id, bodies.ToImmutable(), joints.ToImmutable(),
            exposed.ToImmutable(), connections.ToImmutable());
        ValidatePlan(result.Bodies, result.Joints);
        active.Remove(definition);
        cache.Add(definition, result);
        return result;
    }

    private static PhysicsPortBinding3D Resolve(Dictionary<string, Member> members, PhysicsEndpoint3D endpoint)
    {
        Segment(endpoint.Member);
        Segment(endpoint.Port);
        if (!members.TryGetValue(endpoint.Member, out var member)
            || !member.Ports.TryGetValue(endpoint.Port, out var binding))
        {
            throw new ArgumentException($"AUR-ASSEMBLY-007: Unknown or private port '{endpoint.Member}.{endpoint.Port}'; expose a named child port.");
        }
        return binding;
    }

    private static PhysicsPose3D FrameInMember(Member member, PhysicsPortBinding3D port)
    {
        PhysicsBody3D body = member.Bodies.Single(body => body.Id == port.BodyPath);
        return Compose(port.LocalFrame, body.Pose);
    }

    private static void AddMember(Dictionary<string, Member> members, Member member)
    {
        if (!members.TryAdd(member.Id, member))
        {
            throw new ArgumentException($"AUR-ASSEMBLY-004: Duplicate member '{member.Id}'.");
        }
    }

    private static void Segment(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException($"AUR-ASSEMBLY-008: '{id}' must use ASCII letters, digits, hyphens or underscores, not an internal path.");
        }
    }

    internal static void ValidatePlan(ImmutableArray<PhysicsBody3D> bodies, ImmutableArray<PhysicsJoint3D> joints)
    {
        var bodyMap = new Dictionary<string, PhysicsBody3D>(StringComparer.Ordinal);
        foreach (PhysicsBody3D body in bodies)
        {
            body.Validate();
            if (!bodyMap.TryAdd(body.Id, body)) throw new ArgumentException($"Duplicate body '{body.Id}'.");
        }
        var jointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PhysicsJoint3D joint in joints)
        {
            joint.Validate();
            if (!jointIds.Add(joint.Id) || !bodyMap.TryGetValue(joint.BodyA, out var a)
                || !bodyMap.TryGetValue(joint.BodyB, out var b))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-009: Duplicate joint or unresolved body in '{joint.Id}'.");
            }
            if (a.MotionType == PhysicsMotionType3D.Static || b.MotionType == PhysicsMotionType3D.Static
                || (a.MotionType != PhysicsMotionType3D.Dynamic && b.MotionType != PhysicsMotionType3D.Dynamic))
            {
                throw new ArgumentException($"AUR-ASSEMBLY-010: '{joint.Id}' needs two mobile bodies and at least one dynamic body; use a kinematic anchor.");
            }
        }
    }

    internal static PhysicsJoint3D Remap(PhysicsJoint3D joint, Func<string, string> jointPath, Func<string, string> bodyPath)
    {
        string id = jointPath(joint.Id);
        string a = bodyPath(joint.BodyA);
        string b = bodyPath(joint.BodyB);
        PhysicsJoint3D mapped = joint switch
        {
            PhysicsJoint3D.BallSocket socket => new PhysicsJoint3D.BallSocket(id, a, b, socket.AnchorA, socket.AnchorB),
            PhysicsJoint3D.DistanceLimit distance => new PhysicsJoint3D.DistanceLimit(id, a, b, distance.Minimum, distance.Maximum),
            PhysicsJoint3D.Hinge hinge => new PhysicsJoint3D.Hinge(id, a, b, hinge.AnchorA, hinge.AnchorB, hinge.AxisA, hinge.AxisB),
            PhysicsJoint3D.AngularMotor motor => new PhysicsJoint3D.AngularMotor(id, a, b, motor.AxisA,
                motor.TargetVelocity, motor.MaximumTorque, motor.Softness),
            PhysicsJoint3D.Fixed weld => new PhysicsJoint3D.Fixed(id, a, b, weld.OffsetBInA, weld.OrientationBInA),
            _ => throw new NotSupportedException("Unknown physics joint."),
        };
        return mapped with { Spring = joint.Spring, CollideConnected = joint.CollideConnected };
    }

    internal static PhysicsPose3D Compose(PhysicsPose3D local, PhysicsPose3D parent) => FromMatrix(local.Matrix * parent.Matrix);

    internal static PhysicsPose3D Relative(PhysicsPose3D world, PhysicsPose3D parent)
    {
        Matrix4x4.Invert(parent.Matrix, out var inverse);
        return FromMatrix(world.Matrix * inverse);
    }

    private static PhysicsPose3D FromMatrix(Matrix4x4 matrix) => new(matrix.Translation,
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix)));
}
