using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aetheris.Humanoid;
using Aurelian.World.Scenes;

namespace Aurelian.Humanoid;

/// <summary>Authored geometry mounted in a named semantic joint's local frame, in metres.</summary>
public sealed class HumanoidJointAttachment
{
    private readonly SceneFrame geometry;
    public string Id { get; }
    public HumanoidJointKind Joint { get; }
    public SceneTransform Offset { get; }
    public string Identity { get; }

    public HumanoidJointAttachment(string id, HumanoidJointKind joint, SceneGroup geometry, SceneTransform offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _ = offset.Matrix();
        var plan = SceneCompiler.Compile(geometry);
        if (plan.Agents.Length != 0 || plan.Boxes.Any(box => box.Collision != SceneCollision.None) ||
            plan.Meshes.Any(mesh => mesh.Collision != SceneCollision.None))
        {
            throw new InvalidDataException("Joint attachments are presentation geometry; gameplay agents and colliders belong to the game scene.");
        }
        Id = id;
        Joint = joint;
        Offset = offset;
        this.geometry = new(plan.Boxes, plan.Meshes) { Models = plan.Models };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(id);
        writer.Write((int)joint);
        writer.Write(plan.ContentIdentity);
        Matrix4x4 matrix = offset.Matrix();
        float[] values = [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44];
        foreach (float value in values)
        {
            writer.Write(value);
        }
        writer.Flush();
        Identity = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public Matrix4x4 WorldTransform(HumanoidGameplayBody body, SolvedHumanoidPose pose, Matrix4x4 actorWorld)
    {
        // The binding verifies that the solved pose belongs to this rest skeleton.
        _ = body.CreatePalette(pose);
        int index = body.Skeleton.Joints.ToList().FindIndex(joint => joint.Kind == Joint);
        if (index < 0)
        {
            throw new InvalidDataException($"Attachment '{Id}' needs semantic joint '{Joint}'.");
        }
        return Offset.Matrix() * Matrix4x4.CreateScale(1000) * pose.GlobalTransforms[index] * HumanoidSpace.SourceToWorld * actorWorld;
    }

    public SceneFrame Project(HumanoidGameplayBody body, SolvedHumanoidPose pose, Matrix4x4 actorWorld)
    {
        return geometry.Transformed(WorldTransform(body, pose, actorWorld), Id);
    }
}
