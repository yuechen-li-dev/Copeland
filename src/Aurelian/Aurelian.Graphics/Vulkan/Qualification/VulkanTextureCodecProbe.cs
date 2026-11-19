using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Qualification;

public sealed record CodecTexture(uint Width, uint Height, VulkanTextureFormat Format, byte[] Bytes);

public sealed record TextureCodecCapture(double MedianGpuMilliseconds, double P95GpuMilliseconds,
    uint[]? Samples, int SampleCount);

/// <summary>Matched device-local Vulkan texture sampling/decoding with a common packed output.
/// Timestamps enclose the shader dispatch only. Input uploads and qualification readback are outside.</summary>
public sealed unsafe class VulkanTextureCodecProbe : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandSubmitter submitter;
    private readonly Vulkan3DGpuTimings timings;
    private readonly List<IDisposable> owned = [];
    private readonly AurelianVulkanBuffer demand;
    private readonly AurelianVulkanBuffer output;
    private readonly int pixelsPerGroup;
    private readonly int capacity;
    private readonly bool split;
    private DescriptorSetLayout setLayout;
    private DescriptorPool pool;
    private DescriptorSet set;
    private PipelineLayout layout;
    private Pipeline pipeline;
    private Sampler nearest;
    private Sampler linear;
    private bool disposed;

    public ulong ResidentInputBytes { get; }
    public ulong CommonOutputBytes { get; }

    public VulkanTextureCodecProbe(AurelianVulkanPlant plant, byte[] program, string entry, int capacity,
        CodecTexture first, CodecTexture second, byte[]? packedWeights = null, CodecTexture? coarse = null)
    {
        if (capacity is < 1 or > 1_048_576 || packedWeights is not null && packedWeights.Length != 1024
            || coarse is not null && packedWeights is null)
            throw new ArgumentException("Codec probe needs a bounded sample count and two packed FP16 weight tiles for the neural route.");
        bool cooperative = packedWeights is not null;
        if (cooperative && !plant.Facts.CooperativeMatrixEnabled)
            throw new NotSupportedException(plant.Facts.CooperativeMatrixDiagnostic);
        this.plant = plant;
        this.capacity = capacity;
        pixelsPerGroup = cooperative ? 16 : 64;
        split = coarse is not null;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        timings = new(plant, ["sample-material"]);
        try
        {
            demand = Buffer(24, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu, map: true);
            output = Buffer((ulong)capacity * 12, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource, VulkanMemoryUsage.GpuOnly);
            CommonOutputBytes = AllocationSize(output);
            var a = Upload(first);
            var b = Upload(second);
            var c = coarse is null ? null : Upload(coarse);
            var weights = demand;
            if (packedWeights is not null)
            {
                weights = Buffer((ulong)packedWeights.Length, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly);
                using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
                var uploaded = uploader.Upload(new(weights, packedWeights, DebugName: "texture-codec.weights"));
                Require(uploaded.Success, string.Join(" ", uploaded.Diagnostics.Select(diagnostic => diagnostic.Message)));
            }
            ResidentInputBytes = a.ResourceState.SizeBytes + b.ResourceState.SizeBytes + (c?.ResourceState.SizeBytes ?? 0)
                + (cooperative ? AllocationSize(weights) : 0);
            nearest = Vulkan3DPass.CreateSampler(plant, Filter.Nearest, SamplerAddressMode.Repeat);
            linear = Vulkan3DPass.CreateSampler(plant, Filter.Linear, SamplerAddressMode.Repeat);
            CreatePipeline(program, entry, cooperative);
            Vulkan3DPass.WriteImage(plant, set, 0, a, null);
            Vulkan3DPass.WriteImage(plant, set, 1, b, null);
            Vulkan3DPass.WriteImage(plant, set, 6, c ?? a, null);
            WriteSampler(7, linear);
            WriteBuffer(3, weights);
            WriteBuffer(4, demand);
            WriteBuffer(5, output);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public TextureCodecCapture Run(uint width, uint height, bool oblique, bool shade, bool capture, int trials = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width == 0 || height == 0 || (ulong)width * height > (ulong)capacity || trials is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(width));
        int pixels = checked((int)(width * height));
        int groups = (pixels + pixelsPerGroup - 1) / pixelsPerGroup;
        int columns = Math.Min(4096, groups);
        uint[] parameters = [width, height, oblique ? 1u : 0u, shade ? 1u : 0u, split ? 1u : 0u, (uint)(columns * pixelsPerGroup)];
        Require(demand.Write(MemoryMarshal.AsBytes(parameters.AsSpan())).Success, "Codec sample parameters upload failed.");
        WriteSampler(2, oblique ? linear : nearest);
        int warmup = trials > 1 ? 8 : 0;
        int replays = trials > 1 ? 16 : 1;
        var measurements = new double[trials];
        for (int trial = -warmup; trial < trials; trial++)
        {
            var command = Begin();
            Barrier(command, AccessFlags.HostWriteBit | AccessFlags.TransferWriteBit | AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.HostBit | PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            timings.Reset(command);
            timings.Mark(command, 0);
            DescriptorSet descriptor = set;
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &descriptor, 0, null);
            for (int replay = 0; replay < replays; replay++)
            {
                if (replay != 0)
                    Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.ShaderWriteBit,
                        PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
                plant.Vk.CmdDispatch(command.CommandBuffer, (uint)columns, (uint)((groups + columns - 1) / columns), 1);
            }
            timings.Mark(command, 1);
            Submit(command);
            if (trial >= 0) measurements[trial] = timings.Read().Single().Milliseconds / replays;
        }
        Array.Sort(measurements);
        uint[]? samples = capture ? Readback(pixels) : null;
        return new(measurements[trials / 2], measurements[(int)Math.Ceiling(trials * .95) - 1], samples, pixels);
    }

    private ulong AllocationSize(AurelianVulkanBuffer buffer)
    {
        plant.Vk.GetBufferMemoryRequirements(plant.Device, buffer.NativeBuffer, out var requirements);
        return requirements.Size;
    }

    private AurelianVulkanTexture Upload(CodecTexture texture)
    {
        var result = VulkanTextureFactory.Create(plant, allocator, new(plant.Context.Id, texture.Width, texture.Height, texture.Format,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
            VulkanResourceLayout.Undefined, DebugName: "texture-codec.image"));
        Require(result.Success, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var image = result.Texture!;
        owned.Add(image);
        using var uploader = new VulkanTextureUploader(plant, allocator, commands, fences);
        var upload = uploader.Upload(new(image, texture.Bytes, "texture-codec.image", VulkanResourceLayout.ShaderResourceCompute));
        Require(upload.Success, string.Join(" ", upload.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return image;
    }

    private uint[] Readback(int pixels)
    {
        ulong length = (ulong)pixels * 12;
        using var destination = Buffer(length, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, map: true, keep: false);
        var command = Begin();
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        BufferCopy region = new(0, 0, length);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, output.NativeBuffer, destination.NativeBuffer, 1, &region);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Submit(command);
        return MemoryMarshal.Cast<byte, uint>(destination.ReadBytes((int)length)).ToArray();
    }

    private VulkanCommandBufferLease Begin()
    {
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Codec command begin failed.");
        return command;
    }

    private void Submit(VulkanCommandBufferLease command)
    {
        Require(command.End().Success, "Codec command end failed.");
        Require(submitter.Submit(new(command, DebugName: "texture-codec")).Success, "Codec command submit failed.");
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags before, AccessFlags after,
        PipelineStageFlags source, PipelineStageFlags destination)
    {
        var memory = new MemoryBarrier(StructureType.MemoryBarrier, null, before, after);
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, source, destination, 0, 1, &memory, 0, null, 0, null);
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory, bool map = false, bool keep = true)
    {
        var result = VulkanBufferFactory.Create(plant, allocator,
            new(plant.Context.Id, bytes, usage, memory, "texture-codec.buffer", MapOnCreate: map));
        Require(result.Success, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (keep) owned.Add(result.Buffer!);
        return result.Buffer!;
    }

    private void CreatePipeline(byte[] spirv, string entry, bool cooperative)
    {
        DescriptorType[] types = [DescriptorType.SampledImage, DescriptorType.SampledImage, DescriptorType.Sampler,
            DescriptorType.StorageBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer, DescriptorType.SampledImage, DescriptorType.Sampler];
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[8];
        for (uint binding = 0; binding < types.Length; binding++)
            bindings[binding] = new(binding, types[binding], 1, ShaderStageFlags.ComputeBit);
        DescriptorSetLayoutCreateInfo setInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 8, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &setInfo, null, out setLayout));
        var local = setLayout;
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3]
        {
            new(DescriptorType.SampledImage, 3), new(DescriptorType.Sampler, 2), new(DescriptorType.StorageBuffer, 3),
        };
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 3, PPoolSizes = sizes };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
        DescriptorSetAllocateInfo setAllocate = new() { SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &local };
        fixed (DescriptorSet* descriptor = &set)
            Check(plant.Vk.AllocateDescriptorSets(plant.Device, &setAllocate, descriptor));
        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &local };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, out layout));
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            ShaderModuleCreateInfo code = new() { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)bytes };
            Check(plant.Vk.CreateShaderModule(plant.Device, &code, null, out module));
        }
        nint name = SilkMarshal.StringToPtr(entry);
        try
        {
            ComputePipelineCreateInfo info = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Module = module, Stage = ShaderStageFlags.ComputeBit,
                    Flags = cooperative ? PipelineShaderStageCreateFlags.RequireFullSubgroupsBit : 0, PName = (byte*)name }};
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &info, null, out pipeline));
        }
        finally
        {
            SilkMarshal.Free(name);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private void WriteBuffer(uint binding, AurelianVulkanBuffer buffer)
    {
        var info = new DescriptorBufferInfo(buffer.NativeBuffer, 0, buffer.SizeBytes);
        WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, PBufferInfo = &info };
        plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
    }

    private void WriteSampler(uint binding, Sampler sampler)
    {
        var info = new DescriptorImageInfo(sampler, default, ImageLayout.Undefined);
        WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorType = DescriptorType.Sampler, DescriptorCount = 1, PImageInfo = &info };
        plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
    }

    private static void Check(Result result) => Require(result == Result.Success, "Texture codec Vulkan operation failed: " + result);
    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timings.Dispose();
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (layout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, layout, null);
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        if (nearest.Handle != 0) plant.Vk.DestroySampler(plant.Device, nearest, null);
        if (linear.Handle != 0) plant.Vk.DestroySampler(plant.Device, linear, null);
        foreach (var resource in owned) resource.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
