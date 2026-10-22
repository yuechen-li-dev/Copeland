using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record ParticleBudget3D(int Emitters, int ReservedParticles, int RequestedLiveParticles,
    int OverBudgetParticles, int UploadedBytes);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeParticleVertex3D(Vector3 Position, Vector4 UvKindSoftness, Vector4 Color,
    Vector4 CentreRadius, Vector4 Detail, Vector4 Glow)
{
    /// <summary>Bytes per vertex in the GPU buffer: one float3 and five float4 values, tightly packed.</summary>
    public const int Stride = 92;
}

/// <summary>Bounded analytic particle motion and GPU billboard expansion. The scene command
/// records dispatch and draw; normal rendering has no particle readback or separate submission.</summary>
public sealed unsafe class VulkanParticles3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer emitters;
    private readonly AurelianVulkanBuffer view;
    private readonly bool readback;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct EmitterWords
    {
        public Vector4 OriginTime;
        public Vector4 DirectionLifetime;
        public Vector4 Shape;
        public Vector4 Color;
        public Vector4 Emission;
        public Vector4 Glow;
        public uint Seed;
        public uint Kind;
        public uint Slots;
        public uint Reserved;
    }

    /// <summary>Bytes per packed emitter command: six float4 values and four u32 values.</summary>
    public const int EmitterBytes = 112;

    public int MaximumEmitters { get; }
    public int ParticlesPerEmitter { get; }
    public AurelianVulkanBuffer Vertices { get; }
    public uint VertexCount { get; private set; }
    public int DispatchCount { get; private set; }
    public ParticleBudget3D Inspection { get; private set; } = new(0, 0, 0, 0, 0);

    public VulkanParticles3D(AurelianVulkanPlant plant, byte[] spirv,
        int maximumEmitters = 16, int particlesPerEmitter = 512, bool enableQualificationReadback = false)
    {
        if (maximumEmitters is < 1 or > 16 || particlesPerEmitter is < 1 or > 8192
            || spirv.Length < 20 || spirv.Length % 4 != 0)
        {
            throw new ArgumentException("AUR-VFX-002: Particle pools require bounded capacities and aligned SPIR-V.");
        }
        this.plant = plant;
        MaximumEmitters = maximumEmitters;
        ParticlesPerEmitter = particlesPerEmitter;
        readback = enableQualificationReadback;
        allocator = new(plant);
        try
        {
            emitters = Buffer((ulong)maximumEmitters * EmitterBytes, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            view = Buffer(32, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Vertices = Buffer((ulong)maximumEmitters * (ulong)particlesPerEmitter * 6 * NativeParticleVertex3D.Stride,
                VulkanBufferUsage.Storage | VulkanBufferUsage.Vertex,
                readback ? VulkanMemoryUsage.GpuToCpu : VulkanMemoryUsage.GpuOnly);
            CreatePipeline(spirv, [emitters, view, Vertices]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Configure(IReadOnlyList<ParticleEmitter3D> source, Matrix4x4 clip)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source.Count > MaximumEmitters || source.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != source.Count)
        {
            throw new ArgumentException("AUR-VFX-003: Emitter identities must be unique and fit the pool; excess emitters are rejected.");
        }
        var right = Vector3.Normalize(new(clip.M11, clip.M21, clip.M31));
        var up = Vector3.Normalize(-new Vector3(clip.M12, clip.M22, clip.M32));
        if (!float.IsFinite(right.LengthSquared()) || !float.IsFinite(up.LengthSquared()))
        {
            throw new ArgumentException("AUR-VFX-004: Billboards require a finite nondegenerate Vulkan camera.");
        }
        var words = new EmitterWords[source.Count];
        int requested = 0;
        int overflow = 0;
        for (int index = 0; index < source.Count; index++)
        {
            ParticleEmitter3D emitter = source[index];
            emitter.Validate();
            int demand = (int)MathF.Ceiling(emitter.Rate * emitter.Lifetime);
            int slots = Math.Min(ParticlesPerEmitter, demand);
            requested += demand;
            overflow += demand - slots;
            words[index] = new()
            {
                OriginTime = new(emitter.Position, emitter.ElapsedSeconds),
                DirectionLifetime = new(Vector3.Normalize(emitter.Direction), emitter.Lifetime),
                Shape = new(Math.Min(emitter.Rate, slots / emitter.Lifetime), emitter.Speed, emitter.Radius, emitter.Size),
                Color = emitter.Color,
                // Rain uses the existing kind-specific rows for shutter, incident light and its floor;
                // emitter/vertex strides and every pre-existing kind retain their ABI.
                Emission = new(emitter.EmitUntilSeconds, emitter.SoftIntersectionDistance, emitter.Turbulence,
                    emitter.Kind == ParticleEffectKind3D.Rain ? emitter.RainShutterSeconds : emitter.Temperature),
                Glow = emitter.Kind == ParticleEffectKind3D.Rain
                    ? new(emitter.RainIncidentRadiance, emitter.RainGroundHeight)
                    : new(emitter.Glow, emitter.GlowHeight),
                Seed = emitter.Seed, Kind = (uint)emitter.Kind, Slots = (uint)slots,
            };
        }
        VertexCount = (uint)(source.Count * ParticlesPerEmitter * 6);
        Inspection = new(source.Count, source.Count * ParticlesPerEmitter, requested, overflow,
            source.Count == 0 ? 0 : source.Count * EmitterBytes + 32);
        if (source.Count == 0) { return; }
        Write(emitters, MemoryMarshal.AsBytes(words.AsSpan()));
        Vector4[] camera = [new(right, source.Count * ParticlesPerEmitter), new(up, ParticlesPerEmitter)];
        Write(view, MemoryMarshal.AsBytes(camera.AsSpan()));
    }

    internal void Record(VulkanCommandBufferLease command)
    {
        if (VertexCount == 0) { return; }
        // Previous draw's vertex reads must finish before this frame replaces billboard data.
        Barrier(command.CommandBuffer, AccessFlags.VertexAttributeReadBit | AccessFlags.HostWriteBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.VertexInputBit | PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command.CommandBuffer, (VertexCount / 6 + 63) / 64, 1, 1);
        Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit, AccessFlags.VertexAttributeReadBit | AccessFlags.HostReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.VertexInputBit | PipelineStageFlags.HostBit);
        DispatchCount++;
    }

    internal void ValidatePlant(AurelianVulkanPlant expected)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(plant, expected))
        {
            throw new ArgumentException("AUR-VFX-005: Particle buffers belong to a different Vulkan plant.");
        }
    }

    /// <summary>Explicit qualification only; call after the renderer has completed its submission.</summary>
    public NativeParticleVertex3D[] ReadVerticesForQualification()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!readback) { throw new InvalidOperationException("AUR-VFX-006: Normal particle pools are GPU-only."); }
        if (VertexCount == 0) { return []; }
        return MemoryMarshal.Cast<byte, NativeParticleVertex3D>(Vertices.ReadBytes(checked((int)VertexCount * NativeParticleVertex3D.Stride))).ToArray();
    }

    private void CreatePipeline(byte[] spirv, AurelianVulkanBuffer[] resources)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint index = 0; index < 3; index++)
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings
        };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, 3);
        var pool = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &size
        };
        fixed (DescriptorPool* value = &descriptorPool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        DescriptorSetLayout descriptorLayout = setLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &descriptorLayout
        };
        fixed (DescriptorSet* value = &descriptorSet) Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, value));
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[3];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[3];
        for (int index = 0; index < 3; index++)
        {
            infos[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = (uint)index,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &infos[index]
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 3, writes, 0, null);
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorLayout
        };
        fixed (PipelineLayout* value = &pipelineLayout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, value));
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)bytes
            };
            Check(plant.Vk.CreateShaderModule(plant.Device, &moduleInfo, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr("ParticleMain");
        try
        {
            var create = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Layout = pipelineLayout,
                Stage = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = (byte*)entry
                }
            };
            fixed (Pipeline* value = &pipeline) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory)
    {
        var result = VulkanBufferFactory.Create(plant, allocator,
            new(plant.Context.Id, bytes, usage, memory, "particles3d", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private void Barrier(CommandBuffer command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, ReadOnlySpan<byte> bytes)
    {
        Require(buffer.Write(bytes).Success, "Particle command upload failed.");
    }

    private static void Check(Result result) => Require(result == Result.Success, "Particle Vulkan operation failed: " + result);
    private static void Require(bool success, string message)
    {
        if (!success) { throw new InvalidOperationException(message); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        if (pipeline.Handle != 0) { plant.Vk.DestroyPipeline(plant.Device, pipeline, null); }
        if (pipelineLayout.Handle != 0) { plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null); }
        if (descriptorPool.Handle != 0) { plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null); }
        if (setLayout.Handle != 0) { plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null); }
        foreach (var buffer in buffers.AsEnumerable().Reverse()) { buffer.Dispose(); }
        allocator.Dispose();
    }
}
