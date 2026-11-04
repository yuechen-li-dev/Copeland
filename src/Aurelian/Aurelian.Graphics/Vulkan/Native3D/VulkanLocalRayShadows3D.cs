using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>GPU-generated per-pixel local-light visibility, recorded on the ordinary frame command buffer.
/// Only immutable scene/light admission crosses the CPU boundary; no per-ray readback or submission.</summary>
internal sealed unsafe class VulkanLocalRayShadows3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanSceneAcceleration3D scene;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer[] parameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[,] sets = new DescriptorSet[VulkanFrameInFlightSet.Count, 2];
    private readonly AurelianVulkanBuffer rays, hits, motion, visibility, normals;
    private readonly uint width, height;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private Pipeline sampledPipeline;
    private Pipeline spatialPipeline;
    private readonly byte[]? spatialProgram;
    private AurelianVulkanBuffer candidates = null!;
    private Matrix4x4 previousInverse = Matrix4x4.Identity;
    private Matrix4x4 pendingInverse = Matrix4x4.Identity;
    private readonly byte[]? sampledProgram;
    private AurelianVulkanBuffer historyA = null!;
    private AurelianVulkanBuffer historyB = null!;
    private LocalLightSampling3D sampling = new();
    private float[] priorLights = [];
    private uint sequence;
    private bool historyValid;
    private bool disposed;
    public AurelianVulkanTexture Output { get; }
    public SceneAccelerationStatistics3D Statistics => scene.Statistics;

    public VulkanLocalRayShadows3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanFrameInFlightSet frames, byte[] program, uint width, uint height, byte[]? sampledProgram = null, byte[]? spatialProgram = null)
    {
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        ulong pixels = (ulong)width * height;
        if (width == 0 || height == 0 || (ulong)width * 4 > properties.Limits.MaxImageDimension2D
            || height > properties.Limits.MaxImageDimension2D
            || pixels > properties.Limits.MaxStorageBufferRange / 64UL
            || (pixels + 63) / 64 > properties.Limits.MaxComputeWorkGroupCount[0])
        {
            throw new NotSupportedException($"AUR-RAY-SHADOW-003: The {width}x{height} local shadow target exceeds this device's image, storage-buffer or compute-dispatch limits.");
        }
        this.plant = plant;
        this.allocator = allocator;
        this.frames = frames;
        this.sampledProgram = sampledProgram;
        this.spatialProgram = spatialProgram;
        this.width = width;
        this.height = height;
        scene = new(plant);
        try
        {
            rays = Buffer((4 + pixels * 12) * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination);
            normals = Buffer(pixels * 8, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination);
            hits = Buffer(pixels * 4, VulkanBufferUsage.Storage);
            motion = Buffer(pixels * 16, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination | VulkanBufferUsage.TransferSource);
            visibility = Buffer(pixels * 64, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
            for (int slot = 0; slot < parameters.Length; slot++)
                parameters[slot] = Buffer((64 + 256 * 16) * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Output = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width * 4, height,
                VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
                "local-ray-visibility", VulkanTextureFormat.Rgba32Float);
            historyA = Buffer(16, VulkanBufferUsage.Storage);
            historyB = historyA;
            candidates = historyA;
            CreatePipeline(program);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Prepare(NativeInstanceSet3D instances, Matrix4x4 inverse, ReadOnlySpan<float> lights, int count, LocalLightSampling3D options, SceneAccelerationUpdateMode3D updateMode)
    {
        options.Validate(checked((int)(width * height)));
        if (options.Mode != LocalLightSamplingMode3D.Exact && sampledPipeline.Handle == 0)
        {
            if (sampledProgram is null) throw new NotSupportedException("AUR-LIGHT-SAMPLING-002: The sampled local-light compute program was not configured.");
            CreateComputePipeline(sampledProgram, ref sampledPipeline);
        }
        if ((options.Mode == LocalLightSamplingMode3D.TemporalReservoir || options.Mode >= LocalLightSamplingMode3D.ReprojectedReservoir) && historyA.SizeBytes < (ulong)width * height * 96)
        {
            plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
            if ((ulong)width * height * 96 > properties.Limits.MaxStorageBufferRange)
                throw new NotSupportedException("AUR-LIGHT-SAMPLING-003: Temporal reservoirs exceed the device storage-buffer range.");
            frames.Drain();
            historyA = Buffer((ulong)width * height * 96, VulkanBufferUsage.Storage);
            historyB = Buffer((ulong)width * height * 96, VulkanBufferUsage.Storage);
            historyValid = false;
        }
        if (options.Mode is LocalLightSamplingMode3D.SpatiotemporalReservoir or LocalLightSamplingMode3D.SpatialReservoir)
        {
            if (spatialPipeline.Handle == 0)
            {
                if (spatialProgram is null) throw new NotSupportedException("AUR-RESTIR-001: Spatial/reprojected reservoirs require RestirSpatial3D.");
                CreateComputePipeline(spatialProgram, ref spatialPipeline);
            }
            if (candidates.SizeBytes < (ulong)width * height * 96)
            {
                frames.Drain();
                candidates = Buffer((ulong)width * height * 96, VulkanBufferUsage.Storage);
            }
        }
        if (options != sampling || !lights.SequenceEqual(priorLights))
        {
            ResetSampling();
            sampling = options;
            priorLights = lights.ToArray();
        }
        if (scene.RequiresRebuild(instances, updateMode))
        {
            frames.Drain();
        }
        scene.Prepare(instances, frames.Slot, updateMode);
        var words = new float[64 + 256 * 16];
        Lighting3DUniforms.Rows(inverse).CopyTo(words, 0);
        words[16] = width;
        words[17] = height;
        words[18] = count;
        words[19] = .0002f;
        words[20] = BitConverter.UInt32BitsToSingle(uint.MaxValue);
        words[21] = BitConverter.UInt32BitsToSingle(uint.MaxValue);
        words[22] = .0005f;
        words[23] = (float)options.Mode;
        words[24] = options.FrameRayBudget;
        words[25] = options.ExactLights;
        words[26] = options.Candidates;
        words[27] = options.HistoryLimit;
        words[28] = BitConverter.UInt32BitsToSingle(sequence);
        words[29] = historyValid ? 1 : 0;
        words[35] = BitConverter.UInt32BitsToSingle(options.Seed);
        words[36] = options.SpatialNeighbors;
        words[37] = options.SpatialRadius;
        words[38] = options.ReusePlaneTolerance;
        words[39] = options.ReuseNormalCosine;
        words[56] = options.SpatialTargetMis ? 1 : 0;
        words[57] = options.ImportanceProposals ? 1 : 0;
        Lighting3DUniforms.Rows(previousInverse).CopyTo(words, 40);
        pendingInverse = inverse;
        lights.CopyTo(words.AsSpan(64));
        if (!parameters[frames.Slot].Write(MemoryMarshal.AsBytes(words.AsSpan())).Success)
            throw new InvalidOperationException("Ray shadow parameters could not be uploaded.");
        AccelerationStructureKHR top = scene.TopLevel;
        var structureWrite = new WriteDescriptorSetAccelerationStructureKHR
        {
            SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
            AccelerationStructureCount = 1, PAccelerationStructures = &top,
        };
        DescriptorBufferInfo* info = stackalloc DescriptorBufferInfo[10];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[11];
        for (int stage = 0; stage < 2; stage++)
        {
            AurelianVulkanBuffer[] resources = [scene.Procedurals, rays, hits, scene.Instances,
                parameters[frames.Slot], motion, visibility, normals,
                stage == 0 ? (sequence % 2 == 0 ? historyA : historyB) : candidates,
                stage == 0 && options.Mode is (LocalLightSamplingMode3D.SpatiotemporalReservoir or LocalLightSamplingMode3D.SpatialReservoir)
                    ? candidates : (sequence % 2 == 0 ? historyB : historyA)];
            writes[0] = new() { SType = StructureType.WriteDescriptorSet, PNext = &structureWrite,
                DstSet = sets[frames.Slot, stage], DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.AccelerationStructureKhr };
            for (int index = 0; index < resources.Length; index++)
            {
                info[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
                writes[index + 1] = new() { SType = StructureType.WriteDescriptorSet, DstSet = sets[frames.Slot, stage],
                    DstBinding = (uint)index + 1, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = &info[index] };
            }
            plant.Vk.UpdateDescriptorSets(plant.Device, 11, writes, 0, null);
        }
    }

    public void Record(VulkanCommandBufferLease command, AurelianVulkanTexture source, AurelianVulkanTexture normal)
    {
        scene.RecordUpdate(command);
        // Previous frames use these GPU-only scratch buffers on this same queue.
        Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferReadBit,
            AccessFlags.TransferWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit, PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit);
        Transition(command, source, VulkanResourceLayout.TransferSource);
        BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new(width, height, 1) };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, source.NativeImage, ImageLayout.TransferSrcOptimal, motion.NativeBuffer, 1, &copy);
        Transition(command, source, VulkanResourceLayout.ShaderResourceFragment);
        Transition(command, normal, VulkanResourceLayout.TransferSource);
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, normal.NativeImage, ImageLayout.TransferSrcOptimal, normals.NativeBuffer, 1, &copy);
        Transition(command, normal, VulkanResourceLayout.ShaderResourceFragment);
        uint pixels = width * height;
        plant.Vk.CmdUpdateBuffer(command.CommandBuffer, rays.NativeBuffer, 0, 4, &pixels);
        Barrier(command, AccessFlags.TransferWriteBit | AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, sampling.Mode == LocalLightSamplingMode3D.Exact ? pipeline : sampledPipeline);
        DescriptorSet set = sets[frames.Slot, 0];
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command.CommandBuffer, (pixels + 63) / 64, 1, 1);
        if (sampling.Mode is LocalLightSamplingMode3D.SpatiotemporalReservoir or LocalLightSamplingMode3D.SpatialReservoir)
        {
            Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, spatialPipeline);
            set = sets[frames.Slot, 1];
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, (pixels + 63) / 64, 1, 1);
        }
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        Transition(command, Output, VulkanResourceLayout.TransferDestination);
        copy.ImageExtent = new(width * 4, height, 1);
        plant.Vk.CmdCopyBufferToImage(command.CommandBuffer, visibility.NativeBuffer, Output.NativeImage, ImageLayout.TransferDstOptimal, 1, &copy);
        Transition(command, Output, VulkanResourceLayout.ShaderResourceFragment);
        previousInverse = pendingInverse;
        sequence++;
        historyValid = true;
    }

    private static void Transition(AurelianVulkanPlant plant, VulkanCommandBufferLease command, AurelianVulkanTexture texture, VulkanResourceLayout layout)
    {
        var result = texture.LayoutTracker.Transition("ray-shadows", 0, 0, layout);
        if (!result.Success || result.Plan is not null && !VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, result.Plan)]).Success)
            throw new InvalidOperationException("Ray shadow texture transition failed.");
    }
    private void Transition(VulkanCommandBufferLease command, AurelianVulkanTexture texture, VulkanResourceLayout layout)
        => Transition(plant, command, texture, layout);

    private void Barrier(VulkanCommandBufferLease command, AccessFlags before, AccessFlags after, PipelineStageFlags source, PipelineStageFlags destination)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, source, destination, 0, 1, &barrier, 0, null, 0, null);
    }

    private void CreatePipeline(byte[] spirv)
    {
        const uint bindingCount = 11;
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[(int)bindingCount];
        for (uint index = 0; index < bindingCount; index++) bindings[index] = new(index,
            index == 0 ? DescriptorType.AccelerationStructureKhr : DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        var layout = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = bindingCount, PBindings = bindings };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new(DescriptorType.AccelerationStructureKhr, VulkanFrameInFlightSet.Count * 2);
        sizes[1] = new(DescriptorType.StorageBuffer, 20 * VulkanFrameInFlightSet.Count);
        var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = VulkanFrameInFlightSet.Count * 2, PoolSizeCount = 2, PPoolSizes = sizes };
        fixed (DescriptorPool* value = &descriptorPool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        DescriptorSetLayout descriptorLayout = setLayout;
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool, DescriptorSetCount = 1, PSetLayouts = &descriptorLayout };
            for (int stage = 0; stage < 2; stage++)
                Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out sets[slot, stage]));
        }
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1, PSetLayouts = &descriptorLayout };
        fixed (PipelineLayout* value = &pipelineLayout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineLayoutInfo, null, value));
        CreateComputePipeline(spirv, ref pipeline);
    }

    private void CreateComputePipeline(byte[] spirv, ref Pipeline destination)
    {
        ShaderModule module;
        fixed (byte* code = spirv)
        {
            var shader = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
            Check(plant.Vk.CreateShaderModule(plant.Device, &shader, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr("LocalShadowMain");
        try
        {
            var create = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)entry },
                Layout = pipelineLayout,
            };
            fixed (Pipeline* value = &destination) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    public void ResetSampling()
    {
        sequence = 0;
        historyValid = false;
    }

    public float[] ReadMotionForQualification() => Vulkan3DQualificationReadback.ReadBuffer(plant, frames, motion);

    public float[] ReadSamplesForQualification() => Vulkan3DQualificationReadback.ReadBuffer(plant, frames, visibility);

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory = VulkanMemoryUsage.GpuOnly)
    {
        var result = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "ray-shadow-buffer", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        if (!result.Success) throw new InvalidOperationException("Ray shadow buffer allocation failed.");
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("Ray shadow Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (spatialPipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, spatialPipeline, null);
        if (sampledPipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, sampledPipeline, null);
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (pipelineLayout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        if (descriptorPool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        Output?.Dispose();
        foreach (var buffer in buffers.AsEnumerable().Reverse()) buffer.Dispose();
        scene.Dispose();
    }
}
