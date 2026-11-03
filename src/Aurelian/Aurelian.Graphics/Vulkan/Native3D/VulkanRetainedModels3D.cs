using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record RetainedModel3DPrograms(CompiledGraphicsProgram Vertex, CompiledGraphicsProgram Shadow)
{
    public ClusterVisibilityPrograms? Visibility { get; init; }
    public CompiledGraphicsProgram? Bindless { get; init; }
}

/// <summary>Indexed instanced geometry with the existing material fragment stage and shadow semantics.</summary>
internal sealed unsafe partial class VulkanRetainedModels3D : IDisposable
{
    private const int InstanceBytes = 144;
    private const int MaximumInstances = 65_536;
    private const ulong MaximumMeshBytes = 256 * 1024 * 1024;
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanModel3DBatches materials;
    private readonly RetainedModel3DPrograms programs;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly Dictionary<NativeMesh3D, GpuMesh3D> meshes = new();
    private readonly Dictionary<Vulkan3DPass, AurelianVulkanGraphicsPipeline> shadows = new();
    private readonly AurelianVulkanBuffer?[] instances = new AurelianVulkanBuffer?[VulkanFrameInFlightSet.Count];
    private Dictionary<string, (NativeMesh3D Mesh, Matrix4x4 Transform)> previous = new(StringComparer.Ordinal);
    private readonly List<DrawGroup> groups = [];
    private Vector4[] words = [];
    private ulong residentBytes;
    private int uploadedMeshes;
    private long uploadedBytes;
    private NativeInstanceSet3D? resident;
    private readonly bool[] residentSlots = new bool[VulkanFrameInFlightSet.Count];
    private readonly List<VulkanClusterVisibility3D> visibility = [];
    public bool HasVisibility => visibility.Count > 0;

    private sealed record DrawGroup(GpuMesh3D Mesh, ModelMaterial Material, uint First, uint Count);

