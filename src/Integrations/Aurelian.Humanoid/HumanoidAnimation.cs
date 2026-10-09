using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Aetheris.Humanoid;

namespace Aurelian.Humanoid;

public enum CharacterMotion { Idle, Walk, Aim, Run }
public sealed record HumanoidKeyframe(double Seconds, ImmutableArray<AnatomicalJointRequest> Joints);

/// <summary>Explicit absolute anatomical channels; no reflection, bone-name discovery or mesh mutation.</summary>
public sealed class HumanoidAnimationClip
{
    private readonly ImmutableArray<HumanoidKeyframe> keys;

    public HumanoidAnimationClip(string id, double duration, bool loop, IEnumerable<HumanoidKeyframe> keys)
    {
        this.keys = keys.ToImmutableArray();
        if (string.IsNullOrWhiteSpace(id) || !double.IsFinite(duration) || duration <= 0 || this.keys.Length < 2 ||
            this.keys[0].Seconds != 0 || this.keys[^1].Seconds != duration)
            throw new ArgumentException("Animation needs an identity, duration and boundary keyframes.");
        var joints = this.keys[0].Joints.Select(item => item.Joint).ToArray();
        if (joints.Length == 0 || joints.Distinct().Count() != joints.Length)
            throw new ArgumentException("Animation channels need distinct semantic joints.");
        double previous = -1;
        foreach (var key in this.keys)
        {
            if (!double.IsFinite(key.Seconds) || key.Seconds <= previous ||
                !key.Joints.Select(item => item.Joint).SequenceEqual(joints) ||
                key.Joints.Any(item => !double.IsFinite(item.FlexionDegrees) ||
                    !double.IsFinite(item.AbductionDegrees) || !double.IsFinite(item.TwistDegrees)))
                throw new ArgumentException("Animation keys must have increasing times and the same finite channels.");
            previous = key.Seconds;
        }
        if (loop && !this.keys[0].Joints.SequenceEqual(this.keys[^1].Joints))
            throw new ArgumentException("Looping clip endpoints must agree.");
        Id = id;
        Duration = duration;
        Loop = loop;
    }

    public string Id { get; }
    public double Duration { get; }
    public bool Loop { get; }
    public ImmutableArray<HumanoidKeyframe> Keys => keys;

    public ImmutableArray<AnatomicalJointRequest> Sample(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        double time = Loop ? seconds % Duration : Math.Min(seconds, Duration);
        for (int index = 1; index < keys.Length; index++)
        {
            if (time <= keys[index].Seconds)
            {
                var left = keys[index - 1];
                var right = keys[index];
                return Blend(left.Joints, right.Joints, (time - left.Seconds) / (right.Seconds - left.Seconds));
            }
        }
        return keys[^1].Joints;
    }

    internal static ImmutableArray<AnatomicalJointRequest> Blend(ImmutableArray<AnatomicalJointRequest> from,
        ImmutableArray<AnatomicalJointRequest> to, double amount)
    {
        if (from.Length != to.Length || !from.Select(item => item.Joint).SequenceEqual(to.Select(item => item.Joint)))
            throw new InvalidDataException("Animation blends require matching semantic channels.");
        var result = ImmutableArray.CreateBuilder<AnatomicalJointRequest>(from.Length);
        for (int index = 0; index < from.Length; index++)
        {
            var a = from[index];
            var b = to[index];
            result.Add(new(a.Joint, a.FlexionDegrees + (b.FlexionDegrees - a.FlexionDegrees) * amount,
                a.AbductionDegrees + (b.AbductionDegrees - a.AbductionDegrees) * amount,
                a.TwistDegrees + (b.TwistDegrees - a.TwistDegrees) * amount));
        }
        return result.MoveToImmutable();
    }
}

public sealed record CharacterAnimationState(CharacterMotion Motion, double ClipSeconds,
    double TransitionSeconds, ImmutableArray<AnatomicalJointRequest> TransitionFrom)
{
    public HumanoidLocomotionState? Locomotion { get; init; }
}

/// <summary>Reusable clip fragments. Callers may replace individual clips with ordinary C# authoring.</summary>
public sealed class HumanoidAnimationSet
{
    private readonly IReadOnlyDictionary<CharacterMotion, HumanoidAnimationClip> clips;
    public HumanoidAnimationSet(HumanoidAnimationClip idle, HumanoidAnimationClip walk, HumanoidAnimationClip aim)
    {
        clips = new Dictionary<CharacterMotion, HumanoidAnimationClip>
        {
            [CharacterMotion.Idle] = idle, [CharacterMotion.Walk] = walk, [CharacterMotion.Aim] = aim,
            [CharacterMotion.Run] = walk,
        };
        foreach (var clip in clips.Values)
            _ = HumanoidAnimationClip.Blend(idle.Sample(0), clip.Sample(0), 0);
    }

