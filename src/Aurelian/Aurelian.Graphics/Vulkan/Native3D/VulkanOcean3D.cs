using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Queue-resident inverse FFT and crest map for the refraction pass.
/// Both presentation times are evaluated explicitly, including after a seek.</summary>
internal sealed unsafe class VulkanOcean3D : IDisposable
{
    private const uint Size = OceanSpectrum.Resolution;
    private const ulong AtlasBytes = Size * Size * 2 * 16;
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> buffers = new();
    private readonly AurelianVulkanBuffer spectrum;
    private readonly AurelianVulkanBuffer rows;
    private readonly AurelianVulkanBuffer columns;
    private readonly AurelianVulkanBuffer rowParameters;
    private readonly AurelianVulkanBuffer columnParameters;
    private DescriptorSetLayout descriptorLayout;
    private DescriptorPool descriptorPool;
    private PipelineLayout pipelineLayout;
    private Pipeline fft;
    private Pipeline foam;
    private readonly DescriptorSet rowSet;
    private readonly DescriptorSet columnSet;
    private readonly DescriptorSet foamSet;
    private OceanSpectrum3D? generatedSpectrum;
    private OceanSpectrum3D? uploadedSpectrum;
    private Vector2[] coefficients = [];

    public AurelianVulkanTexture Atlas { get; }

