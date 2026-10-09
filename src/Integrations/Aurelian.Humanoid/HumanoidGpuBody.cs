using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;

namespace Aurelian.Humanoid;

/// <summary>One-time source binding upload, then O(joints + shapes) pose uploads. No per-frame CPU skinning.</summary>
public sealed class HumanoidGpuBody : IDisposable
{
    private readonly HumanoidGameplayBody body;
    private readonly VulkanSkinning3D skinning;
    public NativeGpuGeometry3D Geometry => skinning.Geometry;
    public int DispatchCount => skinning.DispatchCount;

    public HumanoidGpuBody(AurelianVulkanPlant plant, byte[] shader, string bodyPath)
        : this(plant, shader, HumanoidGameplayBody.Load(bodyPath))
    {
    }

    public HumanoidGpuBody(AurelianVulkanPlant plant, byte[] shader, HumanoidGameplayBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        this.body = body;
        var surface = body.Surface;
        var normals = new Vector3[surface.Vertices.Count];
        static Vector3 Point(Aetheris.Kernel.Core.Math.Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);
        foreach (var face in surface.Faces)
        {
            var a = Point(surface.Vertices[face.A].Position);
            var b = Point(surface.Vertices[face.B].Position);
            var c = Point(surface.Vertices[face.C].Position);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            normals[face.A] += normal;
            normals[face.B] += normal;
            normals[face.C] += normal;
        }
        var left = new Vector3[normals.Length];
        var right = new Vector3[normals.Length];
        if (body.Correctives.Count != 2 || !body.Correctives.Select(item => item.Id).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "hip-flexion-volume.left", "hip-flexion-volume.right" }))
            throw new InvalidDataException("GPU body needs the explicit bilateral corrective bank.");
        foreach (var corrective in body.Correctives)
        {
            var expectedJoint = corrective.Id == "hip-flexion-volume.left"
                ? HumanoidJointKind.LeftHip : HumanoidJointKind.RightHip;
            if (corrective.Joint != expectedJoint ||
                corrective.StartFlexionDegrees != 30 || corrective.FullFlexionDegrees != 90)
                throw new InvalidDataException("This GPU profile supports the authored bilateral hip-flexion bank only.");
            var target = corrective.Joint == HumanoidJointKind.LeftHip ? left : right;
            foreach (var delta in corrective.Vertices) target[delta.VertexIndex] += delta.DeltaMm;
        }
        var words = new float[surface.Faces.Count * 3 * 24];
        int offset = 0;
        foreach (var face in surface.Faces)
        {
            foreach (int vertex in new[] { face.A, face.B, face.C })
            {
                if (normals[vertex].LengthSquared() < 1e-12f)
                    throw new InvalidDataException("Body has a vertex without a usable smooth normal.");
                Vector3[] vectors = [Point(surface.Vertices[vertex].Position), Vector3.Normalize(normals[vertex])];
                foreach (Vector3 vector in vectors)
                {
                    words[offset++] = vector.X;
                    words[offset++] = vector.Y;
                    words[offset++] = vector.Z;
                }
                var influences = surface.SkinWeights[vertex].Weights.Where(weight => weight.Weight > 0)
                    .OrderByDescending(weight => weight.Weight).ThenBy(weight => weight.JointIndex).ToArray();
                if (influences.Length > 6) throw new InvalidDataException("Body exceeds the six-influence GPU profile.");
                for (int index = 0; index < 6; index++)
                {
                    words[offset++] = index < influences.Length ? influences[index].JointIndex : 0;
                    words[offset++] = index < influences.Length ? (float)influences[index].Weight : 0;
                }
                foreach (Vector3 vector in new[] { left[vertex], right[vertex] })
                {
                    words[offset++] = vector.X;
                    words[offset++] = vector.Y;
                    words[offset++] = vector.Z;
                }
            }
        }
        skinning = new(plant, shader, words, surface.Faces.Count * 3, body.Skeleton.Joints.Count);
    }

    public void Present(SolvedHumanoidPose pose, Matrix4x4 world)
    {
        var palette = body.CreatePalette(pose);
        var words = new float[palette.Count * 8];
        for (int index = 0; index < palette.Count; index++)
        {
            var value = palette[index];
            words[index * 8] = value.Real.X;
            words[index * 8 + 1] = value.Real.Y;
            words[index * 8 + 2] = value.Real.Z;
            words[index * 8 + 3] = value.Real.W;
            words[index * 8 + 4] = value.Dual.X;
            words[index * 8 + 5] = value.Dual.Y;
            words[index * 8 + 6] = value.Dual.Z;
            words[index * 8 + 7] = value.Dual.W;
        }
        var amounts = body.ObserveCorrectives(pose);
        skinning.Deform(words, (float)amounts["hip-flexion-volume.left"],
            (float)amounts["hip-flexion-volume.right"], world);
    }

    public Native3DVertex[] ReadVerticesForQualification() => skinning.ReadVertices();
    public void Dispose() => skinning.Dispose();
}