    public CharacterAnimationState Initial => new(CharacterMotion.Idle, 0, .2, clips[CharacterMotion.Idle].Sample(0));

    public string Identity => ComputeIdentity();
    public ImmutableArray<HumanoidAnimationClip> Clips => clips.OrderBy(pair => pair.Key)
        .Select(pair => pair.Value).ToImmutableArray();

    private string ComputeIdentity()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var pair in clips.OrderBy(pair => pair.Key))
        {
            writer.Write((int)pair.Key);
            writer.Write(pair.Value.Id);
            writer.Write(pair.Value.Duration);
            writer.Write(pair.Value.Loop);
            writer.Write(pair.Value.Keys.Length);
            foreach (var key in pair.Value.Keys)
            {
                writer.Write(key.Seconds);
                writer.Write(key.Joints.Length);
                foreach (var joint in key.Joints)
                {
                    writer.Write((int)joint.Joint);
                    writer.Write(joint.FlexionDegrees);
                    writer.Write(joint.AbductionDegrees);
                    writer.Write(joint.TwistDegrees);
                }
            }
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public void ValidateState(CharacterAnimationState state)
    {
        if (state is null || !Enum.IsDefined(state.Motion) || !double.IsFinite(state.ClipSeconds) ||
            state.ClipSeconds < 0 || state.ClipSeconds > clips[state.Motion].Duration ||
            !double.IsFinite(state.TransitionSeconds) || state.TransitionSeconds < 0 || state.TransitionSeconds > .2 ||
            state.TransitionFrom.IsDefault || state.TransitionFrom.Any(joint =>
                !double.IsFinite(joint.FlexionDegrees) || !double.IsFinite(joint.AbductionDegrees) || !double.IsFinite(joint.TwistDegrees)))
        {
            throw new InvalidDataException("Invalid humanoid animation state.");
        }
        _ = Sample(state);
    }

    public CharacterAnimationState Advance(CharacterAnimationState state, CharacterMotion motion,
        double seconds, float speed)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !float.IsFinite(speed) || speed < 0 || !Enum.IsDefined(motion))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0) return state;
        if (state.Motion != motion)
            state = new CharacterAnimationState(motion, 0, 0, Sample(state)) { Locomotion = state.Locomotion };
        double rate = motion == CharacterMotion.Walk ? Math.Clamp(speed / .8, .25, 2.5) : 1;
        double time = state.ClipSeconds + seconds * rate;
        var clip = clips[motion];
        time = clip.Loop ? time % clip.Duration : Math.Min(time, clip.Duration);
        return state with { ClipSeconds = time, TransitionSeconds = Math.Min(.2, state.TransitionSeconds + seconds) };
    }

    public ImmutableArray<AnatomicalJointRequest> Sample(CharacterAnimationState state)
    {
        double amount = Math.Clamp(state.TransitionSeconds / .2, 0, 1);
        amount = amount * amount * (3 - 2 * amount);
        return HumanoidAnimationClip.Blend(state.TransitionFrom, clips[state.Motion].Sample(state.ClipSeconds), amount);
    }

    public static HumanoidAnimationSet Basic { get; } = CreateBasic();

    private static HumanoidAnimationSet CreateBasic()
    {
        static ImmutableArray<AnatomicalJointRequest> Pose(double leftHip = 0, double rightHip = 0,
            double leftKnee = 3, double rightKnee = 3, double leftArm = 0, double rightArm = 0,
            double abduction = 12, double elbow = 12) =>
        [
            new(HumanoidJointKind.LeftHip, leftHip), new(HumanoidJointKind.RightHip, rightHip),
            new(HumanoidJointKind.LeftKnee, leftKnee), new(HumanoidJointKind.RightKnee, rightKnee),
            new(HumanoidJointKind.LeftShoulder, leftArm, abduction),
            new(HumanoidJointKind.RightShoulder, rightArm, abduction),
            new(HumanoidJointKind.LeftElbow, elbow), new(HumanoidJointKind.RightElbow, elbow),
        ];
        var idle = Pose();
        var aim = Pose(leftArm: 55, rightArm: 55, abduction: 15, elbow: 60);
        var stride = Pose(20, -15, 8, 35, -12, 18);
        var reverse = Pose(-15, 20, 35, 8, 18, -12);
        return new(
            new("idle.v1", 1, true, [new(0, idle), new(1, idle)]),
            new("walk.v1", 1, true, [new(0, stride), new(.25, idle), new(.5, reverse), new(.75, idle), new(1, stride)]),
            new("aim.v1", 1, false, [new(0, aim), new(1, aim)]));
    }
}