    public VulkanOcean3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator, byte[] fftProgram, byte[] foamProgram)
    {
        this.plant = plant;
        try
        {
            spectrum = Buffer(Size * Size * 8, VulkanBufferUsage.TransferDestination);
            rows = Buffer(AtlasBytes, VulkanBufferUsage.TransferSource);
            columns = Buffer(AtlasBytes, VulkanBufferUsage.TransferSource);
            rowParameters = Buffer(32, VulkanBufferUsage.TransferDestination);
            columnParameters = Buffer(32, VulkanBufferUsage.TransferDestination);
            Atlas = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, Size, Size * 2,
                VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly, "ocean.displacement-foam", VulkanTextureFormat.Rgba32Float);
            buffers.Push(Atlas);
            CreateLayout();
            rowSet = Set(spectrum, columns, rows, rowParameters);
            columnSet = Set(spectrum, rows, columns, columnParameters);
            foamSet = Set(columns, rows, columnParameters);
            fft = CreatePipeline(fftProgram, "OceanFftMain");
            foam = CreatePipeline(foamProgram, "OceanFoamMain");
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage)
        {
            var result = VulkanBufferFactory.Create(plant, allocator,
                new(plant.Context.Id, bytes, VulkanBufferUsage.Storage | usage, VulkanMemoryUsage.GpuOnly, "ocean.fft"));
            if (!result.Success)
                throw new InvalidOperationException("Ocean buffer allocation failed: " + string.Join("; ", result.Diagnostics));
            buffers.Push(result.Buffer!);
            return result.Buffer!;
        }
    }

    public void Record(VulkanCommandBufferLease command, OceanSpectrum3D profile, float time, float previousTime)
    {
        if (generatedSpectrum != profile)
        {
            coefficients = OceanSpectrum.Create(profile);
            generatedSpectrum = profile;
        }
        var cmd = command.CommandBuffer;
        const PipelineStageFlags compute = PipelineStageFlags.ComputeShaderBit;
        const PipelineStageFlags transfer = PipelineStageFlags.TransferBit;
        const AccessFlags shaderAccess = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;

        // Buffers and the image are single-copy across the graphics queue.
        // Order reuse after predecessor dispatches, copies, and texture reads.
        Barrier(cmd, compute | transfer, compute | transfer,
            shaderAccess | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
            shaderAccess | AccessFlags.TransferWriteBit);
        if (uploadedSpectrum != profile)
        {
            fixed (Vector2* data = coefficients)
            {
                plant.Vk.CmdUpdateBuffer(cmd, spectrum.NativeBuffer, 0, spectrum.SizeBytes, data);
            }
        }
        Vector4* parameters = stackalloc Vector4[2];
        parameters[0] = new(time, previousTime, MathF.Tau / profile.TileLength, 0);
        parameters[1] = new(profile.Choppiness, Size / (2 * profile.TileLength), 0, 0);
        plant.Vk.CmdUpdateBuffer(cmd, rowParameters.NativeBuffer, 0, rowParameters.SizeBytes, parameters);
        parameters[0].W = 1;
        plant.Vk.CmdUpdateBuffer(cmd, columnParameters.NativeBuffer, 0, columnParameters.SizeBytes, parameters);
        Barrier(cmd, transfer, compute, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        plant.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, fft);
        Dispatch(cmd, rowSet);
        Barrier(cmd, compute, compute, shaderAccess, shaderAccess);
        Dispatch(cmd, columnSet);
        Barrier(cmd, compute, compute, shaderAccess, shaderAccess);
        plant.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, foam);
        Dispatch(cmd, foamSet);
        Barrier(cmd, compute, transfer, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit);
        Transition(command, VulkanResourceLayout.TransferDestination);
        BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(Size, Size * 2, 1) };
        plant.Vk.CmdCopyBufferToImage(cmd, rows.NativeBuffer, Atlas.NativeImage, ImageLayout.TransferDstOptimal, 1, &copy);
        Transition(command, VulkanResourceLayout.ShaderResourceAll);
        uploadedSpectrum = profile;
    }

    public (Vector2[] Coefficients, Vector4[] Surface, Vector4[] Displacement) Read(VulkanFrameInFlightSet frames) =>
        ((Vector2[])coefficients.Clone(), Vulkan3DQualificationReadback.ReadBuffer<Vector4>(plant, frames, rows),
            Vulkan3DQualificationReadback.ReadBuffer<Vector4>(plant, frames, columns));

    private void Dispatch(CommandBuffer command, DescriptorSet set)
    {
        plant.Vk.CmdBindDescriptorSets(command, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command, Size, 2, 1);
    }

    private void Transition(VulkanCommandBufferLease command, VulkanResourceLayout layout)
    {
        var transition = Atlas.LayoutTracker.Transition("ocean.displacement-foam", 0, 0, layout);
        if (!transition.Success || transition.Plan is not null &&
            !VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(Atlas, transition.Plan)]).Success)
            throw new InvalidOperationException("Ocean atlas transition failed.");
    }

    private void Barrier(CommandBuffer command, PipelineStageFlags sourceStage, PipelineStageFlags destinationStage,
        AccessFlags source, AccessFlags destination)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private void CreateLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint binding = 0; binding < 4; binding++)
            bindings[binding] = new(binding, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        DescriptorSetLayoutCreateInfo descriptors = new() { SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &descriptors, null, out descriptorLayout));
        DescriptorPoolSize size = new(DescriptorType.StorageBuffer, 12);
        DescriptorPoolCreateInfo pool = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 3,
            PoolSizeCount = 1, PPoolSizes = &size };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, out descriptorPool));
        var nativeLayout = descriptorLayout;
        PipelineLayoutCreateInfo layout = new() { SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1, PSetLayouts = &nativeLayout };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layout, null, out pipelineLayout));
    }

    private DescriptorSet Set(params AurelianVulkanBuffer[] storage)
    {
        var layout = descriptorLayout;
        DescriptorSetAllocateInfo allocate = new() { SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool, DescriptorSetCount = 1, PSetLayouts = &layout };
        Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out DescriptorSet set));
        for (uint binding = 0; binding < storage.Length; binding++)
            Vulkan3DPass.WriteStorageBuffer(plant, set, binding, storage[binding]);
        return set;
    }

    private Pipeline CreatePipeline(byte[] spirv, string entryPoint)
    {
        ShaderModule module;
        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo shader = new() { SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
            Check(plant.Vk.CreateShaderModule(plant.Device, &shader, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr(entryPoint);
        try
        {
            ComputePipelineCreateInfo pipeline = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = pipelineLayout,
                Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)entry } };
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &pipeline, null, out Pipeline result));
            return result;
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private static void Check(Result result)
    {
        if (result != Result.Success)
            throw new InvalidOperationException("Ocean Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        if (foam.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, foam, null);
        if (fft.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, fft, null);
        if (pipelineLayout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        if (descriptorPool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        if (descriptorLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, descriptorLayout, null);
        while (buffers.TryPop(out var buffer)) buffer.Dispose();
    }
}
