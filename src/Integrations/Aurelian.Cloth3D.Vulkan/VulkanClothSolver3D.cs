using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Cloth3D.Vulkan;

/// <summary>Retained cloth storage and explicitly ordered compute dispatches. Step waits
/// for completion; Capture is the full-state readback. This baseline uses the graphics queue.</summary>
public sealed unsafe class VulkanClothSolver3D : IClothSolver3D
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer state;
    private readonly AurelianVulkanBuffer header;
    private readonly ClothGpuData3D data;
    private readonly uint headerStride;
    private const int HeaderCapacity = 4096;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;

    public CompiledCloth3D Plan => data.Plan;
    public long Tick { get; private set; }
    public long DispatchCount { get; private set; }

    public VulkanClothSolver3D(AurelianVulkanPlant plant, CompiledCloth3D plan, byte[] spirv)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(spirv);
        if (spirv.Length < 20 || spirv.Length % 4 != 0)
        {
            throw new ArgumentException("Cloth compute requires aligned SPIR-V.");
        }
        this.plant = plant;
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out var properties);
        ulong alignment = Math.Max(4, properties.Limits.MinStorageBufferOffsetAlignment);
        headerStride = checked((uint)((128 + alignment - 1) / alignment * alignment));
        data = new(plan);
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            var source = Buffer((ulong)data.Source.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Write(source, data.Source);
            header = Buffer((ulong)HeaderCapacity * headerStride, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            using var reference = new ClothSolver3D(plan);
            float[] initial = data.State(reference.Capture());
            state = Buffer((ulong)initial.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.GpuToCpu);
            Write(state, initial);
            CreatePipeline(spirv, "Main", [source, header, state]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetPin(int vertex, System.Numerics.Vector3 target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ClothState3D.ValidatePin(Plan, vertex, target);
        float[] values = [target.X, target.Y, target.Z];
        Require(state.Write(MemoryMarshal.AsBytes(values.AsSpan()), (ulong)(vertex * 16 + 10) * 4).Success, "Cloth pin upload failed.");
    }

    public void Step(ClothStepOptions3D options, ClothContacts3D contacts)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ClothState3D.ValidateStep(Plan, options, contacts);
        int trianglePasses = contacts.SphereCenter.HasValue ? data.Triangles.Length : 0;
        int dispatches = 1 + options.Substeps * (2 + options.Iterations *
            (data.Constraints.Length + options.ContactIterations * (1 + trianglePasses)));
        if (dispatches > HeaderCapacity)
        {
            throw new NotSupportedException("AUR-CLOTH-002: Vulkan baseline supports at most 4096 dispatches per tick; reduce the explicit solve budget.");
        }
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Cloth command begin failed.");
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit | AccessFlags.ShaderWriteBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        int headerIndex = 0;
        float[] headerValues = data.Header(options, contacts, new(0, 0, 0), 0);
        int vertices = Plan.Definition.Positions.Length;
        Dispatch(new(5, 0, vertices), 0);
        for (int substep = 0; substep < options.Substeps; substep++)
        {
            float fraction = (substep + 1f) / options.Substeps;
            Dispatch(new(0, 0, vertices), fraction);
            for (int iteration = 0; iteration < options.Iterations; iteration++)
            {
                foreach (var color in data.Constraints)
                {
                    Dispatch(color with { ResetLambda = iteration == 0 }, fraction);
                }
                for (int contact = 0; contact < options.ContactIterations; contact++)
                {
                    Dispatch(new(2, 0, vertices), fraction);
                    if (contacts.SphereCenter.HasValue)
                    {
                        foreach (var color in data.Triangles)
                        {
                            Dispatch(color, fraction);
                        }
                    }
                }
            }
            Dispatch(new(4, 0, vertices), fraction);
        }
        Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit, AccessFlags.HostReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Cloth command end failed.");
        var submitted = submitter.Submit(new(command, DebugName: "cloth-xpbd"));
        Require(submitted.Success, string.Join("; ", submitted.Diagnostics.Select(item => item.Message)));
        Tick++;

        void Dispatch(ClothDispatch3D dispatch, float fraction)
        {
            headerValues[0] = dispatch.Phase;
            headerValues[1] = dispatch.Count;
            headerValues[2] = dispatch.Start;
            headerValues[9] = dispatch.ResetLambda ? 1 : 0;
            headerValues[10] = fraction;
            // Each dispatch gets an aligned, immutable header slice. All host writes
            // finish before submission. Reusing one slice would make every dispatch
            // observe the final header, and updating it on GPU adds avoidable copies.
            uint offset = checked((uint)headerIndex++ * headerStride);
            Require(header.Write(MemoryMarshal.AsBytes(headerValues.AsSpan()), offset).Success, "Cloth header upload failed.");
            DescriptorSet set = descriptorSet;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 1, &offset);
            Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            plant.Vk.CmdDispatch(command.CommandBuffer, ((uint)dispatch.Count + 63) / 64, 1, 1);
            DispatchCount++;
        }
    }

    public ClothSnapshot3D Capture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int bytes = checked(Plan.Definition.Positions.Length * 16 * sizeof(float));
        return data.Snapshot(MemoryMarshal.Cast<byte, float>(state.ReadBytes(bytes)), Tick);
    }

    public void Restore(ClothSnapshot3D snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Write(state, data.State(snapshot));
        Tick = snapshot.Tick;
    }
    private void CreatePipeline(byte[] spirv, string entryName, AurelianVulkanBuffer[] resources)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint index = 0; index < 3; index++)
        {
            DescriptorType type = index == 1 ? DescriptorType.StorageBufferDynamic : DescriptorType.StorageBuffer;
            bindings[index] = new(index, type, 1, ShaderStageFlags.ComputeBit);
        }
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };
        fixed (DescriptorSetLayout* value = &setLayout)
        {
            Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        }
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new(DescriptorType.StorageBuffer, 2);
        sizes[1] = new(DescriptorType.StorageBufferDynamic, 1);
        var pool = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
        };
        fixed (DescriptorPool* value = &descriptorPool)
        {
            Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        }
        DescriptorSetLayout descriptorLayout = setLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &descriptorLayout,
        };
        fixed (DescriptorSet* value = &descriptorSet)
        {
            Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, value));
        }
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[3];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[3];
        for (int index = 0; index < 3; index++)
        {
            infos[index] = new(resources[index].NativeBuffer, 0, index == 1 ? 128 : resources[index].SizeBytes);
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = (uint)index,
                DescriptorCount = 1,
                DescriptorType = index == 1 ? DescriptorType.StorageBufferDynamic : DescriptorType.StorageBuffer,
                PBufferInfo = &infos[index],
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 3, writes, 0, null);
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorLayout,
        };
        fixed (PipelineLayout* value = &pipelineLayout)
        {
            Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, value));
        }
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
        nint entry = SilkMarshal.StringToPtr(entryName);
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
            fixed (Pipeline* value = &pipeline)
            {
                Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
            }
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
            new(plant.Context.Id, bytes, usage, memory, "cloth-xpbd", MapOnCreate: true));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private void Barrier(CommandBuffer command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = source,
            DstAccessMask = destination,
        };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, ReadOnlySpan<float> words) =>
        Require(buffer.Write(MemoryMarshal.AsBytes(words)).Success, "Cloth upload failed.");

    private static void Check(Result result) => Require(result == Result.Success, "Cloth Vulkan operation failed: " + result);
    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
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
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        foreach (var buffer in buffers.AsEnumerable().Reverse())
        {
            buffer.Dispose();
        }
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
