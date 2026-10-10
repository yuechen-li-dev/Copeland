using System.Collections.Immutable;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;

namespace Aurelian.Physics3D.Bepu;

public sealed partial class BepuPhysicsWorld3D
{
    private sealed record JointEntry(PhysicsJoint3D Description, ConstraintHandle Handle);
    private readonly Dictionary<string, JointEntry> joints = new(StringComparer.Ordinal);
    private readonly HashSet<PhysicsContactPair3D> excludedPairs = new();
    private readonly Dictionary<PhysicsContactPair3D, int> jointExclusions = new();

    public int JointCount => joints.Count;

    public void AddJoint(PhysicsJoint3D joint)
    {
        RequireLive();
        ArgumentNullException.ThrowIfNull(joint);
        joint.Validate();
        if (joints.ContainsKey(joint.Id))
        {
            throw new ArgumentException($"Physics joint '{joint.Id}' already exists.");
        }
        Entry first = RequireMobile(joint.BodyA);
        Entry second = RequireMobile(joint.BodyB);
        if (first.Description.MotionType != PhysicsMotionType3D.Dynamic
            && second.Description.MotionType != PhysicsMotionType3D.Dynamic)
        {
            throw new ArgumentException("A joint requires at least one dynamic body.");
        }
        var a = new BodyHandle(first.Handle);
        var b = new BodyHandle(second.Handle);
        BodyReference referenceA = simulation.Bodies.GetBodyReference(a);
        BodyReference referenceB = simulation.Bodies.GetBodyReference(b);
        referenceA.Awake = true;
        referenceB.Awake = true;
        var spring = new SpringSettings(joint.Spring.Frequency, joint.Spring.DampingRatio);
        ConstraintHandle handle;
        switch (joint)
        {
            case PhysicsJoint3D.BallSocket socket:
                handle = simulation.Solver.Add(a, b, new BallSocket
                {
                    LocalOffsetA = socket.AnchorA,
                    LocalOffsetB = socket.AnchorB,
                    SpringSettings = spring,
                });
                break;
            case PhysicsJoint3D.DistanceLimit distance:
                handle = simulation.Solver.Add(a, b, new CenterDistanceLimit
                {
                    MinimumDistance = distance.Minimum,
                    MaximumDistance = distance.Maximum,
                    SpringSettings = spring,
                });
                break;
            case PhysicsJoint3D.Hinge hinge:
                handle = simulation.Solver.Add(a, b, new Hinge
                {
                    LocalOffsetA = hinge.AnchorA,
                    LocalOffsetB = hinge.AnchorB,
                    LocalHingeAxisA = hinge.AxisA,
                    LocalHingeAxisB = hinge.AxisB,
                    SpringSettings = spring,
                });
                break;
            case PhysicsJoint3D.AngularMotor motor:
                handle = simulation.Solver.Add(a, b, new AngularAxisMotor
                {
                    LocalAxisA = motor.AxisA,
                    TargetVelocity = motor.TargetVelocity,
                    Settings = new MotorSettings(motor.MaximumTorque, motor.Softness),
                });
                break;
            default:
                throw new NotSupportedException("Unsupported physics joint.");
        }
        joints.Add(joint.Id, new(joint, handle));
        if (!joint.CollideConnected)
        {
            PhysicsContactPair3D pair = CanonicalPair(joint.BodyA, joint.BodyB);
            jointExclusions[pair] = jointExclusions.GetValueOrDefault(pair) + 1;
        }
    }

    public bool RemoveJoint(string id)
    {
        RequireLive();
        if (!joints.Remove(id, out JointEntry? joint))
        {
            return false;
        }
        simulation.Solver.Remove(joint.Handle);
        if (!joint.Description.CollideConnected)
        {
            PhysicsContactPair3D pair = CanonicalPair(joint.Description.BodyA, joint.Description.BodyB);
            int count = jointExclusions[pair] - 1;
            if (count == 0)
            {
                jointExclusions.Remove(pair);
            }
            else
            {
                jointExclusions[pair] = count;
            }
        }
        return true;
    }

    public ImmutableArray<PhysicsJoint3D> CaptureJoints()
    {
        RequireLive();
        return joints.Values.Select(joint => joint.Description).OrderBy(joint => joint.Id,
            StringComparer.Ordinal).ToImmutableArray();
    }

    public void SetCollisionEnabled(string bodyA, string bodyB, bool enabled)
    {
        RequireLive();
        if (bodyA == bodyB || !entries.ContainsKey(bodyA) || !entries.ContainsKey(bodyB))
        {
            throw new ArgumentException("Collision exclusions require two different existing bodies.");
        }
        PhysicsContactPair3D pair = CanonicalPair(bodyA, bodyB);
        if (enabled)
        {
            excludedPairs.Remove(pair);
        }
        else
        {
            excludedPairs.Add(pair);
        }
        // Invalidate sleeping islands so contact changes are observed on the next step.
        foreach (string id in new[] { bodyA, bodyB })
        {
            Entry entry = entries[id];
            if (entry.Description.MotionType != PhysicsMotionType3D.Static)
            {
                BodyReference reference = simulation.Bodies.GetBodyReference(new BodyHandle(entry.Handle));
                reference.Awake = true;
            }
        }
    }

    private static PhysicsContactPair3D CanonicalPair(string first, string second)
    {
        if (StringComparer.Ordinal.Compare(first, second) <= 0)
        {
            return new(first, second);
        }
        return new(second, first);
    }

    private static ContinuousDetection Continuity(PhysicsBody3D body)
    {
        return body.Continuity switch
        {
            PhysicsContinuity3D.Discrete => ContinuousDetection.Discrete,
            PhysicsContinuity3D.Passive => ContinuousDetection.Passive,
            PhysicsContinuity3D.Continuous => ContinuousDetection.Continuous(body.MinimumSweepSeconds, body.SweepConvergenceSeconds),
            _ => throw new ArgumentException("Unsupported continuity mode."),
        };
    }
}
