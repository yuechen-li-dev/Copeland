using System.Collections.Immutable;
using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Spatial3D;

namespace Aurelian.Humanoid;

/// <summary>Fixed-tick gait phase and world-space foot locks. No surface deformation or renderer state.</summary>
public sealed class HumanoidLocomotion
{
    private readonly HumanoidGameplayBody body;
    private readonly HumanoidLocomotionBank bank;
    private readonly Dictionary<string, int> indices;
    private readonly float leftSoleHeight;
    private readonly float rightSoleHeight;

    public HumanoidLocomotion(HumanoidGameplayBody body, HumanoidLocomotionBank bank)
    {
        this.body = body;
        this.bank = bank;
        indices = body.Skeleton.Joints.Select((joint, index) => (joint.Kind, index))
            .ToDictionary(item => item.Kind.ToString(), item => item.index);
        leftSoleHeight = MeasureSoleHeight(AnatomicalSide.Left);
        rightSoleHeight = MeasureSoleHeight(AnatomicalSide.Right);
    }

    public HumanoidLocomotionState Advance(HumanoidLocomotionState state, HumanoidRootMotionRequest request,
        Vector3 accepted, bool grounded, Matrix4x4 actorWorld, IRayQueryWorld3D groundQueries,
        bool aiming, SolvedHumanoidPose aimPose, float seconds)
    {
        var horizontal = new Vector3(accepted.X, 0, accepted.Z);
        float fraction = request.Displacement.LengthSquared() > .00000001f
            ? Math.Clamp(horizontal.Length() / request.Displacement.Length(), 0, 1) : 0;
        HumanoidGait gait = grounded && horizontal.Length() / seconds > .05f ? request.Gait : HumanoidGait.Idle;
        var clip = bank.Clip(gait);
        bool changed = gait != state.Gait;
        double phase = gait == HumanoidGait.Idle
            ? (changed ? 0 : state.Seconds) + seconds
            : request.StartSeconds + request.PhaseDelta * fraction;
        phase %= clip.Duration;
        var sample = clip.Sample(phase);
        var from = changed ? state.Rotations : state.BlendFrom;
        Vector3 fromOffset = changed ? state.OffsetMm + Vector3.UnitZ * state.PelvisAdjustmentMm : state.BlendOffsetMm;
        float blendSeconds = Math.Min(.2f, (changed ? 0 : state.BlendSeconds) + seconds);
        float blend = blendSeconds / .2f;
        blend = blend * blend * (3 - 2 * blend);
        var rotations = HumanoidLocomotionClip.BlendRotations(from, sample.Rotations, blend);
        Vector3 offset = Vector3.Lerp(fromOffset, sample.OffsetMm, blend);
        float aimWeight = Math.Clamp(state.AimWeight + (aiming ? seconds * 5 : -seconds * 5), 0, 1);
        if (aimWeight > 0)
        {
            var arms = aimPose.PoseState.LocalRotations.ToDictionary(item => item.Joint, item => item.LocalRotation);
            rotations = rotations.Select(item => item.Joint.ToString().EndsWith("Shoulder") || item.Joint.ToString().EndsWith("Elbow")
                ? new JointPose(item.Joint, Quaternion.Normalize(Quaternion.Slerp(item.LocalRotation,
                    arms.GetValueOrDefault(item.Joint, item.LocalRotation), aimWeight))) : item).ToImmutableArray();
        }
        var pose = HumanoidLocomotionBank.RequirePose(body, "locomotion", rotations, offset);
        HumanoidFootPlant left = Contact(state.Left, AnatomicalSide.Left, sample.LeftContact && grounded);
        HumanoidFootPlant right = Contact(state.Right, AnatomicalSide.Right, sample.RightContact && grounded);
        float desiredPelvis = Math.Clamp(Math.Max(Lowering(left, AnatomicalSide.Left), Lowering(right, AnatomicalSide.Right)), 0, 100);
        float pelvis = state.PelvisAdjustmentMm + Math.Clamp(desiredPelvis - state.PelvisAdjustmentMm, -seconds * 600, seconds * 600);
        offset.Z -= pelvis;
        pose = HumanoidLocomotionBank.RequirePose(body, "locomotion", rotations, offset);
        string? diagnostic = null;
        left = Solve(left, AnatomicalSide.Left);
        right = Solve(right, AnatomicalSide.Right);
        return new(gait, phase, blendSeconds, from, fromOffset,
            pose.PoseState.LocalRotations.OrderBy(item => indices[item.Joint.ToString()]).ToImmutableArray(),
            offset, left, right, request.Displacement, accepted, pelvis, grounded, diagnostic, aimWeight);

        HumanoidFootPlant Contact(HumanoidFootPlant previous, AnatomicalSide side, bool contact)
        {
            if (!grounded) return previous with { Locked = false, Weight = 0 };
            if (!contact)
            {
                return previous with { Locked = false, Weight = Math.Max(0, previous.Weight - seconds * 10) };
            }
            Vector3 ankleWorld = Vector3.Transform(pose.GlobalTransforms[Index(side, "Ankle")].Translation, HumanoidSpace.SourceToWorld * actorWorld);
            var hit = groundQueries.Raycast(new(ankleWorld + Vector3.UnitY * .25f, -Vector3.UnitY, .65f));
            if (hit is not { Normal.Y: >= .707f })
            {
                return previous with { Locked = false, Weight = Math.Max(0, previous.Weight - seconds * 10) };
            }
            var target = hit.Value.Point + hit.Value.Normal * (side == AnatomicalSide.Left ? leftSoleHeight : rightSoleHeight);
            if (!previous.Locked)
            {
                previous = previous with { AnkleWorld = target, NormalWorld = hit.Value.Normal };
            }
            return previous with { Locked = true, Weight = Math.Min(1, previous.Weight + seconds * 10) };
        }
        float Lowering(HumanoidFootPlant plant, AnatomicalSide side)
        {
            if (plant.Weight == 0) return 0;
            Vector3 hip = Vector3.Transform(pose.GlobalTransforms[Index(side, "Hip")].Translation, HumanoidSpace.SourceToWorld * actorWorld);
            Vector3 middle = pose.GlobalTransforms[Index(side, "Knee")].Translation;
            Vector3 end = pose.GlobalTransforms[Index(side, "Ankle")].Translation;
            float maximum = Vector3.Distance(pose.GlobalTransforms[Index(side, "Hip")].Translation, middle) + Vector3.Distance(middle, end);
            return Math.Max(0, Vector3.Distance(hip, plant.AnkleWorld) * 1000 - maximum + 2);
        }
        HumanoidFootPlant Solve(HumanoidFootPlant plant, AnatomicalSide side)
        {
            if (plant.Weight <= 0) return plant with { ResidualMm = 0, ReachClamped = false };
            Matrix4x4.Invert(HumanoidSpace.SourceToWorld * actorWorld, out var worldToSource);
            Vector3 target = Vector3.Transform(plant.AnkleWorld, worldToSource);
            Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(plant.NormalWorld, worldToSource));
            HumanoidLegSolve solved;
            try
            {
                solved = HumanoidKinematicSolver.SolveLeg(body.Skeleton, pose, new(side, target, normal));
            }
            catch (InvalidDataException error)
            {
                diagnostic = error.Message;
                return plant with { Locked = false, Weight = 0, ReachClamped = true };
            }
            if (solved.ReachClamped && solved.ResidualMm > 50)
            {
                diagnostic = "Foot lock released: ankle target exceeds rigid leg reach.";
                float releasedResidual = Vector3.Distance(pose.GlobalTransforms[Index(side, "Ankle")].Translation, target);
                return plant with { Locked = false, Weight = 0, ResidualMm = releasedResidual, ReachClamped = true };
            }
            var original = pose.PoseState.LocalRotations.ToDictionary(item => item.Joint, item => item.LocalRotation);
            var adjusted = solved.Pose.PoseState.LocalRotations.Select(item => new JointPose(item.Joint,
                Quaternion.Normalize(Quaternion.Slerp(original.GetValueOrDefault(item.Joint, Quaternion.Identity), item.LocalRotation, plant.Weight))))
                .ToImmutableArray();
            pose = HumanoidLocomotionBank.RequirePose(body, "locomotion.ik", adjusted, offset);
            float residual = Vector3.Distance(pose.GlobalTransforms[Index(side, "Ankle")].Translation, target);
            return plant with { ResidualMm = residual, ReachClamped = solved.ReachClamped };
        }
    }

    private int Index(AnatomicalSide side, string stem) => indices[side + stem];
    private float MeasureSoleHeight(AnatomicalSide side)
    {
        var ankle = body.Skeleton.Joints[Index(side, "Ankle")].GlobalBind.Translation;
        var region = side == AnatomicalSide.Left ? HumanoidRegionKind.LeftFoot : HumanoidRegionKind.RightFoot;
        double minimum = body.Surface.Vertices.Where(vertex => vertex.Region == region).Select(vertex => vertex.Position.Z).DefaultIfEmpty(0).Min();
        return (ankle.Z - (float)minimum) * .001f;
    }

}