    public Native3DRetainedStatistics Statistics { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public int TriangleCount { get; private set; }

    public VulkanRetainedModels3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        VulkanModel3DBatches materials, AurelianVulkanRenderPass pass,
        CompiledGraphicsProgram modelProgram, RetainedModel3DPrograms programs)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.commands = commands;
        this.fences = fences;
        this.frames = frames;
        this.materials = materials;
        this.programs = programs;
        geometryPass = pass;
        if (modelProgram.PixelTargets.Count != 5 || modelProgram.Material?.Size != programs.Vertex.Material?.Size)
        {
            throw new ArgumentException("AUR-MESH-005: Retained models require the shared surface material ABI.");
        }
        var linked = new CompiledShaderProgram(CompiledShaderProgram.CurrentFormatVersion,
            [programs.Vertex.Shaders.Stages.Single(stage => stage.Stage == CompiledShaderStageKind.Vertex),
                modelProgram.Shaders.Stages.Single(stage => stage.Stage == CompiledShaderStageKind.Fragment)]);
        pipeline = CreatePipeline(pass, linked, materials.DescriptorLayout, shadow: false);
    }

    public void Prepare(NativeMesh3D mesh)
    {
        if (meshes.ContainsKey(mesh))
        {
            return;
        }
        ulong bytes = (ulong)mesh.Vertices.Length * 64 + (ulong)mesh.Indices.Length * 4;
        if (bytes > MaximumMeshBytes - residentBytes)
        {
            throw new InvalidOperationException("AUR-MESH-006: Retained mesh residency exceeds the declared 256 MiB budget.");
        }
        var gpu = new GpuMesh3D(plant, allocator, commands, fences, mesh);
        meshes.Add(mesh, gpu);
        residentBytes += bytes;
        uploadedMeshes++;
        uploadedBytes += (long)bytes;
    }

    public void Upload(IReadOnlyList<SceneInstance3D> source, bool resetHistory, bool useBindless = false)
    {
        if (source.Count > MaximumInstances)
        {
            throw new InvalidOperationException("AUR-MESH-007: Retained draws exceed the declared 65536-instance frame budget.");
        }
        if (!ReferenceEquals(resident, source) || useBindless != BindlessActive)
        {
            foreach (var item in visibility) frames.Retire(item);
            visibility.Clear();
            resident = source as NativeInstanceSet3D;
            Array.Clear(residentSlots);
            if (bindlessTable is not null) frames.Retire(bindlessTable);
            bindlessTable = null;
            bindlessRuns.Clear();
            BindlessActive = useBindless;
        }
        if (resident is not null && residentSlots[frames.Slot])
        {
            Statistics = new(source.Count, BindlessActive ? bindlessRuns.Count : groups.Count, 0, 0, 0, residentBytes)
            {
                CpuVisitedInstances = 0,
                IndirectGroups = visibility.Count,
            };
            return;
        }
        // A replacement resident set is a static scene revision, not a continuing transform stream.
        if (resident is not null) resetHistory = true;
        groups.Clear();
        TriangleCount = 0;
        var activeMeshes = source.Select(instance => instance.Mesh).ToHashSet();
        foreach (NativeMesh3D mesh in meshes.Keys.Where(mesh => !activeMeshes.Contains(mesh)).ToArray())
        {
            GpuMesh3D gpu = meshes[mesh];
            residentBytes -= gpu.Bytes;
            meshes.Remove(mesh);
            frames.Retire(gpu);
        }
        if (words.Length < source.Count * 9)
        {
            words = new Vector4[source.Count * 9];
        }
        var next = new Dictionary<string, (NativeMesh3D, Matrix4x4)>(source.Count, StringComparer.Ordinal);
        int first = 0;
        foreach (var group in source.GroupBy(instance => (instance.Mesh, instance.Material)))
        {
            Prepare(group.Key.Mesh);
            int start = first;
            foreach (SceneInstance3D instance in group)
            {
                if (string.IsNullOrWhiteSpace(instance.Identity) || !next.TryAdd(instance.Identity, (instance.Mesh, instance.Transform)))
                {
                    throw new InvalidDataException("AUR-MESH-008: Instance identities must be nonempty and unique within a frame.");
                }
                Matrix4x4 world = instance.Transform;
                ValidateTransform(world);
                Matrix4x4 prior = world;
                if (!resetHistory)
                {
                    if (instance.PreviousTransform is { } authored)
                    {
                        ValidateTransform(authored);
                        prior = authored;
                    }
                    else if (previous.TryGetValue(instance.Identity, out var old) && ReferenceEquals(old.Mesh, instance.Mesh))
                    {
                        prior = old.Transform;
                    }
                }
                Matrix4x4.Invert(world, out var inverse);
                Store(first * 9, world);
                Store(first * 9 + 3, prior);
                Store(first * 9 + 6, Matrix4x4.Transpose(inverse));
                first++;
            }
            var gpu = meshes[group.Key.Mesh];
            groups.Add(new(gpu, group.Key.Material, (uint)start, (uint)(first - start)));
            TriangleCount = checked(TriangleCount + (int)gpu.IndexCount / 3 * (first - start));
        }
        if (source.Count > 0)
        {
            ulong bytes = (ulong)source.Count * InstanceBytes;
            if (instances[frames.Slot] is not { } buffer || buffer.SizeBytes < bytes)
            {
                instances[frames.Slot]?.Dispose();
                instances[frames.Slot] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                    bytes, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "retained.instances");
            }
            if (!instances[frames.Slot]!.Write(MemoryMarshal.AsBytes(words.AsSpan(0, source.Count * 9))).Success)
            {
                throw new InvalidOperationException("AUR-MESH-009: Instance upload failed.");
            }
        }
        previous = next;
        Statistics = new(source.Count, groups.Count, uploadedMeshes, uploadedBytes,
            (long)source.Count * InstanceBytes, residentBytes) { CpuVisitedInstances = source.Count };
        uploadedMeshes = 0;
        uploadedBytes = 0;
        if (resident is not null)
        {
            if (BindlessActive && bindlessTable is null) PrepareBindless(resident);
            if (visibility.Count == 0 && programs.Visibility is not null && plant.Facts.IndirectDrawingEnabled)
            {
                if (!BindlessActive && resident.Groups.Length > 128)
                    throw new InvalidDataException("AUR-VIS-003: Grouped GPU culling is limited to 128 groups; use bindless or direct resident drawing for larger material sets.");
                long candidates = resident.Groups.Sum(group => (long)group.Length * ((group[0].Mesh.Indices.Length + 383) / 384));
                if (candidates > 1_048_576)
                {
                    throw new InvalidDataException("AUR-VIS-003: Resident cluster candidates exceed the aggregate 1048576-command budget.");
                }
                var created = new List<VulkanClusterVisibility3D>();
                try
                {
                    uint firstInstance = 0;
                    var cullingGroups = BindlessActive ? bindlessRuns.Select(run => run.Source) : resident.Groups;
                    foreach (var group in cullingGroups)
                    {
                        created.Add(new(plant, allocator, commands, fences, frames, programs.Visibility, group, firstInstance));
                        firstInstance += (uint)group.Length;
                    }
                    visibility.AddRange(created);
                }
                catch
                {
                    foreach (var group in created) group.Dispose();
                    throw;
                }
            }
            residentSlots[frames.Slot] = true;
            Statistics = Statistics with { IndirectGroups = visibility.Count };
        }
    }

    public void RecordVisibility(VulkanCommandBufferLease command, Matrix4x4 clip, Vector3 eye, ClusterVisibilityMode mode)
    {
        foreach (var group in visibility) group.Record(command, clip, eye, mode);
    }

    public IReadOnlyList<ClusterVisibilityReadback> ReadVisibilityForQualification()
        => visibility.Select(group => group.ReadForQualification()).ToArray();

    public void Draw(VulkanCommandBufferLease command, VulkanRenderPassScope scope, AurelianVulkanFramebuffer framebuffer)
    {
        if (BindlessActive)
        {
            DrawBindless(command, scope, framebuffer);
            return;
        }
        for (int index = 0; index < groups.Count; index++)
        {
            DrawGroup group = groups[index];
            materials.BindMaterial(command, pipeline, group.Material);
            DrawGroupIndexed(command, scope, framebuffer, pipeline, group, HasVisibility ? visibility[index] : null);
        }
        Statistics = Statistics with { MaterialDescriptorBinds = groups.Count };
    }

    public void DrawShadows(VulkanCommandBufferLease command, VulkanRenderPassScope scope, Vulkan3DPass pass)
    {
        if (groups.Count == 0)
        {
            return;
        }
        if (!shadows.TryGetValue(pass, out var shadow))
        {
            shadow = CreatePipeline(pass.Pass, programs.Shadow.Shaders, pass.DescriptorLayout, shadow: true);
            shadows.Add(pass, shadow);
        }
        DescriptorSet set = pass.Descriptor;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            shadow.NativePipelineLayout, 0, 1, &set, 0, null);
        foreach (DrawGroup group in groups)
        {
            if (!group.Material.AlphaMask)
            {
                DrawGroupIndexed(command, scope, pass.Framebuffer, shadow, group);
            }
        }
    }

    private void DrawGroupIndexed(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        AurelianVulkanFramebuffer framebuffer, AurelianVulkanGraphicsPipeline selected, DrawGroup group,
        VulkanClusterVisibility3D? indirect = null)
    {
        var result = new VulkanDrawCommandEncoder().DrawVertices(plant, command, scope,
            new(selected, group.Mesh.Vertices, group.Mesh.VertexCount, 0, VulkanViewportScissor.FromFramebuffer(framebuffer))
            {
                IndexBuffer = group.Mesh.Indices,
                IndexCount = group.Mesh.IndexCount,
                InstanceBuffer = instances[frames.Slot],
                InstanceCount = group.Count,
                FirstInstance = group.First,
                IndirectArguments = indirect?.Arguments,
                IndirectCount = indirect?.Count,
                MaximumIndirectDraws = indirect?.MaximumDrawCount ?? 0,
            });
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        }
    }

    private AurelianVulkanGraphicsPipeline CreatePipeline(AurelianVulkanRenderPass pass, CompiledShaderProgram shader,
        DescriptorSetLayout layout, bool shadow)
    {
        var attributes = new List<VulkanVertexAttributeDescriptor> { new(0, 0, VulkanVertexAttributeFormat.Float3, 0) };
        if (!shadow)
        {
            attributes.AddRange([new(1, 0, VulkanVertexAttributeFormat.Float3, 12),
                new(2, 0, VulkanVertexAttributeFormat.Float4, 24), new(3, 0, VulkanVertexAttributeFormat.Float2, 40),
                new(4, 0, VulkanVertexAttributeFormat.Float4, 48)]);
        }
        for (uint index = 0; index < (shadow ? 3 : 9); index++)
        {
            attributes.Add(new(5 + index, 1, VulkanVertexAttributeFormat.Float4, index * 16));
        }
        var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(shader,
            [new(0, 64), new(1, InstanceBytes) { PerInstance = true }], attributes,
            enableDepthTest: true, enableDepthWrite: true);
        if (!descriptor.Success)
        {
            throw new InvalidOperationException("AUR-MESH-010: Instanced pipeline declaration rejected.");
        }
        var created = VulkanGraphicsPipelineFactory.Create(plant, pass, descriptor.Descriptor!, [layout]);
        if (!created.Success)
        {
            throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        }
        return created.Pipeline!;
    }

    private void Store(int offset, Matrix4x4 matrix)
    {
        words[offset] = new(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        words[offset + 1] = new(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        words[offset + 2] = new(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
    }

    internal static void ValidateTransform(Matrix4x4 matrix)
    {
        float[] values = Lighting3DUniforms.Rows(matrix);
        if (values.Any(value => !float.IsFinite(value)) || matrix.M14 != 0 || matrix.M24 != 0
            || matrix.M34 != 0 || matrix.M44 != 1 || !Matrix4x4.Invert(matrix, out _)
            || !float.IsFinite(matrix.GetDeterminant()) || matrix.GetDeterminant() <= 0)
        {
            throw new InvalidDataException("AUR-MESH-011: Instances require finite, invertible, positive affine transforms.");
        }
    }

    public void Dispose()
    {
        bindlessPipeline?.Dispose();
        bindlessTable?.Dispose();
        foreach (var item in visibility) item.Dispose();
        foreach (var item in shadows.Values) item.Dispose();
        foreach (var item in instances) item?.Dispose();
        foreach (var item in meshes.Values) item.Dispose();
        pipeline.Dispose();
    }
}
