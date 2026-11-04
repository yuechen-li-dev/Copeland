using Aurelian.Graphics.Vulkan.Resources.Allocation;
using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.RayQueries;

internal sealed unsafe partial class VulkanSceneAcceleration3D
{
    private readonly List<AurelianVulkanBuffer> shadingOwned = [];
    private readonly AurelianVulkanBuffer[] shadingSlots = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly Dictionary<NativeMesh3D, int> triangleOffsets = [];
    private readonly Dictionary<NativeMesh3D, bool> validatedMeshes = [];
    private NativeInstanceSet3D? shaded;
    private readonly NativeInstanceSet3D?[] shadedSlots = new NativeInstanceSet3D?[VulkanFrameInFlightSet.Count];
    private float[] triangleAtlas = [];
    public AurelianVulkanBuffer Shading { get; private set; } = null!;

    public void PrepareShading(NativeInstanceSet3D source, int slot)
    {
        bool newLayout = shaded is null || !source.Keys.SequenceEqual(shaded.Keys) || source.PackedInstances.Any(instance => !triangleOffsets.ContainsKey(instance.Mesh));
        if (!ReferenceEquals(shadedSlots[slot], source)) ValidateSource(source);
        if (newLayout)
        {
            foreach (var buffer in shadingOwned)
            {
                buffer.Dispose();
            }
            shadingOwned.Clear();
            var activeMeshes = source.PackedInstances.Select(instance => instance.Mesh).ToHashSet();
            foreach (var mesh in validatedMeshes.Keys.Where(mesh => !activeMeshes.Contains(mesh)).ToArray())
            {
                validatedMeshes.Remove(mesh);
            }
            triangleOffsets.Clear();
            var words = new List<float>();
            foreach (var mesh in source.PackedInstances.Select(instance => instance.Mesh).Distinct())
            {
                triangleOffsets[mesh] = source.Count * 32 + words.Count;
                if (mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles) continue;
                foreach (uint index in mesh.Indices)
                {
                    Vector3 position = mesh.Vertices[(int)index].Position;
                    words.AddRange([position.X, position.Y, position.Z, 0]);
                }
            }
            triangleAtlas = words.ToArray();
            for (int index = 0; index < shadingSlots.Length; index++)
            {
                shadingSlots[index] = Allocate((ulong)(source.Count * 32 + triangleAtlas.Length) * 4,
                    VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                shadingOwned.Add(shadingSlots[index]);
            }
        }
        if (newLayout || !ReferenceEquals(shadedSlots[slot], source))
        {
            float[] words = new float[source.Count * 32 + triangleAtlas.Length];
            triangleAtlas.CopyTo(words, source.Count * 32);
            for (int index = 0; index < source.Count; index++)
            {
                var instance = source.PackedInstances[index];
                var material = instance.Material;
                int offset = index * 32;
                words[offset] = Bits((uint)instance.Mesh.PrimitiveKind);
                words[offset + 1] = Bits(instance.RayLayer);
                words[offset + 2] = Bits(instance.RayMask);
                words[offset + 3] = Bits((uint)index);
                Matrix4x4.Invert(instance.Transform, out var inverse);
                Lighting3DUniforms.Rows(Matrix4x4.Transpose(inverse)).AsSpan(0, 12).CopyTo(words.AsSpan(offset + 4));
                words[offset + 16] = material.BaseColor.X;
                words[offset + 17] = material.BaseColor.Y;
                words[offset + 18] = material.BaseColor.Z;
                words[offset + 19] = material.Roughness;
                words[offset + 20] = material.Emissive.X;
                words[offset + 21] = material.Emissive.Y;
                words[offset + 22] = material.Emissive.Z;
                words[offset + 23] = material.Metallic;
                words[offset + 24] = Bits((uint)triangleOffsets[instance.Mesh]);
                words[offset + 25] = validatedMeshes[instance.Mesh] || material.Textures().Any(texture => texture is not null) ? 1 : 0;
            }
            for (int index = 0; index < shadingSlots.Length; index++)
            {
                if (newLayout || index == slot)
                {
                    Write(shadingSlots[index], words);
                    shadedSlots[index] = source;
                }
            }
            shaded = source;
        }
        Shading = shadingSlots[slot];
    }

    private void ValidateSource(NativeInstanceSet3D source)
    {
        foreach (var material in source.Materials)
        {
            if (material.Unlit || material.SubsurfaceStrength > 0)
                throw new NotSupportedException("AUR-GI-001: Retained diffuse hits require opaque materials without unlit or subsurface shading.");
        }
        foreach (var mesh in source.PackedInstances.Select(instance => instance.Mesh).Distinct())
        {
            if (validatedMeshes.ContainsKey(mesh)) continue;
            bool needsAttributes = mesh.Vertices.Any(vertex => vertex.Color != Vector4.One);
            if (mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
            {
                validatedMeshes.Add(mesh, needsAttributes);
                continue;
            }
            for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
            {
                var a = mesh.Vertices[(int)mesh.Indices[triangle]];
                var b = mesh.Vertices[(int)mesh.Indices[triangle + 1]];
                var c = mesh.Vertices[(int)mesh.Indices[triangle + 2]];
                Vector3 cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                if (cross.LengthSquared() < 1e-16f)
                    throw new NotSupportedException("AUR-GI-001: Retained diffuse triangles must be nondegenerate.");
                Vector3 normal = Vector3.Normalize(cross);
                if (a.Normal.LengthSquared() < 1e-12f || b.Normal.LengthSquared() < 1e-12f || c.Normal.LengthSquared() < 1e-12f
                    || Vector3.DistanceSquared(normal, Vector3.Normalize(a.Normal)) > 1e-8f
                    || Vector3.DistanceSquared(normal, Vector3.Normalize(b.Normal)) > 1e-8f
                    || Vector3.DistanceSquared(normal, Vector3.Normalize(c.Normal)) > 1e-8f)
                {
                    needsAttributes = true;
                }
            }
            validatedMeshes.Add(mesh, needsAttributes);
        }
    }
}
