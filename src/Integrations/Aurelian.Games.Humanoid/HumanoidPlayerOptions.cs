using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using Aetheris.Humanoid;
using Aurelian.Humanoid;
using Aurelian.World.Scenes;

namespace Aurelian.Games;

/// <summary>Explicit body revision and replaceable clips; load before creating a game window.</summary>
public sealed record HumanoidPlayerOptions(HumanoidGameplayBody Body, string BodyRevision)
{
    public HumanoidAnimationSet Animations { get; init; } = HumanoidAnimationSet.Basic;
    public HumanoidLocomotionBank? Locomotion { get; init; }
    public ImmutableArray<HumanoidJointAttachment> Attachments { get; init; } = [];
    public string Identity => BodyRevision + ":" + Animations.Identity + ":" + (Locomotion?.Identity ?? "in-place") + ":" + string.Join("|", Attachments.Select(item => item.Identity));

    public static HumanoidPlayerOptions Load(string path)
    {
        return new(HumanoidGameplayBody.Load(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    public void Validate()
    {
        if (Locomotion is not null && Locomotion.BodyRevision != BodyRevision)
        {
            throw new InvalidDataException("Locomotion was baked for a different gameplay body revision.");
        }
        if (Body is null || string.IsNullOrWhiteSpace(BodyRevision) || Animations is null || Attachments.IsDefault ||
            Attachments.Any(item => item is null) || Attachments.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Attachments.Length)
        {
            throw new ArgumentException("Humanoid presentation needs a body revision, clips and distinct attachments.");
        }
        foreach (var attachment in Attachments)
        {
            _ = Body.Skeleton.GetJoint(attachment.Joint);
        }
        foreach (var clip in Animations.Clips)
        {
            foreach (var key in clip.Keys)
            {
                var solved = Body.Solve(clip.Id, key.Joints);
                if (!solved.IsSolved)
                {
                    throw new InvalidDataException($"Clip '{clip.Id}' at {key.Seconds} has invalid anatomical channels: " +
                        string.Join("; ", solved.Diagnostics.Select(item => item.Message)));
                }
            }
        }
    }

    public SceneFrame Attach(SolvedHumanoidPose pose, Matrix4x4 world)
    {
        var frame = new SceneFrame([], []);
        foreach (var attachment in Attachments)
        {
            var projected = attachment.Project(Body, pose, world);
            frame = frame with
            {
                Boxes = frame.Boxes.AddRange(projected.Boxes),
                Meshes = frame.Meshes.AddRange(projected.Meshes),
                Models = frame.Models.AddRange(projected.Models),
            };
        }
        return frame;
    }

    public static HumanoidJointAttachment PlaceholderWeapon()
    {
        return new("weapon", HumanoidJointKind.RightWrist, Scene.Group("weapon",
        [
            Scene.Box("receiver", new(.10f, .10f, .24f), new(.3f, .6f, .7f, 1)),
            Scene.Box("barrel", new(.045f, .045f, .20f), new(.2f, .3f, .35f, 1), at: new(0, 0, -.20f)),
        ]), SceneTransform.Identity);
    }
}
