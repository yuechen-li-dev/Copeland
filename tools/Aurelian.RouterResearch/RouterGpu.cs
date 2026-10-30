using System.Runtime.InteropServices;
using System.Diagnostics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.RouterResearch;

// Research-only binding/recording harness. Allocation, commands, submission,
// completion and timestamp queries all use the existing Aurelian owners.
internal sealed unsafe class RouterGpu : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanBufferUploader uploader;
    private readonly Vulkan3DGpuTimings timings;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly RouterFixture fixture;
    private readonly AurelianVulkanBuffer input;
    private readonly AurelianVulkanBuffer tables;
    private readonly AurelianVulkanBuffer output;
    private readonly AurelianVulkanBuffer upload;
    private readonly AurelianVulkanBuffer readback;
    private readonly VulkanCommandBufferLease residentCommand;
    private readonly VulkanCommandBufferLease transferCommand;
    private DescriptorSetLayout descriptorLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    public string ReadbackMemoryFlags { get; }
    public double LastMappedReadMicroseconds { get; private set; }
    private ulong pendingFence;

    public RouterGpu(AurelianVulkanPlant plant, RouterFixture fixture, byte[] spirv, bool preferCachedReadback = true)
    {
        this.plant = plant;
        this.fixture = fixture;
        allocator = new(plant, preferCachedReadback);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        uploader = new(plant, allocator, commands, fences);
        timings = new(plant, ["packet-policy"]);
        try
        {
            input = Buffer((ulong)fixture.Packets.Length,
                VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly);
            tables = Buffer((ulong)fixture.Tables.Length * 4,
                VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly);
            output = Buffer((ulong)fixture.Count * RouterFixture.ResultWords * 4,
                VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource | VulkanBufferUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly);
            upload = Buffer(input.SizeBytes, VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
            readback = Buffer(output.SizeBytes, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu);
            plant.Vk.GetBufferMemoryRequirements(plant.Device, readback.NativeBuffer, out MemoryRequirements requirements);
            Require(allocator.TryFindMemoryTypeIndex(requirements.MemoryTypeBits, VulkanMemoryUsage.GpuToCpu, out uint memoryType),
                "Readback memory selection failed.");
            plant.Vk.GetPhysicalDeviceMemoryProperties(plant.PhysicalDevice, out PhysicalDeviceMemoryProperties properties);
            ReadbackMemoryFlags = properties.MemoryTypes[(int)memoryType].PropertyFlags.ToString();
            Require(uploader.Upload(new(tables, fixture.TableBytes())).Success, "Table upload failed.");
            Require(uploader.Upload(new(input, fixture.Packets)).Success, "Packet upload failed.");
            CreatePipeline(spirv);
            residentCommand = Record(false);
            transferCommand = Record(true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public double Execute(bool transfer, uint[] decisions)
    {
        Require(pendingFence == 0, "Complete the asynchronous packet batch before reuse.");
        if (transfer)
        {
            Require(upload.Write(fixture.Packets).Success, "Packet staging write failed.");
        }
        var command = transfer ? transferCommand : residentCommand;
        var result = submitter.Submit(new(command, DebugName: "router-research") { RetainForReplay = true });
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        double milliseconds = timings.Read().Single().Milliseconds;
        if (transfer)
        {
            long readStarted = Stopwatch.GetTimestamp();
            MemoryMarshal.Cast<byte, uint>(readback.ReadBytes((int)readback.SizeBytes)).CopyTo(decisions);
            LastMappedReadMicroseconds = Stopwatch.GetElapsedTime(readStarted).TotalMicroseconds;
        }
        return milliseconds * 1000;
    }

    public void SubmitTransfer()
    {
        Require(pendingFence == 0, "Cannot reuse an in-flight packet slot.");
        Require(upload.Write(fixture.Packets).Success, "Packet staging write failed.");
        // Aurelian retains prepared commands only for synchronous replay.
        // Its ordinary asynchronous path retires a command against the actual
        // timeline fence; record/rent again only after this slot has completed.
        var command = Record(true);
        var result = submitter.Submit(new(command, WaitForCompletion: false, DebugName: "router-stream"));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        pendingFence = result.SignalFenceValue!.Value;
    }

    public double CompleteTransfer(uint[] decisions)
    {
        Require(pendingFence != 0, "No packet batch is pending in this slot.");
        var completed = fences.CommandListFence.WaitForValue(pendingFence, 5_000_000_000);
        Require(completed.Success, "Packet stream completion failed.");
        double microseconds = timings.Read().Single().Milliseconds * 1000;
        MemoryMarshal.Cast<byte, uint>(readback.ReadBytes((int)readback.SizeBytes)).CopyTo(decisions);
        pendingFence = 0;
        return microseconds;
    }

    public void PoisonOutput()
    {
        var bytes = new byte[(int)output.SizeBytes];
        Array.Fill(bytes, (byte)0xCD);
        Require(uploader.Upload(new(output, bytes)).Success, "Output poisoning failed.");
    }

    public void UpdateSnapshot()
    {
        Require(uploader.Upload(new(tables, fixture.TableBytes())).Success, "Snapshot upload failed.");
    }

    private VulkanCommandBufferLease Record(bool transfer)
    {
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin(reusable: true).Success, "Router recording failed.");
        timings.Reset(command);
        if (transfer)
        {
            Barrier(command, AccessFlags.HostWriteBit, AccessFlags.TransferReadBit,
                PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit);
            var inputCopy = new BufferCopy(0, 0, input.SizeBytes);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, upload.NativeBuffer, input.NativeBuffer, 1, &inputCopy);
        }
        Barrier(command, AccessFlags.TransferWriteBit | AccessFlags.ShaderWriteBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        timings.Mark(command, 0);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute,
            pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command.CommandBuffer, (uint)((fixture.Count + 63) / 64), 1, 1);
        timings.Mark(command, 1);
        if (transfer)
        {
            Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
            var outputCopy = new BufferCopy(0, 0, output.SizeBytes);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, output.NativeBuffer, readback.NativeBuffer, 1, &outputCopy);
            Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit,
                PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        }
        Require(command.End().Success, "Router recording end failed.");
        return command;
    }

    private void CreatePipeline(byte[] spirv)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint index = 0; index < 3; index++)
        {
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        }
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };
        Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layoutInfo, null, out descriptorLayout));
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, 3);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &size,
        };
        Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out descriptorPool));
        DescriptorSetLayout layout = descriptorLayout;
        var allocation = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, out descriptorSet));
        AurelianVulkanBuffer[] resources = [input, tables, output];
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
                PBufferInfo = &infos[index],
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 3, writes, 0, null);
        var pipelineInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &layout,
        };
        Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineInfo, null, out pipelineLayout));
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)bytes,
            };
            Check(plant.Vk.CreateShaderModule(plant.Device, &moduleInfo, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr("Main");
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
                    PName = (byte*)entry,
                },
            };
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, out pipeline));
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
            new(plant.Context.Id, bytes, usage, memory, "router-research", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = source,
            DstAccessMask = destination,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Check(Result result)
    {
        Require(result == Result.Success, "Router Vulkan operation failed: " + result);
    }

    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        if (pipeline.Handle != 0)
        {
            plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        }
        if (pipelineLayout.Handle != 0)
        {
            plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        }
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        if (descriptorLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, descriptorLayout, null);
        }
        timings.Dispose();
        foreach (var buffer in buffers.AsEnumerable().Reverse())
        {
            buffer.Dispose();
        }
        uploader.Dispose();
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
