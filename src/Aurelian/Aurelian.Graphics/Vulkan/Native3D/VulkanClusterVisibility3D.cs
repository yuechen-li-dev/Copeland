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
public sealed record ClusterVisibilityReadback(int Candidates, uint DrawCount, uint[] Ranks, bool[] CpuVisible);

/// <summary>Resident cluster inputs and stable GPU compaction, consumed by the existing material pipeline.</summary>
internal sealed unsafe class VulkanClusterVisibility3D : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Candidate(Vector4 InstanceSphere, Vector4 Sphere, Vector4 Cone,
        uint FirstIndex, uint IndexCount, uint FirstInstance, uint Reserved);

    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly Candidate[] candidates;
    private readonly AurelianVulkanBuffer input;
    private readonly AurelianVulkanBuffer[] camera = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] ranks = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] blocks = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] offsets = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] arguments = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] counts = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[,] sets = new DescriptorSet[VulkanFrameInFlightSet.Count, 3];
    private readonly Pipeline[] pipelines = new Pipeline[3];
    private DescriptorSetLayout setLayout;
    private DescriptorPool pool;
    private PipelineLayout layout;
    private readonly uint blockCount;
    private int lastSlot;
    private Matrix4x4 lastClip;
    private Vector3 lastEye;
    private ClusterVisibilityMode lastMode;

    public AurelianVulkanBuffer Arguments => arguments[frames.Slot];
    public AurelianVulkanBuffer Count => counts[frames.Slot];
    public uint MaximumDrawCount => (uint)candidates.Length;

    public VulkanClusterVisibility3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        ClusterVisibilityPrograms programs, ImmutableArray<SceneInstance3D> instances, uint firstInstance)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.commands = commands;
        this.fences = fences;
        this.frames = frames;
        var mesh = instances[0].Mesh;
        var clusters = NativeMeshClusters3D.Build(mesh);
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
        int index = 0;
        for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
        {
            var instance = instances[instanceIndex];
            Vector4 instanceSphere = NativeMeshClusters3D.TransformSphere(new(center, radius), instance.Transform);
            foreach (var cluster in clusters)
            {
                Vector4 cone = new(0, 0, 0, -2);
                if (!instance.Material.DoubleSided && cluster.Cone.W >= -1 && IsSimilarity(instance.Transform))
                {
                    Vector3 axis = Vector3.Normalize(Vector3.TransformNormal(new(cluster.Cone.X, cluster.Cone.Y, cluster.Cone.Z), instance.Transform));
                    cone = new(axis, cluster.Cone.W);
                }
                candidates[index++] = new(instanceSphere, NativeMeshClusters3D.TransformSphere(cluster.Sphere, instance.Transform),
                    cone, cluster.FirstIndex, cluster.IndexCount, firstInstance + (uint)instanceIndex, 0);
            }
        }
        blockCount = ((uint)candidates.Length + 63) / 64;
        try
        {
            input = Buffer((ulong)candidates.Length * 64, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination);
            using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
            var upload = uploader.Upload(new(input, MemoryMarshal.AsBytes(candidates.AsSpan()).ToArray(), DebugName: "visibility.candidates"));
            Require(upload.Success, "Candidate upload failed.");
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            {
                camera[slot] = Buffer(128, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                ranks[slot] = Buffer(blockCount * 64UL * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
                blocks[slot] = Buffer(blockCount * 4UL, VulkanBufferUsage.Storage);
                offsets[slot] = Buffer(blockCount * 4UL, VulkanBufferUsage.Storage);
                arguments[slot] = Buffer((ulong)candidates.Length * 20, VulkanBufferUsage.Storage | VulkanBufferUsage.Indirect);
                counts[slot] = Buffer(4, VulkanBufferUsage.Storage | VulkanBufferUsage.Indirect | VulkanBufferUsage.TransferSource);
            }
            CreatePipelines(programs);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Record(VulkanCommandBufferLease command, Matrix4x4 clip, Vector3 eye, ClusterVisibilityMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        lastSlot = frames.Slot;
        lastClip = clip;
        lastEye = eye;
        lastMode = mode;
        Vector4[] parameters = [.. Planes(clip), new(eye, 1), Vector4.Zero];
        Span<byte> parameterBytes = MemoryMarshal.AsBytes(parameters.AsSpan());
        Span<uint> metadata = MemoryMarshal.Cast<byte, uint>(parameterBytes[112..]);
        metadata[0] = (uint)candidates.Length;
        metadata[1] = blockCount;
        metadata[2] = (uint)mode;
        Require(camera[frames.Slot].Write(parameterBytes).Success, "Camera upload failed.");
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
            DescriptorSet set = sets[frames.Slot, phase];
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipelines[phase]);
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, groups, 1, 1);
        }
    }

    public ClusterVisibilityReadback ReadForQualification()
    {
        frames.Drain();
        using var staging = BufferForReadback((ulong)candidates.Length * 4 + 4);
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Readback command begin failed.");
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        BufferCopy rankCopy = new(0, 0, (ulong)candidates.Length * 4);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, ranks[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &rankCopy);
        BufferCopy countCopy = new(0, rankCopy.Size, 4);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, counts[lastSlot].NativeBuffer, staging.NativeBuffer, 1, &countCopy);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Readback command end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, DebugName: "visibility.qualification")).Success, "Readback submit failed.");
        uint[] data = MemoryMarshal.Cast<byte, uint>(staging.ReadBytes((int)staging.SizeBytes)).ToArray();
        Vector4[] planes = Planes(lastClip);
        bool[] reference = candidates.Select(candidate => CpuVisible(candidate, planes, lastEye, lastMode)).ToArray();
        return new(candidates.Length, data[^1], data[..^1], reference);
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

    private void CreatePipelines(ClusterVisibilityPrograms programs)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint index = 0; index < 4; index++)
        {
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        }
        DescriptorSetLayoutCreateInfo setInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 4, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &setInfo, null, out setLayout));
        DescriptorPoolSize size = new(DescriptorType.StorageBuffer, 24);
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 6, PoolSizeCount = 1, PPoolSizes = &size };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
        DescriptorSetLayout nativeSetLayout = setLayout;
        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &nativeSetLayout };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, out layout));
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            AurelianVulkanBuffer[][] resources = [
                [input, ranks[slot], blocks[slot], camera[slot]],
                [blocks[slot], offsets[slot], counts[slot], camera[slot]],
                [input, ranks[slot], offsets[slot], arguments[slot]],
            ];
            for (int phase = 0; phase < 3; phase++)
            {
                DescriptorSetAllocateInfo allocation = new() { SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &nativeSetLayout };
                DescriptorSet set;
                Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
                sets[slot, phase] = set;
                for (uint binding = 0; binding < 4; binding++)
                {
                    var buffer = resources[phase][binding];
                    DescriptorBufferInfo bufferInfo = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
                    WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set,
                        DstBinding = binding, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &bufferInfo };
                    plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
                }
            }
        }
        byte[][] sources = [programs.Cull, programs.Scan, programs.Scatter];
        string[] entries = ["VisibilityCullMain", "VisibilityScanMain", "VisibilityScatterMain"];
        for (int phase = 0; phase < 3; phase++)
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
