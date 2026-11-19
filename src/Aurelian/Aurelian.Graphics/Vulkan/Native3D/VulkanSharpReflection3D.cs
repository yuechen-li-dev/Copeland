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

/// <summary>Full-resolution one-bounce reflection radiance from the frame's retained TLAS.</summary>
internal sealed unsafe class VulkanSharpReflection3D : IDisposable
{
    private const uint BufferCount = 13;
    private const uint ImageCount = 7;
    private const uint SamplerBinding = 21;
    private const uint MaterialImagesBinding = 22;
    private const uint MaterialSamplersBinding = 23;
    private const uint HitMetadataBinding = 24;
    private const uint ProbeGuideBinding = 25;
    private const uint DescriptorCount = 26;
    private const uint AuxiliaryBufferCount = 2;
    private const uint ThreadsPerGroup = 64;
    private const uint MaximumScratchRays = 256 * 1024;

    private readonly AurelianVulkanPlant plant;
    private readonly VulkanFrameInFlightSet frames;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanBuffer[] parameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[] sets = new DescriptorSet[VulkanFrameInFlightSet.Count];
    private readonly VulkanBindlessMaterials3D?[] sampledTables = new VulkanBindlessMaterials3D[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer rays;
    private readonly AurelianVulkanBuffer hits;
    private readonly AurelianVulkanBuffer occlusion;
    private readonly AurelianVulkanBuffer radiance;
    private AurelianVulkanBuffer? hitMetadata;
    private readonly uint pixelCount;
    private readonly uint scratchRays;
    private readonly uint stripeCount;
    private DescriptorSetLayout setLayout;
    private DescriptorPool pool;
    private PipelineLayout layout;
    private Pipeline pipeline;
    private Sampler sampler;
    private uint sequence;

    public AurelianVulkanTexture Output { get; }
    public AurelianVulkanTexture? HitMetadata { get; private set; }
    public int RecordedPhase { get; private set; }

    public VulkanSharpReflection3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanFrameInFlightSet frames, byte[] program, uint width, uint height)
    {
        this.plant = plant;
        this.frames = frames;
        this.allocator = allocator;
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        pixelCount = checked(width * height);
        scratchRays = Math.Min(pixelCount, MaximumScratchRays);
        stripeCount = (pixelCount + scratchRays - 1) / scratchRays;
        if (pixelCount * 16UL > properties.Limits.MaxStorageBufferRange
            || Groups(scratchRays) > properties.Limits.MaxComputeWorkGroupCount[0]
            || stripeCount > properties.Limits.MaxComputeWorkGroupCount[1])
            throw new NotSupportedException($"AUR-REFLECT-003: The {width}x{height} reflection target exceeds storage or compute dispatch limits.");
        const uint images = ImageCount + VulkanBindlessMaterials3D.TextureCapacity;
        const uint samplers = 1 + VulkanBindlessMaterials3D.SamplerCapacity;
        if (properties.Limits.MaxPerStageDescriptorSampledImages < images || properties.Limits.MaxDescriptorSetSampledImages < images
            || properties.Limits.MaxPerStageDescriptorSamplers < samplers || properties.Limits.MaxDescriptorSetSamplers < samplers
            || properties.Limits.MaxPerStageDescriptorStorageBuffers < BufferCount + AuxiliaryBufferCount
            || properties.Limits.MaxDescriptorSetStorageBuffers < BufferCount + AuxiliaryBufferCount
            || properties.Limits.MaxPerStageResources < images + samplers + BufferCount + AuxiliaryBufferCount + 1)
            throw new NotSupportedException("AUR-REFLECT-003: The reflection material table exceeds this device's compute descriptor limits.");

        try
        {
            rays = Own(CreateBuffer("rays", (4 + scratchRays * 12UL) * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination));
            hits = Own(CreateBuffer("hits", scratchRays * 64UL, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource));
            occlusion = Own(CreateBuffer("occlusion", scratchRays * 4UL, VulkanBufferUsage.Storage));
            radiance = Own(CreateBuffer("radiance", pixelCount * 16UL, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource));
            for (int slot = 0; slot < parameters.Length; slot++)
                parameters[slot] = Own(CreateBuffer("parameters", (64 + 256 * 16) * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu));
            Output = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
                "sharp-reflection.radiance", VulkanTextureFormat.Rgba32Float));
            sampler = Vulkan3DPass.CreateSampler(plant, Filter.Linear);
            CreatePipeline(program);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void PrepareMetadata()
    {
        if (HitMetadata is not null) return;
        var buffer = CreateBuffer("hit-metadata", pixelCount * 8UL, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
        try
        {
            HitMetadata = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, Output.Width, Output.Height,
                VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
                "glossy-reflection.hits", VulkanTextureFormat.Rg32UInt));
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        hitMetadata = Own(buffer);
    }

