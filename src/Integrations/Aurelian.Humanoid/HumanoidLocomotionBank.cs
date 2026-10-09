using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aetheris.Humanoid;

namespace Aurelian.Humanoid;

public enum HumanoidGait { Idle, Walk, Run }
public sealed record HumanoidLocomotionFrame(double Seconds, ImmutableArray<JointPose> Rotations,
    Vector3 OffsetMm, float RootDistance, bool LeftContact, bool RightContact);
public sealed record HumanoidLocomotionClip(HumanoidGait Gait, string Id, double Duration,
    ImmutableArray<HumanoidLocomotionFrame> Frames, string SourceSha256, double SourceUnitsToMm,
    double SourcePoseSeamDegrees, double ForwardTravelMetres)
{
    public float NominalSpeed => Gait == HumanoidGait.Idle ? 0 : Frames[^1].RootDistance / (float)Duration;

    public HumanoidLocomotionFrame Sample(double seconds)
    {
        double cycles = Math.Floor(seconds / Duration);
        double phase = seconds - cycles * Duration;
        int low = 1;
        int high = Frames.Length - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Frames[middle].Seconds < phase) low = middle + 1;
            else high = middle;
        }
        var left = Frames[low - 1];
        var right = Frames[low];
        float amount = (float)((phase - left.Seconds) / (right.Seconds - left.Seconds));
        return new(phase, BlendRotations(left.Rotations, right.Rotations, amount),
            Vector3.Lerp(left.OffsetMm, right.OffsetMm, amount),
            left.RootDistance + (right.RootDistance - left.RootDistance) * amount + (float)cycles * Frames[^1].RootDistance,
            amount < .5f ? left.LeftContact : right.LeftContact, amount < .5f ? left.RightContact : right.RightContact);
    }

    internal static ImmutableArray<JointPose> BlendRotations(ImmutableArray<JointPose> from,
        ImmutableArray<JointPose> to, float amount)
    {
        var result = ImmutableArray.CreateBuilder<JointPose>(to.Length);
        for (int index = 0; index < to.Length; index++)
        {
            if (from[index].Joint != to[index].Joint) throw new InvalidDataException("Locomotion rotation channels disagree.");
            result.Add(new(to[index].Joint, Quaternion.Normalize(Quaternion.Slerp(from[index].LocalRotation, to[index].LocalRotation, amount))));
        }
        return result.ToImmutable();
    }
}
public sealed record HumanoidLocomotionArtifact(string Schema, string BodySha256, string SkeletonId,
    string RestPoseId, ImmutableArray<HumanoidLocomotionClip> Clips);
public sealed record HumanoidFootPlant(bool Locked, Vector3 AnkleWorld, Vector3 NormalWorld,
    float Weight, float ResidualMm, bool ReachClamped)
{
    public static HumanoidFootPlant Released { get; } = new(false, Vector3.Zero, Vector3.UnitY, 0, 0, false);
}
public sealed record HumanoidLocomotionState(HumanoidGait Gait, double Seconds, float BlendSeconds,
    ImmutableArray<JointPose> BlendFrom, Vector3 BlendOffsetMm, ImmutableArray<JointPose> Rotations, Vector3 OffsetMm,
    HumanoidFootPlant Left, HumanoidFootPlant Right, Vector3 RequestedDisplacement, Vector3 AcceptedDisplacement,
    float PelvisAdjustmentMm, bool Grounded, string? Diagnostic, float AimWeight = 0);
public sealed record HumanoidRootMotionRequest(HumanoidGait Gait, double StartSeconds, double PhaseDelta,
    Vector3 Displacement);

/// <summary>Explicit, body-bound clips baked offline. No runtime FBX discovery or retargeting.</summary>
public sealed class HumanoidLocomotionBank
{
    private readonly ImmutableDictionary<HumanoidGait, HumanoidLocomotionClip> clips;
    public string Identity { get; }
    public string BodyRevision { get; }
    public HumanoidLocomotionState Initial { get; }
    public HumanoidLocomotionClip Clip(HumanoidGait gait) => clips[gait];

