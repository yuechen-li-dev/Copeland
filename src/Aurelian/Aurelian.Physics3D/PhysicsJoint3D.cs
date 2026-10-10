using System.Numerics;

namespace Aurelian.Physics3D;

public readonly record struct PhysicsSpring3D(float Frequency = 30, float DampingRatio = 1)
{
    public PhysicsSpring3D() : this(30, 1) { }

    public void Validate()
    {
        PhysicsValidation3D.Positive(Frequency, nameof(Frequency));
        if (!float.IsFinite(DampingRatio) || DampingRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DampingRatio));
        }
    }
}

/// <summary>Anchors and axes are body-local. Static anchors are authored as kinematic bodies.</summary>
public abstract record PhysicsJoint3D(string Id, string BodyA, string BodyB)
{
    public PhysicsSpring3D Spring { get; init; } = new();
    public bool CollideConnected { get; init; }

    public sealed record BallSocket(string JointId, string A, string B, Vector3 AnchorA, Vector3 AnchorB)
        : PhysicsJoint3D(JointId, A, B);
    public sealed record DistanceLimit(string JointId, string A, string B, float Minimum, float Maximum)
        : PhysicsJoint3D(JointId, A, B);
    public sealed record Hinge(string JointId, string A, string B, Vector3 AnchorA, Vector3 AnchorB,
        Vector3 AxisA, Vector3 AxisB) : PhysicsJoint3D(JointId, A, B);
    public sealed record AngularMotor(string JointId, string A, string B, Vector3 AxisA,
        float TargetVelocity, float MaximumTorque, float Softness = .0001f) : PhysicsJoint3D(JointId, A, B);
    public sealed record Fixed(string JointId, string A, string B, Vector3 OffsetBInA,
        Quaternion OrientationBInA) : PhysicsJoint3D(JointId, A, B);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(BodyA);
        ArgumentException.ThrowIfNullOrWhiteSpace(BodyB);
        if (BodyA == BodyB)
        {
            throw new ArgumentException("A joint requires two different bodies.");
        }
        Spring.Validate();
        switch (this)
        {
            case Fixed weld:
                new PhysicsPose3D(weld.OffsetBInA, weld.OrientationBInA).Validate();
                break;
            case BallSocket socket:
                PhysicsValidation3D.Finite(socket.AnchorA);
                PhysicsValidation3D.Finite(socket.AnchorB);
                break;
            case DistanceLimit distance:
                PhysicsValidation3D.Positive(distance.Maximum, nameof(distance.Maximum));
                if (!float.IsFinite(distance.Minimum) || distance.Minimum < 0 || distance.Minimum > distance.Maximum)
                {
                    throw new ArgumentException("Distance limits require 0 <= minimum <= maximum.");
                }
                break;
            case Hinge hinge:
                PhysicsValidation3D.Finite(hinge.AnchorA);
                PhysicsValidation3D.Finite(hinge.AnchorB);
                UnitAxis(hinge.AxisA);
                UnitAxis(hinge.AxisB);
                break;
            case AngularMotor motor:
                UnitAxis(motor.AxisA);
                PhysicsValidation3D.Positive(motor.MaximumTorque, nameof(motor.MaximumTorque));
                if (!float.IsFinite(motor.TargetVelocity) || !float.IsFinite(motor.Softness) || motor.Softness < 0)
                {
                    throw new ArgumentException("Motor target velocity and nonnegative softness must be finite.");
                }
                break;
            default:
                throw new NotSupportedException("Unsupported physics joint.");
        }
    }

    private static void UnitAxis(Vector3 axis)
    {
        PhysicsValidation3D.Finite(axis);
        if (MathF.Abs(axis.LengthSquared() - 1) > .0001f)
        {
            throw new ArgumentException("Joint axes must be unit vectors.");
        }
    }
}