    private AurelianVulkanBuffer CreateBuffer(string name, ulong bytes, VulkanBufferUsage usage,
        VulkanMemoryUsage memory = VulkanMemoryUsage.GpuOnly)
    {
        var allocation = VulkanBufferFactory.Create(plant, allocator,
            new(plant.Context.Id, bytes, usage, memory, "sharp-reflection." + name, MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        if (!allocation.Success)
            throw new InvalidOperationException("AUR-REFLECT-004: Reflection " + name + " allocation failed.");
        return allocation.Buffer!;
    }

    public void Record(VulkanCommandBufferLease command, VulkanSceneAcceleration3D scene, VulkanBindlessMaterials3D materials,
        AurelianVulkanTexture[] inputs, Matrix4x4 clip, Matrix4x4 inverse, Vector3 eye, Graphics3DSettings settings,
        ReadOnlySpan<float> packedLights, int lightCount, float emissionIntensity, bool hasEnvironment, bool hasSkySource, bool hasProbe,
        AurelianVulkanBuffer? reflectionGuide)
    {
        var words = new float[64 + lightCount * 16];
        Lighting3DUniforms.Rows(inverse).CopyTo(words, 0);
        words[16] = eye.X;
        words[17] = eye.Y;
        words[18] = eye.Z;
        words[19] = clip.M14 == 0 && clip.M24 == 0 && clip.M34 == 0 ? 0 : 1;
        words[20] = Output.Width;
        words[21] = Output.Height;
        words[22] = (float)settings.DirectDiffuseModel;
        words[23] = emissionIntensity;
        words[24] = hasEnvironment ? 1 : 0;
        words[25] = settings.EnvironmentIntensity;
        words[26] = lightCount;
        words[27] = scratchRays;
        words[28] = settings.GlossyReflections != GlossyReflectionMode3D.Off ? 1 : 0;
        if (reflectionGuide is not null) words[28] = 2;
        words[29] = hasSkySource ? 1 : 0;
        words[30] = hasProbe ? 1 : 0;
        bool reconstruct = settings.GlossyReflections is GlossyReflectionMode3D.FullResolution or GlossyReflectionMode3D.HalfResolution;
        words[31] = reconstruct ? 1 : 0;
        Lighting3DUniforms.Rows(clip).CopyTo(words, 32);
        WriteVector(48, Vector3.Normalize(settings.SunDirection), settings.Shadows ? 1 : 0);
        WriteVector(52, settings.SunColor, settings.SunIntensity);
        bool quarterRate = settings.GlossyReflections == GlossyReflectionMode3D.HalfResolution;
        int traceFlags = quarterRate ? 1 : 0;
        if (settings.ScreenSpaceReflectionBounds) traceFlags |= 2;
        WriteVector(56, settings.SkyAmbient, traceFlags);
        WriteVector(60, settings.GroundAmbient, 0);
        words[63] = sequence % 4096;
        RecordedPhase = (int)(sequence % 4);
        packedLights[..(lightCount * 16)].CopyTo(words.AsSpan(64));
        if (!parameters[frames.Slot].Write(MemoryMarshal.AsBytes(words.AsSpan())).Success)
            throw new InvalidOperationException("AUR-REFLECT-004: Reflection parameters could not be uploaded.");

        DescriptorSet set = UpdateDescriptors(scene, materials, inputs, reflectionGuide);
        foreach (var input in inputs) Transition(command, input, VulkanResourceLayout.ShaderResourceAll);
        Barrier(command, PipelineStageFlags.AllCommandsBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferReadBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit,
            AccessFlags.TransferWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        uint count = scratchRays;
        plant.Vk.CmdUpdateBuffer(command.CommandBuffer, rays.NativeBuffer, 0, 4, &count);
        Barrier(command, PipelineStageFlags.TransferBit | PipelineStageFlags.HostBit,
            AccessFlags.TransferWriteBit | AccessFlags.HostWriteBit, PipelineStageFlags.ComputeShaderBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
        DispatchStripes(pixelCount, 0);
        if (quarterRate)
        {
            // Full-rate sharp pixels and receiver flags come from the first
            // dispatch. Pack the glossy lanes together for the hardware query.
            ReuseScratch();
            DispatchStripes((Output.Width + 1) / 2 * ((Output.Height + 1) / 2), 1);
        }
        Barrier(command, PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit);
        Publish(radiance, Output);
        if (reconstruct)
            Publish(hitMetadata!, HitMetadata!);
        if (settings.GlossyReflections != GlossyReflectionMode3D.Off) sequence++;

        void ReuseScratch()
        {
            Barrier(command, PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        }

        void DispatchStripes(uint pixels, uint pass)
        {
            uint stripes = (pixels + scratchRays - 1) / scratchRays;
            for (uint stripe = 0; stripe < stripes; stripe++)
            {
                if (stripe != 0) ReuseScratch();
                uint remaining = pixels - stripe * scratchRays;
                plant.Vk.CmdDispatchBase(command.CommandBuffer, 0, stripe, pass, Groups(Math.Min(scratchRays, remaining)), 1, 1);
            }
        }

        void Publish(AurelianVulkanBuffer source, AurelianVulkanTexture destination)
        {
            Transition(command, destination, VulkanResourceLayout.TransferDestination);
            BufferImageCopy copy = new()
            {
                ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new(destination.Width, destination.Height, 1),
            };
            plant.Vk.CmdCopyBufferToImage(command.CommandBuffer, source.NativeBuffer, destination.NativeImage,
                ImageLayout.TransferDstOptimal, 1, &copy);
            Transition(command, destination, VulkanResourceLayout.ShaderResourceFragment);
        }
        void WriteVector(int offset, Vector3 value, float w)
        {
            words[offset] = value.X;
            words[offset + 1] = value.Y;
            words[offset + 2] = value.Z;
            words[offset + 3] = w;
        }
    }

    public (float[] Radiance, float[] Hits) ReadForQualification()
    {
        if (stripeCount != 1)
            throw new NotSupportedException("Read full hit packets from a qualification target of at most 262144 pixels.");
        return (Vulkan3DQualificationReadback.ReadBuffer(plant, frames, radiance),
            Vulkan3DQualificationReadback.ReadBuffer(plant, frames, hits));
    }

    public (float[] Radiance, uint[] HitPairs) ReadGlossyForQualification() =>
        (Vulkan3DQualificationReadback.ReadBuffer(plant, frames, radiance),
            Vulkan3DQualificationReadback.ReadBuffer<uint>(plant, frames, hitMetadata!));

    private static uint Groups(uint count) => (count + ThreadsPerGroup - 1) / ThreadsPerGroup;

    private DescriptorSet UpdateDescriptors(VulkanSceneAcceleration3D scene, VulkanBindlessMaterials3D materials,
        AurelianVulkanTexture[] inputs, AurelianVulkanBuffer? reflectionGuide)
    {
        int slot = frames.Slot;
        DescriptorSet set = sets[slot];
        AccelerationStructureKHR top = scene.TopLevel;
        WriteDescriptorSetAccelerationStructureKHR acceleration = new()
        {
            SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
            AccelerationStructureCount = 1,
            PAccelerationStructures = &top,
        };
        AurelianVulkanBuffer[] buffers = [scene.Procedurals, scene.Instances, scene.Shading, parameters[slot], rays, hits, occlusion, radiance,
            materials.MaterialRecords, materials.InstanceSurfaces, materials.ResidentVertices, materials.ResidentIndices, materials.ResidentInstances];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[(int)DescriptorCount];
        DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[(int)(BufferCount + AuxiliaryBufferCount)];
        DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[(int)ImageCount + 1];
        writes[0] = new() { SType = StructureType.WriteDescriptorSet, PNext = &acceleration, DstSet = set,
            DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.AccelerationStructureKhr };
        for (uint index = 0; index < BufferCount; index++)
        {
            bufferInfos[index] = new(buffers[index].NativeBuffer, 0, buffers[index].SizeBytes);
            writes[1 + index] = new() { SType = StructureType.WriteDescriptorSet, DstSet = set,
                DstBinding = 1 + index, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &bufferInfos[index] };
        }
        for (uint index = 0; index < ImageCount; index++)
        {
            imageInfos[index] = new(default, inputs[index].NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
            uint binding = 1 + BufferCount + index;
            writes[binding] = ImageWrite(set, binding, DescriptorType.SampledImage, 1, &imageInfos[index]);
        }
        imageInfos[ImageCount] = new(sampler, default, ImageLayout.Undefined);
        writes[SamplerBinding] = ImageWrite(set, SamplerBinding, DescriptorType.Sampler, 1, &imageInfos[ImageCount]);
        WriteAuxiliary(HitMetadataBinding, BufferCount, hitMetadata ?? occlusion);
        WriteAuxiliary(ProbeGuideBinding, BufferCount + 1, reflectionGuide ?? occlusion);
        if (ReferenceEquals(sampledTables[slot], materials))
        {
            writes[MaterialImagesBinding] = writes[HitMetadataBinding];
            writes[MaterialSamplersBinding] = writes[ProbeGuideBinding];
            plant.Vk.UpdateDescriptorSets(plant.Device, MaterialImagesBinding + AuxiliaryBufferCount, writes, 0, null);
        }
        else
        {
            fixed (DescriptorImageInfo* textures = materials.TextureDescriptors)
            fixed (DescriptorImageInfo* samplers = materials.SamplerDescriptors)
            {
                writes[MaterialImagesBinding] = ImageWrite(set, MaterialImagesBinding, DescriptorType.SampledImage, VulkanBindlessMaterials3D.TextureCapacity, textures);
                writes[MaterialSamplersBinding] = ImageWrite(set, MaterialSamplersBinding, DescriptorType.Sampler, VulkanBindlessMaterials3D.SamplerCapacity, samplers);
                plant.Vk.UpdateDescriptorSets(plant.Device, DescriptorCount, writes, 0, null);
            }
            sampledTables[slot] = materials;
        }
        return set;

        void WriteAuxiliary(uint binding, uint index, AurelianVulkanBuffer buffer)
        {
            bufferInfos[index] = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
            writes[binding] = new() { SType = StructureType.WriteDescriptorSet, DstSet = set,
                DstBinding = binding, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &bufferInfos[index] };
        }
    }

    private static WriteDescriptorSet ImageWrite(DescriptorSet set, uint binding, DescriptorType type, uint count, DescriptorImageInfo* images) =>
        new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorCount = count, DescriptorType = type, PImageInfo = images };

    private void CreatePipeline(byte[] spirv)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[(int)DescriptorCount];
        bindings[0] = new(0, DescriptorType.AccelerationStructureKhr, 1, ShaderStageFlags.ComputeBit);
        for (uint index = 1; index <= BufferCount; index++)
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        for (uint index = 1 + BufferCount; index < SamplerBinding; index++)
            bindings[index] = new(index, DescriptorType.SampledImage, 1, ShaderStageFlags.ComputeBit);
        bindings[SamplerBinding] = new(SamplerBinding, DescriptorType.Sampler, 1, ShaderStageFlags.ComputeBit);
        bindings[MaterialImagesBinding] = new(MaterialImagesBinding, DescriptorType.SampledImage, VulkanBindlessMaterials3D.TextureCapacity, ShaderStageFlags.ComputeBit);
        bindings[MaterialSamplersBinding] = new(MaterialSamplersBinding, DescriptorType.Sampler, VulkanBindlessMaterials3D.SamplerCapacity, ShaderStageFlags.ComputeBit);
        bindings[HitMetadataBinding] = new(HitMetadataBinding, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        bindings[ProbeGuideBinding] = new(ProbeGuideBinding, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        DescriptorSetLayoutCreateInfo descriptorLayout = new() { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = DescriptorCount, PBindings = bindings };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &descriptorLayout, null, value));
        const uint slots = VulkanFrameInFlightSet.Count;
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[4]
        {
            new(DescriptorType.AccelerationStructureKhr, slots), new(DescriptorType.StorageBuffer, (BufferCount + AuxiliaryBufferCount) * slots),
            new(DescriptorType.SampledImage, (ImageCount + VulkanBindlessMaterials3D.TextureCapacity) * slots),
            new(DescriptorType.Sampler, (1 + VulkanBindlessMaterials3D.SamplerCapacity) * slots),
        };
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = slots, PoolSizeCount = 4, PPoolSizes = sizes };
        fixed (DescriptorPool* value = &pool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, value));
        DescriptorSetLayout descriptorSetLayout = setLayout;
        DescriptorSetAllocateInfo allocate = new() { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = pool,
            DescriptorSetCount = 1, PSetLayouts = &descriptorSetLayout };
        for (int slot = 0; slot < sets.Length; slot++)
            Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out sets[slot]));

        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &descriptorSetLayout };
        fixed (PipelineLayout* value = &layout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, value));
        ShaderModule module;
        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo shader = new() { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
            Check(plant.Vk.CreateShaderModule(plant.Device, &shader, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr("SharpReflectionMain");
        try
        {
            ComputePipelineCreateInfo create = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                Flags = PipelineCreateFlags.CreateDispatchBaseBit,
                Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)entry } };
            fixed (Pipeline* value = &pipeline) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private void Transition(VulkanCommandBufferLease command, AurelianVulkanTexture texture, VulkanResourceLayout layout)
    {
        var transition = texture.LayoutTracker.Transition("sharp-reflection", 0, 0, layout);
        if (!transition.Success || transition.Plan is not null
            && !VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, transition.Plan)]).Success)
            throw new InvalidOperationException("AUR-REFLECT-004: Reflection texture transition failed.");
    }

    private void Barrier(VulkanCommandBufferLease command, PipelineStageFlags source, AccessFlags before,
        PipelineStageFlags destination, AccessFlags after)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, source, destination, 0, 1, &barrier, 0, null, 0, null);
    }

    private T Own<T>(T value) where T : IDisposable
    {
        owned.Push(value);
        return value;
    }

    private static void Check(Result result)
    {
        if (result != Result.Success)
            throw new InvalidOperationException("AUR-REFLECT-004: Reflection Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (layout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, layout, null);
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        if (sampler.Handle != 0) plant.Vk.DestroySampler(plant.Device, sampler, null);
        while (owned.TryPop(out var resource)) resource.Dispose();
    }
}
