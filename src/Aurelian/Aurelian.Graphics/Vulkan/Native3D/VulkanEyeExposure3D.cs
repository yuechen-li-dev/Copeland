using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record EyeExposurePrograms(byte[] Histogram, byte[] Adapt, CompiledGraphicsProgram Output, VulkanEyeExposureState3D State);
public readonly record struct EyeExposureMeasurement(float AppliedEv, float TargetEv, float MeteredLogLuminance, float MeterWeight)
{
    public float AdaptedLogLuminance { get; init; }
}

/// <summary>One GPU-resident adaptation state per view, shared by all its render scales.</summary>
public sealed class VulkanEyeExposureState3D : IDisposable
{
    private readonly RawVulkanMemoryAllocator allocator;
    internal AurelianVulkanPlant Plant { get; }
    internal AurelianVulkanBuffer Buffer { get; }
    internal bool Valid { get; private set; }

    public VulkanEyeExposureState3D(AurelianVulkanPlant plant)
    {
        Plant = plant;
        allocator = new(plant);
        var allocation = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, 32,
            VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource, VulkanMemoryUsage.GpuOnly, "eye.exposure"));
        if (!allocation.Success)
        {
            allocator.Dispose();
            throw new InvalidOperationException("Eye exposure state allocation failed.");
        }
        Buffer = allocation.Buffer!;
    }

    internal void RecordStep() => Valid = true;

    public void Reset() => Valid = false;

    public void Dispose()
    {
        Buffer.Dispose();
        allocator.Dispose();
    }
}

internal sealed unsafe class VulkanEyeExposure3D : IDisposable
{
    private const uint Blocks = 144;
    private const uint Bins = 64;
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanFrameInFlightSet frames;
    private readonly VulkanEyeExposureState3D state;
    private readonly AurelianVulkanBuffer histogram;
    private readonly AurelianVulkanBuffer[] parameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[] sets = new DescriptorSet[VulkanFrameInFlightSet.Count];
    private DescriptorSetLayout setLayout;
    private PipelineLayout layout;
    private DescriptorPool pool;
    private Pipeline histogramPipeline;
    private Pipeline adaptPipeline;

    public VulkanEyeExposure3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanFrameInFlightSet frames, EyeExposurePrograms programs)
    {
        this.plant = plant;
        this.frames = frames;
        state = programs.State;
        if (!ReferenceEquals(state.Plant, plant))
        {
            throw new ArgumentException("Eye exposure state belongs to a different Vulkan plant.", nameof(programs));
        }
        var allocation = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, Blocks * Bins * 4,
            VulkanBufferUsage.Storage, VulkanMemoryUsage.GpuOnly, "eye.histogram"));
        Require(allocation.Success, "Histogram allocation failed.");
        histogram = allocation.Buffer!;
        try
        {
            CreateBindings(allocator);
            histogramPipeline = CreatePipeline(programs.Histogram, "EyeHistogramMain");
            adaptPipeline = CreatePipeline(programs.Adapt, "EyeAdaptMain");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Record(VulkanCommandBufferLease command, AurelianVulkanTexture scene, EyeExposure3D settings,
        float elapsedSeconds)
    {
        var transition = scene.LayoutTracker.Transition("eye.meter", 0, 0, VulkanResourceLayout.ShaderResourceCompute);
        Require(transition.Success && (transition.Plan is null
            || VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(scene, transition.Plan)]).Success),
            "Eye meter scene transition failed.");
        int slot = frames.Slot;
        Span<float> data =
        [
            scene.Width, scene.Height, elapsedSeconds, state.Valid ? 0 : 1,
            settings.MeterCenter.X, settings.MeterCenter.Y, settings.MeterRadius, settings.PeripheralWeight,
            MathF.Log2(.18f) + settings.CompensationEv, settings.MinimumEv, settings.MaximumEv, 0,
            settings.LowPercentile, settings.HighPercentile, settings.BrightTimeConstant, settings.DarkTimeConstant,
        ];
        Require(parameters[slot].Write(MemoryMarshal.AsBytes(data)).Success, "Eye meter parameter upload failed.");
        DescriptorSet set = sets[slot];
        Vulkan3DPass.WriteImage(plant, set, 0, scene, null);
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, histogramPipeline);
        plant.Vk.CmdDispatch(command.CommandBuffer, Blocks, 1, 1);

        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit, DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit,
            0, 1, &barrier, 0, null, 0, null);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, adaptPipeline);
        plant.Vk.CmdDispatch(command.CommandBuffer, 1, 1, 1);
    }

    private void CreateBindings(RawVulkanMemoryAllocator allocator)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4]
        {
            new(0, DescriptorType.SampledImage, 1, ShaderStageFlags.ComputeBit),
            new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(2, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(3, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
        };
        DescriptorSetLayoutCreateInfo setInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &setInfo, null, out setLayout));
        DescriptorSetLayout nativeLayout = setLayout;
        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1, PSetLayouts = &nativeLayout };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, out layout));
        uint count = (uint)sets.Length;
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2]
        {
            new(DescriptorType.SampledImage, count), new(DescriptorType.StorageBuffer, count * 3),
        };
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = count, PoolSizeCount = 2, PPoolSizes = sizes };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
        for (int slot = 0; slot < sets.Length; slot++)
        {
            DescriptorSetAllocateInfo allocate = new() { SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &nativeLayout };
            Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out sets[slot]));
            parameters[slot] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 64,
                VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu, "eye.parameters");
            Vulkan3DPass.WriteStorageBuffer(plant, sets[slot], 1, histogram);
            Vulkan3DPass.WriteStorageBuffer(plant, sets[slot], 2, parameters[slot]);
            Vulkan3DPass.WriteStorageBuffer(plant, sets[slot], 3, state.Buffer);
        }
    }

    private Pipeline CreatePipeline(byte[] shader, string entryPoint)
    {
        ShaderModule module;
        fixed (byte* bytes = shader)
        {
            ShaderModuleCreateInfo info = new() { SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)shader.Length, PCode = (uint*)bytes };
            Check(plant.Vk.CreateShaderModule(plant.Device, &info, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr(entryPoint);
        try
        {
            ComputePipelineCreateInfo info = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit,
                    Module = module, PName = (byte*)entry } };
            Pipeline pipeline;
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &info, null, &pipeline));
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private static void Check(Result result) => Require(result == Result.Success, "Eye exposure Vulkan result " + result);
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (adaptPipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, adaptPipeline, null);
        if (histogramPipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, histogramPipeline, null);
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (layout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, layout, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        foreach (var parameter in parameters) parameter?.Dispose();
        histogram.Dispose();
    }
}
