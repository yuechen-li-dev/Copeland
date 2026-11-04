using Aurelian.Rendering.Contracts.Lighting;
using System.Numerics;
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
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Directional world-space diffuse cache on the shared retained scene.
/// All updates and receiver connectivity queries run on the ordinary frame queue.</summary>
internal sealed unsafe class VulkanDynamicDiffuse3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFrameInFlightSet frames;
    private readonly uint width, height;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly List<AurelianVulkanBuffer> cacheBuffers = [];
    private readonly AurelianVulkanBuffer[,] parameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count, 3];
    private readonly DescriptorSet[,] sets = new DescriptorSet[VulkanFrameInFlightSet.Count, 3];
    private readonly AurelianVulkanBuffer rays, hits, occlusion, output;
    private AurelianVulkanBuffer historyA = null!, historyB = null!, baseline = null!;
    private DynamicDiffuseLighting3D options = new();
    private CompiledDiffuseProbeField3D? compiled;
    private uint sequence;
    private int stationaryFrames;
    private NativeInstanceSet3D? captureScene;
    private float[] captureLights = [];
    private float captureResponseModel = float.NaN;
    private float captureEmissionIntensity = float.NaN;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;
    public AurelianVulkanTexture Output { get; }

    public VulkanDynamicDiffuse3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanFrameInFlightSet frames, byte[] program, uint width, uint height)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.frames = frames;
        this.width = width;
        this.height = height;
        // 512 prepared probes and at most 128 directions are the admitted contract.
        ulong capacity = Math.Max((ulong)width * height, 512 * 128);
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        if ((capacity * 12 + 4) * 4 > properties.Limits.MaxStorageBufferRange)
            throw new NotSupportedException("AUR-GI-002: Diffuse scratch exceeds the device storage-buffer range.");
        try
        {
            rays = Buffer((4 + capacity * 12) * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination);
            hits = Buffer(512 * 128 * 64, VulkanBufferUsage.Storage);
            occlusion = Buffer(capacity * 4, VulkanBufferUsage.Storage);
            output = Buffer((ulong)width * height * 16, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
                for (int stage = 0; stage < 3; stage++)
                    parameters[slot, stage] = Buffer((64 + 256 * 16) * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Output = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
                "dynamic-diffuse", VulkanTextureFormat.Rgba32Float);
            CreatePipeline(program);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetCompiled(CompiledDiffuseProbeField3D field)
    {
        field.Validate();
        frames.Drain();
        compiled = field with { Data = (float[])field.Data.Clone() };
        sequence = 0;
        if (options.Grid == field.Grid && baseline is not null) Write(baseline, compiled.Data);
    }

    public void Prepare(VulkanSceneAcceleration3D scene, NativeInstanceSet3D instances, Matrix4x4 inverse,
        ReadOnlySpan<float> lights, int count, DynamicDiffuseLighting3D settings, float responseModel, float emissionIntensity,
        AurelianVulkanBuffer motion, AurelianVulkanBuffer normals)
    {
        settings.Validate();
        var grid = settings.Grid!;
        if (settings.Mode is DynamicDiffuseMode3D.CompiledResidual or DynamicDiffuseMode3D.CompiledOnly
            && (compiled is null || compiled.Grid != grid))
            throw new InvalidOperationException("AUR-GI-003: Compiled modes require a matching fully captured probe field.");
        if (options.Grid != grid)
        {
            frames.Drain();
            foreach (var buffer in cacheBuffers) buffer.Dispose();
            cacheBuffers.Clear();
            ulong bytes = checked((ulong)grid.Count * (ulong)(grid.RaysPerProbe + 64) * 16);
            historyA = Cache(bytes, VulkanMemoryUsage.GpuOnly);
            historyB = Cache(bytes, VulkanMemoryUsage.GpuOnly);
            baseline = Cache(bytes, VulkanMemoryUsage.CpuToGpu);
            Write(baseline, compiled?.Grid == grid ? compiled.Data : new float[bytes / 4]);
            sequence = 0;
            stationaryFrames = 0;
        }
        if (options.Mode != settings.Mode || options.RaysPerProbePerFrame != settings.RaysPerProbePerFrame)
        {
            sequence = 0;
            stationaryFrames = 0;
        }
        if (!ReferenceEquals(captureScene, instances) || !lights.SequenceEqual(captureLights)
            || captureResponseModel != responseModel || captureEmissionIntensity != emissionIntensity)
        {
            stationaryFrames = 0;
            captureScene = instances;
            captureLights = lights.ToArray();
            captureResponseModel = responseModel;
            captureEmissionIntensity = emissionIntensity;
        }
        options = settings;
        scene.PrepareShading(instances, frames.Slot);
        var words = new float[64 + 256 * 16];
        Lighting3DUniforms.Rows(inverse).CopyTo(words, 0);
        words[16] = width;
        words[17] = height;
        words[18] = count;
        words[24] = grid.X;
        words[25] = grid.Y;
        words[26] = grid.Z;
        words[27] = grid.Count;
        words[28] = grid.RaysPerProbe;
        words[29] = settings.RaysPerProbePerFrame;
        words[30] = BitConverter.UInt32BitsToSingle(sequence);
        words[31] = (float)settings.Mode;
        words[32] = grid.Minimum.X;
        words[33] = grid.Minimum.Y;
        words[34] = grid.Minimum.Z;
        Vector3 spacing = (grid.Maximum - grid.Minimum) / new Vector3(grid.X - 1, grid.Y - 1, grid.Z - 1);
        words[36] = spacing.X;
        words[37] = spacing.Y;
        words[38] = spacing.Z;
        words[40] = settings.ConnectivityVisibility ? 1 : 0;
        words[41] = settings.Intensity;
        words[43] = DirectionStride(grid.RaysPerProbe);
        words[44] = responseModel;
        words[45] = emissionIntensity;
        lights.CopyTo(words.AsSpan(64));
        AccelerationStructureKHR top = scene.TopLevel;
        var structure = new WriteDescriptorSetAccelerationStructureKHR { SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
            AccelerationStructureCount = 1, PAccelerationStructures = &top };
        DescriptorBufferInfo* info = stackalloc DescriptorBufferInfo[13];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[14];
        for (int phase = 0; phase < 3; phase++)
        {
            words[39] = phase;
            Write(parameters[frames.Slot, phase], words);
            AurelianVulkanBuffer[] resources = [scene.Procedurals, rays, hits, scene.Instances, parameters[frames.Slot, phase],
                motion, output, normals, sequence % 2 == 0 ? historyA : historyB, sequence % 2 == 0 ? historyB : historyA, baseline, scene.Shading, occlusion];
            writes[0] = new() { SType = StructureType.WriteDescriptorSet, PNext = &structure,
                DstSet = sets[frames.Slot, phase], DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.AccelerationStructureKhr };
            for (int index = 0; index < resources.Length; index++)
            {
                info[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
                writes[index + 1] = new() { SType = StructureType.WriteDescriptorSet, DstSet = sets[frames.Slot, phase],
                    DstBinding = (uint)index + 1, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &info[index] };
            }
            plant.Vk.UpdateDescriptorSets(plant.Device, 14, writes, 0, null);
        }
    }

    public void Record(VulkanCommandBufferLease command)
    {
        uint capacity = Math.Max(width * height, 512 * 128);
        Barrier(command, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit | AccessFlags.HostWriteBit,
            AccessFlags.TransferWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        plant.Vk.CmdUpdateBuffer(command.CommandBuffer, rays.NativeBuffer, 0, 4, &capacity);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        uint[] counts = [checked((uint)(options.Grid!.Count * options.Grid.RaysPerProbe)), checked((uint)options.Grid.Count * 64), width * height];
        for (int phase = 0; phase < 3; phase++)
        {
            DescriptorSet set = sets[frames.Slot, phase];
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, (counts[phase] + 63) / 64, 1, 1);
            Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferReadBit);
        }
        Transition(command, VulkanResourceLayout.TransferDestination);
        BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new(width, height, 1) };
        plant.Vk.CmdCopyBufferToImage(command.CommandBuffer, output.NativeBuffer, Output.NativeImage, ImageLayout.TransferDstOptimal, 1, &copy);
        Transition(command, VulkanResourceLayout.ShaderResourceFragment);
        sequence++;
        stationaryFrames++;
    }

    public CompiledDiffuseProbeField3D CaptureCompiled()
    {
        if (options.Mode != DynamicDiffuseMode3D.Runtime || stationaryFrames * options.RaysPerProbePerFrame < options.Grid!.RaysPerProbe)
            throw new InvalidOperationException("AUR-GI-004: Compile capture requires runtime mode and complete directional coverage of a stationary scene.");
        var field = new CompiledDiffuseProbeField3D(1, options.Grid, ReadCache());
        field.Validate();
        return field;
    }

    public float[] ReadCache() => Vulkan3DQualificationReadback.ReadBuffer(plant, frames, sequence % 2 == 0 ? historyA : historyB);
    public float[] ReadOutput() => Vulkan3DQualificationReadback.ReadBuffer(plant, frames, output);

    private void Transition(VulkanCommandBufferLease command, VulkanResourceLayout layout)
    {
        var result = Output.LayoutTracker.Transition("dynamic-diffuse", 0, 0, layout);
        if (!result.Success || result.Plan is not null && !VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(Output, result.Plan)]).Success)
            throw new InvalidOperationException("Dynamic diffuse transition failed.");
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags before, AccessFlags after)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.AllCommandsBit | PipelineStageFlags.HostBit,
            PipelineStageFlags.AllCommandsBit, 0, 1, &barrier, 0, null, 0, null);
    }

    private AurelianVulkanBuffer Cache(ulong bytes, VulkanMemoryUsage memory)
    {
        var buffer = Buffer(bytes, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource, memory);
        buffers.Remove(buffer);
        cacheBuffers.Add(buffer);
        return buffer;
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory = VulkanMemoryUsage.GpuOnly)
    {
        var result = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "dynamic-diffuse-buffer", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        if (!result.Success) throw new InvalidOperationException("Dynamic diffuse allocation failed.");
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private static void Write(AurelianVulkanBuffer destination, float[] words)
    {
        if (!destination.Write(MemoryMarshal.AsBytes(words.AsSpan())).Success)
            throw new InvalidOperationException("Dynamic diffuse upload failed.");
    }

    private static int DirectionStride(int count)
    {
        int stride = Math.Max(1, (int)(count * .61803398875));
        while (GreatestCommonDivisor(stride, count) != 1) stride--;
        return stride;

        static int GreatestCommonDivisor(int first, int second)
        {
            while (second != 0)
            {
                int remainder = first % second;
                first = second;
                second = remainder;
            }
            return first;
        }
    }

    private void CreatePipeline(byte[] spirv)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[14];
        for (uint index = 0; index < 14; index++) bindings[index] = new(index,
            index == 0 ? DescriptorType.AccelerationStructureKhr : DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        var layout = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 14, PBindings = bindings };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new(DescriptorType.AccelerationStructureKhr, VulkanFrameInFlightSet.Count * 3);
        sizes[1] = new(DescriptorType.StorageBuffer, 13 * VulkanFrameInFlightSet.Count * 3);
        var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = VulkanFrameInFlightSet.Count * 3, PoolSizeCount = 2, PPoolSizes = sizes };
        fixed (DescriptorPool* value = &descriptorPool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        DescriptorSetLayout descriptorLayout = setLayout;
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
        {
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool, DescriptorSetCount = 1, PSetLayouts = &descriptorLayout };
            for (int phase = 0; phase < 3; phase++) Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out sets[slot, phase]));
        }
        var pipelineInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &descriptorLayout };
        fixed (PipelineLayout* value = &pipelineLayout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineInfo, null, value));
        ShaderModule module;
        fixed (byte* code = spirv)
        {
            var shader = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
            Check(plant.Vk.CreateShaderModule(plant.Device, &shader, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr("DynamicDiffuseMain");
        try
        {
            var create = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)entry }, Layout = pipelineLayout };
            fixed (Pipeline* value = &pipeline) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("Dynamic diffuse Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (pipelineLayout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        if (descriptorPool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        Output?.Dispose();
        foreach (var buffer in cacheBuffers) buffer.Dispose();
        foreach (var buffer in buffers) buffer.Dispose();
    }
}