    public HumanoidLocomotionBank(HumanoidGameplayBody body, HumanoidLocomotionArtifact artifact, string identity)
    {
        if (artifact.Schema != "aurelian.humanoid.locomotion.v1" ||
            artifact.SkeletonId != body.Skeleton.SkeletonId || artifact.RestPoseId != body.Skeleton.RestPoseId ||
            artifact.Clips.IsDefault || artifact.Clips.Length != 3 ||
            artifact.Clips.Select(clip => clip.Gait).Distinct().Count() != 3 ||
            artifact.Clips.Any(clip => !Enum.IsDefined(clip.Gait)))
        {
            throw new InvalidDataException("Locomotion needs Idle/Walk/Run and this body's explicit rest skeleton.");
        }
        Identity = identity;
        BodyRevision = artifact.BodySha256;
        clips = artifact.Clips.ToImmutableDictionary(clip => clip.Gait);
        if (Clip(HumanoidGait.Idle).Frames.IsDefaultOrEmpty || Clip(HumanoidGait.Idle).Frames[0].Rotations.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("Locomotion needs explicit rotation channels.");
        }
        var channels = Clip(HumanoidGait.Idle).Frames[0].Rotations.Select(item => item.Joint).ToArray();
        HumanoidJointKind[] required = [HumanoidJointKind.LeftHip, HumanoidJointKind.LeftKnee, HumanoidJointKind.LeftAnkle,
            HumanoidJointKind.RightHip, HumanoidJointKind.RightKnee, HumanoidJointKind.RightAnkle];
        if (required.Any(joint => !channels.Contains(joint)))
        {
            throw new InvalidDataException("Locomotion needs both explicit hip-knee-ankle chains.");
        }
        foreach (var clip in artifact.Clips)
        {
            if (!double.IsFinite(clip.Duration) || clip.Duration <= 0 || clip.Frames.IsDefault || clip.Frames.Length < 2 ||
                clip.Frames[0].Seconds != 0 || clip.Frames[^1].Seconds != clip.Duration ||
                (clip.Gait != HumanoidGait.Idle && (!float.IsFinite(clip.NominalSpeed) || clip.NominalSpeed <= .1f)))
            {
                throw new InvalidDataException("Invalid locomotion clip duration or root travel.");
            }
            var first = clip.Frames[0];
            if (!first.Rotations.Select(item => item.Joint).SequenceEqual(channels))
            {
                throw new InvalidDataException("All gaits must share the same ordered rotation channels.");
            }
            double previous = -1;
            float distance = float.MinValue;
            foreach (var frame in clip.Frames)
            {
                if (!double.IsFinite(frame.Seconds) || frame.Seconds <= previous || !float.IsFinite(frame.RootDistance) ||
                    (clip.Gait != HumanoidGait.Idle && frame.RootDistance < distance - .0001f) ||
                    frame.Rotations.IsDefault || !frame.Rotations.Select(item => item.Joint).SequenceEqual(first.Rotations.Select(item => item.Joint)))
                {
                    throw new InvalidDataException("Locomotion keys need increasing time, forward root travel and matching channels.");
                }
                RequirePose(body, clip.Id, frame.Rotations, frame.OffsetMm);
                previous = frame.Seconds;
                distance = frame.RootDistance;
            }
            if (!first.Rotations.SequenceEqual(clip.Frames[^1].Rotations) || first.OffsetMm != clip.Frames[^1].OffsetMm)
            {
                throw new InvalidDataException("Locomotion pose endpoints must close independently of accumulated root travel.");
            }
        }
        var initial = Clip(HumanoidGait.Idle).Sample(0);
        Initial = new(HumanoidGait.Idle, 0, .2f, initial.Rotations, initial.OffsetMm,
            initial.Rotations, initial.OffsetMm, HumanoidFootPlant.Released, HumanoidFootPlant.Released,
            Vector3.Zero, Vector3.Zero, 0, true, null);
    }

