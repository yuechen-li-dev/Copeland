using System.Numerics;

namespace Aurelian.Runtime;

public readonly record struct FirstPersonMotion3D(Vector3 Feet, float Yaw, float Pitch, bool Grounded,
    int Shots, bool Aiming = false, bool Sprinting = false, bool Reloading = false);

public sealed record FirstPersonProfile3D
{
    public float BobMetres { get; init; } = .014f;
    public float RecoilImpulse { get; init; } = 1.8f;
    public float SpringFrequency { get; init; } = 18;
    public float WalkStride { get; init; } = 1.55f;
    public float SprintStride { get; init; } = 2;
    public float AimFovDegrees { get; init; } = 48;
    public float SprintFovDegrees { get; init; } = 66;

    internal void Validate()
    {
        float[] values = [BobMetres, RecoilImpulse, SpringFrequency, WalkStride, SprintStride, AimFovDegrees, SprintFovDegrees];
        if (values.Any(value => !float.IsFinite(value)) || BobMetres is < 0 or > .05f
            || RecoilImpulse is < 0 or > 5 || SpringFrequency is < 5 or > 60
            || WalkStride is < .5f or > 4 || SprintStride is < .5f or > 4
            || AimFovDegrees is < 35 or > 60 || SprintFovDegrees is < 60 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(FirstPersonProfile3D));
        }
    }
}

public readonly record struct FirstPersonPose3D(Vector3 CameraOffset, Vector3 WeaponOffset, Vector3 WeaponAngles,
    float Roll, float FieldOfView, float Aim)
{
    public static FirstPersonPose3D Neutral => new(Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, MathF.PI / 3, 0);

    public Vector3 Eye(Vector3 canonicalEye, Vector3 direction)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
        return canonicalEye + right * CameraOffset.X + Vector3.UnitY * CameraOffset.Y + direction * CameraOffset.Z;
    }

    public Matrix4x4 Camera(Vector3 canonicalEye, Vector3 direction, float aspect)
    {
        Vector3 eye = Eye(canonicalEye, direction);
        Vector3 up = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(direction, Roll));
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, .1f, 80);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(eye, eye + direction, up) * projection;
    }
}

/// <summary>Presentation-only state from accepted motion and shot counters; never feeds an aim ray or simulation.</summary>
public sealed class FirstPersonPresentation3D
{
    private readonly FirstPersonProfile3D profile;
    private FirstPersonMotion3D previous;
    private bool initialized;
    private float phase;
    private float stride;
    private float recoil;
    private float recoilVelocity;
    private float landing;
    private float landingVelocity;
    private float verticalSpeed;
    private float aim;
    private float sprint;
    private float reload;
    private Vector2 sway;
    private float movementBlend;
    public FirstPersonPose3D Pose { get; private set; } = FirstPersonPose3D.Neutral;
    public int Footsteps { get; private set; }
    public int Landings { get; private set; }
    public float LastLandingSpeed { get; private set; }
    public float Recoil => recoil;

    public FirstPersonPresentation3D(FirstPersonProfile3D? profile = null)
    {
        this.profile = profile ?? new();
        this.profile.Validate();
    }

    public void Reset(FirstPersonMotion3D motion)
    {
        Validate(motion);
        previous = motion;
        initialized = true;
        phase = stride = recoil = recoilVelocity = landing = landingVelocity = verticalSpeed = aim = sprint = reload = 0;
        sway = Vector2.Zero;
        movementBlend = 0;
        Footsteps = Landings = 0;
        LastLandingSpeed = 0;
        Pose = FirstPersonPose3D.Neutral;
    }

    public void Step(FirstPersonMotion3D motion, float seconds)
    {
        Validate(motion);
        if (!float.IsFinite(seconds) || seconds <= 0 || seconds > .05f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        if (!initialized || motion.Shots < previous.Shots || Vector3.DistanceSquared(previous.Feet, motion.Feet) > 16)
        {
            Reset(motion);
            return;
        }
        Vector3 displacement = motion.Feet - previous.Feet;
        float distance = new Vector2(displacement.X, displacement.Z).Length();
        float speed = distance / seconds;
        float blend = 1 - MathF.Exp(-12 * seconds);
        aim += ((motion.Aiming ? 1 : 0) - aim) * blend;
        sprint += ((motion.Sprinting && motion.Grounded && speed > 1 && !motion.Aiming ? 1 : 0) - sprint) * blend;
        reload += ((motion.Reloading ? 1 : 0) - reload) * blend;
        float strideLength = profile.WalkStride + (profile.SprintStride - profile.WalkStride) * sprint;
        if (motion.Grounded && previous.Grounded)
        {
            stride += distance;
            phase = (phase + distance / strideLength * MathF.PI) % MathF.Tau;
            while (stride >= strideLength)
            {
                stride -= strideLength;
                Footsteps++;
            }
        }
        if (motion.Grounded && !previous.Grounded)
        {
            LastLandingSpeed = Math.Max(0, -verticalSpeed);
            landingVelocity += Math.Min(LastLandingSpeed, 12) * .22f;
            Landings++;
        }
        verticalSpeed = displacement.Y / seconds;
        int shots = Math.Min(4, motion.Shots - previous.Shots);
        recoilVelocity += shots * profile.RecoilImpulse;
        Spring(ref recoil, ref recoilVelocity, profile.SpringFrequency, seconds);
        Spring(ref landing, ref landingVelocity, 16, seconds);
        float yawDelta = MathF.IEEERemainder(motion.Yaw - previous.Yaw, MathF.Tau);
        Vector2 targetSway = new(Math.Clamp(-yawDelta / seconds * .01f, -.04f, .04f),
            Math.Clamp((motion.Pitch - previous.Pitch) / seconds * .01f, -.04f, .04f));
        sway = Vector2.Lerp(sway, targetSway, blend);
        movementBlend += (Math.Min(1, speed / 4) * (motion.Grounded ? 1 : 0) - movementBlend) * blend;
        float movement = movementBlend;
        float bob = MathF.Sin(phase * 2) * profile.BobMetres * movement * (1 - aim * .8f);
        Vector3 weapon = new(sway.X + MathF.Sin(phase) * .012f * movement, sway.Y + bob - landing * .3f,
            -recoil - reload * .18f);
        Vector3 angles = new(recoil * 1.8f + reload * .45f, sway.X * .5f, -reload * .45f);
        float fov = 60 + (profile.AimFovDegrees - 60) * aim + (profile.SprintFovDegrees - 60) * sprint * (1 - aim);
        Pose = new(new(0, bob * .45f - Math.Min(landing, .06f), 0), weapon, angles,
            MathF.Sin(phase) * .006f * movement * (1 - aim), fov * MathF.PI / 180, aim);
        previous = motion;
    }

    private static void Spring(ref float position, ref float velocity, float frequency, float seconds)
    {
        float impulse = velocity + frequency * position;
        float decay = MathF.Exp(-frequency * seconds);
        position = (position + impulse * seconds) * decay;
        velocity = (velocity - frequency * impulse * seconds) * decay;
    }

    private static void Validate(FirstPersonMotion3D motion)
    {
        if (!float.IsFinite(motion.Feet.X) || !float.IsFinite(motion.Feet.Y) || !float.IsFinite(motion.Feet.Z)
            || !float.IsFinite(motion.Yaw) || !float.IsFinite(motion.Pitch) || motion.Shots < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(motion));
        }
    }
}
