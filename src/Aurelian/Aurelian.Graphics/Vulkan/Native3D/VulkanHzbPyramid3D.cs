using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record HzbPyramidReadback(uint Width, uint Height, uint[] Offsets, uint[] Widths, uint[] Heights, float[] Seed, float[] History);
public sealed record HierarchicalZPrograms(byte[] Cull, byte[] Reduce);

/// <summary>GPU-only max-depth mip atlases. History is ordered by the graphics queue, not frame-slot age.</summary>
internal sealed unsafe class VulkanHzbPyramid3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanFrameInFlightSet frames;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer[,] views = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count, 2];
    private readonly uint[] offsets;
    private readonly uint[] widths;
    private readonly uint[] heights;
    private readonly DescriptorSet[,] sets;
    private DescriptorSetLayout setLayout;
    private DescriptorPool pool;
    private PipelineLayout layout;
    private Pipeline pipeline;
    private Matrix4x4 previousClip;
    private bool historyValid;
    public AurelianVulkanBuffer Seed { get; private set; } = null!;
    public AurelianVulkanBuffer History { get; private set; } = null!;
    public ulong Bytes => Seed.SizeBytes + History.SizeBytes;
    public AurelianVulkanBuffer View(int slot, bool seedPhase) => views[slot, seedPhase ? 0 : 1];

    public VulkanHzbPyramid3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        byte[] shader, uint width, uint height)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.commands = commands;
        this.fences = fences;
        this.frames = frames;
        var levels = new List<(uint Width, uint Height, uint Offset)>();
        uint words = 0;
        while (true)
        {
            levels.Add((width, height, words));
            words = checked(words + width * height);
            if (width == 1 && height == 1) break;
            width = (width + 1) / 2;
            height = (height + 1) / 2;
        }
        if (levels.Count > 16) throw new InvalidDataException("AUR-HZB-001: Pyramid exceeds the 16-level shader contract.");
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        if (words * 4UL > properties.Limits.MaxStorageBufferRange
            || levels.Count > 1 && ((ulong)levels[1].Width * levels[1].Height + 63) / 64 > properties.Limits.MaxComputeWorkGroupCount[0])
            throw new InvalidDataException("AUR-HZB-001: Pyramid exceeds the device storage range or reduction-dispatch limit.");
        widths = levels.Select(level => level.Width).ToArray();
        heights = levels.Select(level => level.Height).ToArray();
        offsets = levels.Select(level => level.Offset).ToArray();
        sets = new DescriptorSet[2, levels.Count - 1];
        try
        {
            Seed = Buffer(words * 4UL, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination | VulkanBufferUsage.TransferSource);
            History = Buffer(words * 4UL, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination | VulkanBufferUsage.TransferSource);
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            {
                views[slot, 0] = Buffer(80, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
                views[slot, 1] = Buffer(80, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            }
            CreatePipeline(shader);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void UpdateView(Matrix4x4 clip)
    {
        WriteView(views[frames.Slot, 0], previousClip, historyValid);
        WriteView(views[frames.Slot, 1], clip, true);
        previousClip = clip;
        historyValid = true;
    }

    private void WriteView(AurelianVulkanBuffer buffer, Matrix4x4 clip, bool valid)
    {
        byte[] data = new byte[80];
        MemoryMarshal.AsBytes(Lighting3DUniforms.Rows(clip).AsSpan()).CopyTo(data);
        Span<uint> metadata = MemoryMarshal.Cast<byte, uint>(data.AsSpan(64));
        metadata[0] = widths[0];
        metadata[1] = heights[0];
        metadata[2] = (uint)widths.Length;
        metadata[3] = valid ? 1u : 0u;
        Require(buffer.Write(data).Success, "View upload failed.");
    }

    public void Record(VulkanCommandBufferLease command, AurelianVulkanTexture depth, bool history)
    {
        int atlas = history ? 1 : 0;
        var destination = history ? History : Seed;
        Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit, AccessFlags.TransferWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        DepthTransition(command, depth, ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.TransferReadBit,
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit, PipelineStageFlags.TransferBit);
        BufferImageCopy copy = new() { ImageSubresource = new(ImageAspectFlags.DepthBit, 0, 0, 1),
            ImageExtent = new(depth.Width, depth.Height, 1) };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, depth.NativeImage, ImageLayout.TransferSrcOptimal,
            destination.NativeBuffer, 1, &copy);
        DepthTransition(command, depth, ImageLayout.TransferSrcOptimal, ImageLayout.DepthStencilAttachmentOptimal,
            AccessFlags.TransferReadBit, AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            PipelineStageFlags.TransferBit, PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        for (int level = 1; level < widths.Length; level++)
        {
            DescriptorSet set = sets[atlas, level - 1];
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, (widths[level] * heights[level] + 63) / 64, 1, 1);
            Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }
    }

    private void CreatePipeline(byte[] shader)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2]
        {
            new(0, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
        };
        DescriptorSetLayoutCreateInfo setInfo = new() { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 2, PBindings = bindings };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &setInfo, null, out setLayout));
        uint setCount = (uint)Math.Max(1, 2 * (widths.Length - 1));
        DescriptorPoolSize size = new(DescriptorType.StorageBuffer, setCount * 2);
        DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = setCount, PoolSizeCount = 1, PPoolSizes = &size };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
        DescriptorSetLayout nativeLayout = setLayout;
        PipelineLayoutCreateInfo layoutInfo = new() { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &nativeLayout };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, out layout));
        for (int level = 1; level < widths.Length; level++)
        {
            var parameters = Buffer(24, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            uint[] words = [offsets[level - 1], widths[level - 1], heights[level - 1], offsets[level], widths[level], heights[level]];
            Require(parameters.Write(MemoryMarshal.AsBytes(words.AsSpan())).Success, "Reduction parameters failed.");
            for (int atlas = 0; atlas < 2; atlas++)
            {
                DescriptorSetAllocateInfo allocation = new() { SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &nativeLayout };
                DescriptorSet set;
                Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
                sets[atlas, level - 1] = set;
                Write(set, 0, atlas == 0 ? Seed : History);
                Write(set, 1, parameters);
            }
        }
        ShaderModule module;
        fixed (byte* bytes = shader)
        {
            ShaderModuleCreateInfo info = new() { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)shader.Length, PCode = (uint*)bytes };
            Check(plant.Vk.CreateShaderModule(plant.Device, &info, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr("HzbReduceMain");
        try
        {
            ComputePipelineCreateInfo info = new() { SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit,
                    Module = module, PName = (byte*)entry } };
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &info, null, out pipeline));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private void Write(DescriptorSet set, uint binding, AurelianVulkanBuffer buffer)
    {
        DescriptorBufferInfo info = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
        WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, PBufferInfo = &info };
        plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory = VulkanMemoryUsage.GpuOnly)
    {
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "hzb", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        Require(created.Success, "Buffer allocation failed.");
        buffers.Add(created.Buffer!);
        return created.Buffer!;
    }

    public HzbPyramidReadback ReadForQualification()
    {
        frames.Drain();
        var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, Bytes,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "hzb.readback", MapOnCreate: true));
        Require(created.Success, "Readback allocation failed.");
        using var staging = created.Buffer!;
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Readback begin failed.");
        Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        BufferCopy first = new(0, 0, Seed.SizeBytes);
        BufferCopy second = new(0, Seed.SizeBytes, History.SizeBytes);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, Seed.NativeBuffer, staging.NativeBuffer, 1, &first);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, History.NativeBuffer, staging.NativeBuffer, 1, &second);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Readback end failed.");
        using var submitter = new VulkanCommandSubmitter(plant, commands, fences);
        Require(submitter.Submit(new(command, DebugName: "hzb.qualification")).Success, "Readback submit failed.");
        float[] values = MemoryMarshal.Cast<byte, float>(staging.ReadBytes((int)Bytes)).ToArray();
        int words = values.Length / 2;
        return new(widths[0], heights[0], offsets, widths, heights, values[..words], values[words..]);
    }

    private void DepthTransition(VulkanCommandBufferLease command, AurelianVulkanTexture depth,
        ImageLayout before, ImageLayout after, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        ImageMemoryBarrier barrier = new() { SType = StructureType.ImageMemoryBarrier, SrcAccessMask = source,
            DstAccessMask = destination, OldLayout = before, NewLayout = after, Image = depth.NativeImage,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            SubresourceRange = new(ImageAspectFlags.DepthBit, 0, 1, 0, 1) };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException("AUR-HZB-002: " + message);
    }
    private static void Check(Result result) => Require(result == Result.Success, result.ToString());

    public void Dispose()
    {
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (layout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, layout, null);
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        foreach (var buffer in buffers) buffer.Dispose();
    }
}