    public static HumanoidLocomotionBank Load(string path, HumanoidGameplayBody body)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Locomotion exceeds the 16 MiB budget.");
        var artifact = JsonSerializer.Deserialize(bytes, HumanoidLocomotionJson.Default.HumanoidLocomotionArtifact)
            ?? throw new InvalidDataException("Empty locomotion artifact.");
        return new(body, artifact, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public HumanoidRootMotionRequest Request(HumanoidLocomotionState state, Vector3 desiredVelocity, float seconds)
    {
        if (!Finite(desiredVelocity) || desiredVelocity.Y != 0 || !float.IsFinite(seconds) || seconds <= 0 || seconds > .05f)
        {
            throw new ArgumentException("Root motion needs finite horizontal velocity and a tick of at most 50 ms.");
        }
        float speed = desiredVelocity.Length();
        HumanoidGait gait = HumanoidGait.Idle;
        if (speed > 2.6f) gait = HumanoidGait.Run;
        else if (speed > .05f) gait = HumanoidGait.Walk;
        var clip = Clip(gait);
        double start = state.Gait == gait ? state.Seconds : state.Seconds / Clip(state.Gait).Duration * clip.Duration;
        double phaseDelta = gait == HumanoidGait.Idle ? seconds : seconds * speed / clip.NominalSpeed;
        float distance = clip.Sample(start + phaseDelta).RootDistance - clip.Sample(start).RootDistance;
        Vector3 displacement = gait == HumanoidGait.Idle ? Vector3.Zero : Vector3.Normalize(desiredVelocity) * distance;
        return new(gait, start, phaseDelta, displacement);
    }

    public void ValidateState(HumanoidGameplayBody body, HumanoidLocomotionState state)
    {
        if (!Enum.IsDefined(state.Gait) || !double.IsFinite(state.Seconds) || state.Seconds < 0 ||
            state.Seconds >= Clip(state.Gait).Duration || !float.IsFinite(state.BlendSeconds) || state.BlendSeconds < 0 || state.BlendSeconds > .2f ||
            state.BlendFrom.IsDefault || state.Rotations.IsDefault || !float.IsFinite(state.PelvisAdjustmentMm) ||
            MathF.Abs(state.PelvisAdjustmentMm) > 100 || !Finite(state.RequestedDisplacement) || !Finite(state.AcceptedDisplacement) ||
            !float.IsFinite(state.AimWeight) || state.AimWeight < 0 || state.AimWeight > 1 ||
            !ValidPlant(state.Left) || !ValidPlant(state.Right))
        {
            throw new InvalidDataException("Invalid locomotion phase, blend or foot locks.");
        }
        RequirePose(body, "saved-locomotion", state.Rotations, state.OffsetMm);
        RequirePose(body, "saved-locomotion-blend", state.BlendFrom, state.BlendOffsetMm);
        var channels = Clip(state.Gait).Frames[0].Rotations.Select(item => item.Joint);
        if (!state.Rotations.Select(item => item.Joint).SequenceEqual(channels) ||
            !state.BlendFrom.Select(item => item.Joint).SequenceEqual(channels))
        {
            throw new InvalidDataException("Saved locomotion channels do not match this animation bank.");
        }
    }

    internal static SolvedHumanoidPose RequirePose(HumanoidGameplayBody body, string id,
        ImmutableArray<JointPose> rotations, Vector3 offset)
    {
        var solved = HumanoidKinematicSolver.SolveAuthored(body.Skeleton,
            new(id, body.Skeleton.SkeletonId, body.Skeleton.RestPoseId, rotations, offset));
        if (!solved.IsSolved) throw new InvalidDataException(id + ": " + string.Join("; ", solved.Diagnostics.Select(item => item.Message)));
        return solved.Pose!;
    }

    private static bool ValidPlant(HumanoidFootPlant plant) => plant is not null && Finite(plant.AnkleWorld) &&
        Finite(plant.NormalWorld) && MathF.Abs(plant.NormalWorld.LengthSquared() - 1) < .001f &&
        float.IsFinite(plant.Weight) && plant.Weight >= 0 && plant.Weight <= 1 &&
        float.IsFinite(plant.ResidualMm) && plant.ResidualMm >= 0;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    IncludeFields = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HumanoidLocomotionArtifact))]
[JsonSerializable(typeof(HumanoidLocomotionState))]
public partial class HumanoidLocomotionJson : JsonSerializerContext;
