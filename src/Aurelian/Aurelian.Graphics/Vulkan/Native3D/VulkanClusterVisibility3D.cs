using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record ClusterVisibilityPrograms(byte[] Cull, byte[] Scan, byte[] Scatter);
public enum ClusterVisibilityMode { Unculled, InstanceFrustum, ClusterFrustum, ClusterAndCone }
public sealed record ClusterVisibilityReadback(int Candidates, uint DrawCount, uint[] Ranks, bool[] CpuVisible)
{
    public uint[] SelectedLods { get; init; } = [];
    public long SubmittedTriangles { get; init; }
    public uint[] SeedMask { get; init; } = [];
    public Vector4[] CandidateSpheres { get; init; } = [];
}

/// <summary>Resident cluster inputs and stable GPU compaction, consumed by the existing material pipeline.</summary>
internal sealed unsafe class VulkanClusterVisibility3D : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LodRange(float Error, uint FirstIndex, uint IndexCount, uint Reserved);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Candidate(Vector4 InstanceSphere, Vector4 Sphere, Vector4 Cone,
        uint FirstIndex, uint IndexCount, uint FirstInstance, uint LodCount, LodRange Level1, LodRange Level2, LodRange Level3);

    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly Candidate[] candidates;
    private readonly AurelianVulkanBuffer[] inputs = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly NativeMesh3D sourceMesh;
    private readonly ImmutableArray<NativeMeshCluster3D> sourceClusters;
    private readonly Vector4 sourceBounds;
    private readonly uint sourceFirstInstance;
    private readonly AurelianVulkanBuffer lodState;
    private Vector4 lodSettings;
    private readonly AurelianVulkanBuffer[,] camera = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count, 2];
    private readonly AurelianVulkanBuffer[] seeds = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly bool hzbEnabled;
    private readonly AurelianVulkanBuffer[] ranks = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] blocks = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] offsets = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] arguments = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] counts = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[,,] sets = new DescriptorSet[VulkanFrameInFlightSet.Count, 2, 3];
    private readonly Pipeline[] pipelines = new Pipeline[4];
    private DescriptorSetLayout setLayout;
    private DescriptorPool pool;
    private PipelineLayout layout;
    private readonly uint blockCount;
    private int lastSlot;
    private Matrix4x4 lastClip;
    private Vector3 lastEye;
    private ClusterVisibilityMode lastMode;
    private bool lastUsedHzb;

    public AurelianVulkanBuffer Arguments => arguments[frames.Slot];
    public AurelianVulkanBuffer Count => counts[frames.Slot];
    public uint MaximumDrawCount => (uint)candidates.Length;

    public VulkanClusterVisibility3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        ClusterVisibilityPrograms programs, ImmutableArray<SceneInstance3D> instances, uint firstInstance, byte[]? hzbCull = null)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.commands = commands;
        this.fences = fences;
        this.frames = frames;
        hzbEnabled = hzbCull is not null;
        var mesh = instances[0].Mesh;
        var clusters = NativeMeshClusters3D.Build(mesh);
        bool hasLods = mesh.LodRanges.Length > 1;
        if (hasLods)
        {
            Vector3 low = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Min);
            Vector3 high = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Max);
            Vector3 midpoint = (low + high) * .5f;
            float extent = mesh.Vertices.Max(vertex => Vector3.Distance(midpoint, vertex.Position)) + 1e-4f;
            clusters = [new(0, (uint)mesh.Indices.Length, new(midpoint, extent), new(0, 0, 0, -2))];
        }
        long total = (long)instances.Length * clusters.Length;
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        if (total > 1_048_576 || total > properties.Limits.MaxDrawIndirectCount)
        {
            throw new InvalidDataException("AUR-VIS-003: A material/mesh group exceeds its cluster-command budget or device draw limit.");
        }
        candidates = new Candidate[total];
        Vector3 minimum = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Min);
        Vector3 maximum = mesh.Vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Max);
        Vector3 center = (minimum + maximum) * .5f;
        float radius = mesh.Vertices.Max(vertex => Vector3.Distance(center, vertex.Position)) + 1e-4f;
        if (mesh.PrimitiveKind != Aurelian.Rendering.Contracts.Models.AnalyticPrimitiveKind3D.Triangles)
        {
            center = Vector3.Zero;
            radius = NativeAnalyticPrimitives3D.Bounds(mesh.PrimitiveKind).W;
        }
        sourceMesh = mesh;
        sourceClusters = clusters;
        sourceBounds = new(center, radius);
        sourceFirstInstance = firstInstance;
        FillCandidates(instances);
        blockCount = ((uint)candidates.Length + 63) / 64;
        try
        {
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            {
                inputs[slot] = Buffer((ulong)candidates.Length * 112, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                Require(inputs[slot].Write(MemoryMarshal.AsBytes(candidates.AsSpan())).Success, "Candidate upload failed.");
            }
            lodState = Buffer((ulong)candidates.Length * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination);
            using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
            Require(uploader.Upload(new(lodState, new byte[candidates.Length * 4], DebugName: "lod.history")).Success,
                "LOD history initialization failed.");
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            {
                camera[slot, 0] = Buffer(192, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                if (hzbEnabled)
                {
                    camera[slot, 1] = Buffer(192, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                    seeds[slot] = Buffer((ulong)candidates.Length * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
                }
                ranks[slot] = Buffer(blockCount * 64UL * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
                blocks[slot] = Buffer(blockCount * 4UL, VulkanBufferUsage.Storage);
                offsets[slot] = Buffer(blockCount * 4UL, VulkanBufferUsage.Storage);
                arguments[slot] = Buffer((ulong)candidates.Length * 20, VulkanBufferUsage.Storage | VulkanBufferUsage.Indirect | VulkanBufferUsage.TransferSource);
                counts[slot] = Buffer(4, VulkanBufferUsage.Storage | VulkanBufferUsage.Indirect | VulkanBufferUsage.TransferSource);
            }
            CreatePipelines(programs, hzbCull);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void UpdateInstances(ImmutableArray<SceneInstance3D> instances)
    {
        if (instances.Length * sourceClusters.Length != candidates.Length
            || instances.Any(instance => !ReferenceEquals(instance.Mesh, sourceMesh)))
        {
            throw new InvalidOperationException("AUR-VIS-005: Motion changed the admitted cluster topology.");
        }
        FillCandidates(instances);
        Require(inputs[frames.Slot].Write(MemoryMarshal.AsBytes(candidates.AsSpan())).Success, "Motion candidate upload failed.");
    }

    private void FillCandidates(ImmutableArray<SceneInstance3D> instances)
    {
        int index = 0;
        for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
        {
            var instance = instances[instanceIndex];
            Vector4 instanceSphere = NativeMeshClusters3D.TransformSphere(sourceBounds, instance.Transform);
            float errorScale = NativeMeshClusters3D.TransformSphere(new(0, 0, 0, 1), instance.Transform).W;
            LodRange Range(int level)
            {
                if (level >= sourceMesh.LodRanges.Length) return default;
                var range = sourceMesh.LodRanges[level];
                return new(range.Error * errorScale, range.FirstIndex, range.IndexCount, 0);
            }
            foreach (var cluster in sourceClusters)
            {
                Vector4 cone = new(0, 0, 0, -2);
                if (!instance.Material.DoubleSided && cluster.Cone.W >= -1 && IsSimilarity(instance.Transform))
                {
                    Vector3 axis = Vector3.Normalize(Vector3.TransformNormal(new(cluster.Cone.X, cluster.Cone.Y, cluster.Cone.Z), instance.Transform));
                    cone = new(axis, cluster.Cone.W);
                }
                candidates[index++] = new(instanceSphere, NativeMeshClusters3D.TransformSphere(cluster.Sphere, instance.Transform),
                    cone, cluster.FirstIndex, cluster.IndexCount, sourceFirstInstance + (uint)instanceIndex,
                    (uint)sourceMesh.LodRanges.Length - 1, Range(1), Range(2), Range(3));
            }
        }
    }

    public void Record(VulkanCommandBufferLease command, Matrix4x4 clip, Vector3 eye, ClusterVisibilityMode mode)
        => RecordPhase(command, clip, eye, mode, 0);

    public void ConfigureLod(uint width, uint height, float pixelError)
    {
        if (!float.IsFinite(pixelError) || pixelError < 0 || pixelError > 16)
            throw new ArgumentOutOfRangeException(nameof(pixelError), "AUR-LOD-010: Pixel error must be finite and in [0,16].");
        lodSettings = new(width * .5f, height * .5f, pixelError, 0);
    }

    public void RecordHzb(VulkanCommandBufferLease command, Matrix4x4 clip, Vector3 eye, ClusterVisibilityMode mode, bool seed)
        => RecordPhase(command, clip, eye, mode, seed ? 1u : 2u);

    private void RecordPhase(VulkanCommandBufferLease command, Matrix4x4 clip, Vector3 eye, ClusterVisibilityMode mode, uint hzbPhase)
    {
        Barrier(command, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        int variant = hzbPhase == 2 ? 1 : 0;
        Barrier(command, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.ComputeShaderBit);
        lastSlot = frames.Slot;
        lastClip = clip;
        lastEye = eye;
        lastMode = mode;
        lastUsedHzb = hzbPhase > 0;
        Vector4[] parameters = [.. Planes(clip), new(eye, 1), Vector4.Zero,
            new(clip.M11, clip.M21, clip.M31, clip.M41), new(clip.M12, clip.M22, clip.M32, clip.M42),
            new(clip.M14, clip.M24, clip.M34, clip.M44), lodSettings];
        Span<byte> parameterBytes = MemoryMarshal.AsBytes(parameters.AsSpan());
        Span<uint> metadata = MemoryMarshal.Cast<byte, uint>(parameterBytes[112..]);
        metadata[0] = (uint)candidates.Length;
        metadata[1] = blockCount;
        metadata[2] = (uint)mode;
        metadata[3] = hzbPhase;
        Require(camera[frames.Slot, variant].Write(parameterBytes).Success, "Camera upload failed.");
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipelines[0]);
        Dispatch(0, blockCount);
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        Dispatch(1, 1);
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        Dispatch(2, blockCount);
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.IndirectCommandReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.DrawIndirectBit);

        void Dispatch(int phase, uint groups)
        {
            DescriptorSet set = sets[frames.Slot, variant, phase];
            int pipeline = phase == 0 && hzbPhase > 0 ? 3 : phase;
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipelines[pipeline]);
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, groups, 1, 1);
        }
    }

    public ClusterVisibilityReadback ReadForQualification()
    {
        frames.Drain();
        ulong commandOffset = (ulong)candidates.Length * (hzbEnabled ? 8UL : 4UL) + 4;
        using var staging = BufferForReadback(commandOffset + arguments[lastSlot].SizeBytes);
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Readback command begin failed.");
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        BufferCopy rankCopy = new(0, 0, (ulong)candidates.Length * 4);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, ranks[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &rankCopy);
        BufferCopy countCopy = new(0, rankCopy.Size, 4);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, counts[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &countCopy);
        if (hzbEnabled)
        {
            BufferCopy seedCopy = new(0, rankCopy.Size + 4, rankCopy.Size);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, seeds[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &seedCopy);
        }
        BufferCopy commandsCopy = new(0, commandOffset, arguments[lastSlot].SizeBytes);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, arguments[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &commandsCopy);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Readback command end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, DebugName: "visibility.qualification")).Success, "Readback submit failed.");
        uint[] data = MemoryMarshal.Cast<byte, uint>(staging.ReadBytes((int)staging.SizeBytes)).ToArray();
        Vector4[] planes = Planes(lastClip);
        bool[] reference = candidates.Select(candidate => CpuVisible(candidate, planes, lastEye, lastMode)).ToArray();
        int accepted = 0;
        long submittedTriangles = 0;
        int commandStart = checked((int)(commandOffset / 4));
        for (int index = 0; index < candidates.Length; index++)
        {
            uint rank = data[index];
            if ((rank & 255) == 0) continue;
            var candidate = candidates[index];
            var range = (rank >> 8) switch
            {
                1 => candidate.Level1,
                2 => candidate.Level2,
                3 => candidate.Level3,
                _ => new LodRange(0, candidate.FirstIndex, candidate.IndexCount, 0),
            };
            int offset = commandStart + accepted * 5;
            Require(data[offset] == range.IndexCount && data[offset + 1] == 1
                && data[offset + 2] == range.FirstIndex && data[offset + 3] == 0
                && data[offset + 4] == candidate.FirstInstance, "Compacted indirect command disagrees with stable selected geometry.");
            submittedTriangles += data[offset] / 3;
            accepted++;
        }
        Require(accepted == data[candidates.Length], "Indirect count disagrees with accepted candidates.");
        return new(candidates.Length, data[candidates.Length], data[..candidates.Length], reference)
        {
            SelectedLods = data[..candidates.Length].Select(rank => rank >> 8).ToArray(),
            SubmittedTriangles = submittedTriangles,
            SeedMask = lastUsedHzb ? data[(candidates.Length + 1)..(candidates.Length * 2 + 1)] : [],
            CandidateSpheres = lastUsedHzb ? candidates.Select(candidate => candidate.Sphere).ToArray() : [],
        };
    }

    private static bool CpuVisible(Candidate candidate, Vector4[] planes, Vector3 eye, ClusterVisibilityMode mode)
    {
        bool Inside(Vector4 sphere)
        {
            foreach (Vector4 plane in planes)
            {
                double distance = (double)plane.X * sphere.X + (double)plane.Y * sphere.Y
                    + (double)plane.Z * sphere.Z + plane.W + sphere.W;
                if (distance < -.0001) return false;
            }
            return true;
        }
        if (mode >= ClusterVisibilityMode.InstanceFrustum && !Inside(candidate.InstanceSphere)) return false;
        if (mode >= ClusterVisibilityMode.ClusterFrustum && !Inside(candidate.Sphere)) return false;
        if (mode >= ClusterVisibilityMode.ClusterAndCone && candidate.Cone.W >= -1)
        {
            double x = (double)eye.X - candidate.Sphere.X;
            double y = (double)eye.Y - candidate.Sphere.Y;
            double z = (double)eye.Z - candidate.Sphere.Z;
            double facing = candidate.Cone.X * x + candidate.Cone.Y * y + candidate.Cone.Z * z;
            if (facing <= candidate.Cone.W * Math.Sqrt(x * x + y * y + z * z) - candidate.Sphere.W - .0001) return false;
        }
        return true;
    }

    internal static Vector4[] Planes(Matrix4x4 matrix)
    {
        Vector4 x = new(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        Vector4 y = new(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        Vector4 z = new(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        Vector4 w = new(matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        return new[] { w + x, w - x, w + y, w - y, z, w - z }
            .Select(plane =>
            {
                float length = new Vector3(plane.X, plane.Y, plane.Z).Length();
                if (!float.IsFinite(length) || length < 1e-15f)
                {
                    // A constant/degenerate clip inequality must never introduce a false-negative cull.
                    return new Vector4(0, 0, 0, 1);
                }
                Vector4 normalized = plane / length;
                return float.IsFinite(normalized.W) ? normalized : new Vector4(0, 0, 0, 1);
            }).ToArray();
    }

    private static bool IsSimilarity(Matrix4x4 matrix)
    {
        Vector3 x = new(matrix.M11, matrix.M12, matrix.M13);
        Vector3 y = new(matrix.M21, matrix.M22, matrix.M23);
        Vector3 z = new(matrix.M31, matrix.M32, matrix.M33);
        float scale = x.LengthSquared();
        float tolerance = scale * 1e-5f;
        return Math.Abs(scale - y.LengthSquared()) < tolerance && Math.Abs(scale - z.LengthSquared()) < tolerance
            && Math.Abs(Vector3.Dot(x, y)) < tolerance && Math.Abs(Vector3.Dot(x, z)) < tolerance && Math.Abs(Vector3.Dot(y, z)) < tolerance;
    }

    public void ConfigureHzb(VulkanHzbPyramid3D pyramid)
    {
        if (!hzbEnabled) throw new InvalidOperationException("AUR-HZB-003: Culler has no HZB program.");
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            for (int variant = 0; variant < 2; variant++)
            {
                for (int phase = 0; phase < 3; phase++)
                {
                    Write(sets[slot, variant, phase], 5, variant == 0 ? pyramid.History : pyramid.Seed);
                    Write(sets[slot, variant, phase], 6, pyramid.View(slot, seedPhase: variant == 0));
                }
            }
        }
    }

    private void Write(DescriptorSet set, uint binding, AurelianVulkanBuffer buffer)
    {
        DescriptorBufferInfo info = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
        WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &info };
        plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
    }

    private void CreatePipelines(ClusterVisibilityPrograms programs, byte[]? hzbCull)
    {
        uint bindingCount = 8;
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[(int)bindingCount];
        for (uint index = 0; index < bindingCount; index++)
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        DescriptorSetLayoutCreateInfo setInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = bindingCount, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &setInfo, null, out setLayout));
        int variants = hzbEnabled ? 2 : 1;
        uint setCount = (uint)(VulkanFrameInFlightSet.Count * variants * 3);
        DescriptorPoolSize size = new(DescriptorType.StorageBuffer, setCount * bindingCount);
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = setCount, PoolSizeCount = 1, PPoolSizes = &size };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
        DescriptorSetLayout nativeSetLayout = setLayout;
        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1, PSetLayouts = &nativeSetLayout };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, out layout));
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            for (int variant = 0; variant < variants; variant++)
            {
                AurelianVulkanBuffer[][] resources = [
                    [inputs[slot], ranks[slot], blocks[slot], camera[slot, variant]],
                    [blocks[slot], offsets[slot], counts[slot], camera[slot, variant]],
                    [inputs[slot], ranks[slot], offsets[slot], arguments[slot]],
                ];
                for (int phase = 0; phase < 3; phase++)
                {
                    DescriptorSetAllocateInfo allocation = new() { SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &nativeSetLayout };
                    DescriptorSet set;
                    Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
                    sets[slot, variant, phase] = set;
                    for (uint binding = 0; binding < 4; binding++) Write(set, binding, resources[phase][binding]);
                    for (uint binding = 4; binding < 8; binding++) Write(set, binding, lodState);
                    if (hzbEnabled) Write(set, 4, seeds[slot]);
                }
            }
        }
        byte[][] sources = hzbCull is null
            ? [programs.Cull, programs.Scan, programs.Scatter]
            : [programs.Cull, programs.Scan, programs.Scatter, hzbCull];
        string[] entries = ["VisibilityCullMain", "VisibilityScanMain", "VisibilityScatterMain", "HzbCullMain"];
        for (int phase = 0; phase < sources.Length; phase++)
        {
            ShaderModule module;
            fixed (byte* bytes = sources[phase])
            {
                ShaderModuleCreateInfo moduleInfo = new() { SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)sources[phase].Length, PCode = (uint*)bytes };
                Check(plant.Vk.CreateShaderModule(plant.Device, &moduleInfo, null, &module));
            }
            nint name = SilkMarshal.StringToPtr(entries[phase]);
            try
            {
                ComputePipelineCreateInfo info = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                    Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit,
                        Module = module, PName = (byte*)name } };
                fixed (Pipeline* output = &pipelines[phase]) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &info, null, output));
            }
            finally
            {
                SilkMarshal.Free(name);
                plant.Vk.DestroyShaderModule(plant.Device, module, null);
            }
        }
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory = VulkanMemoryUsage.GpuOnly)
    {
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "visibility", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        Require(created.Success, "Buffer allocation failed.");
        buffers.Add(created.Buffer!);
        return created.Buffer!;
    }

    private AurelianVulkanBuffer BufferForReadback(ulong bytes)
    {
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "visibility.readback", MapOnCreate: true));
        Require(created.Success, "Readback allocation failed.");
        return created.Buffer!;
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags before, AccessFlags after,
        PipelineStageFlags source, PipelineStageFlags destination)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, source, destination, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Check(Result result) => Require(result == Result.Success, result.ToString());
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AUR-VIS-004: " + message);
    }

    public void Dispose()
    {
        foreach (Pipeline pipeline in pipelines)
        {
            if (pipeline.Handle != 0)
            {
                plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
            }
        }
        if (layout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, layout, null);
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }
    }
}
