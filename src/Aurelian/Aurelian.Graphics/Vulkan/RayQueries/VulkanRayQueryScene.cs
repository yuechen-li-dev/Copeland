using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Aurelian.Graphics.Vulkan.RayQueries;

public readonly record struct RayQueryTriangle(Vector3 A, Vector3 B, Vector3 C, uint Layer = 1, uint Mask = uint.MaxValue);
public readonly record struct RayQuerySphere(Vector3 Center, float Radius, uint Layer = 1, uint Mask = uint.MaxValue);
public readonly record struct RayQueryRequest(Vector3 Origin, Vector3 Direction, float MaximumDistance,
    uint IncludedLayers = uint.MaxValue, uint QueryLayer = uint.MaxValue);
public readonly record struct RayQueryHit(uint Kind, uint Primitive, float Distance, Vector3 Point, Vector3 Normal, Vector2 Barycentrics);

/// <summary>Retained BLAS/TLAS and batched inline traversal, adapted from Oct's
/// reactor_vulkan_ray_query.c. Uses the existing Aurelian plant, allocator and submission owners.</summary>
public sealed unsafe class VulkanRayQueryScene : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly KhrAccelerationStructure acceleration;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly List<AccelerationStructureKHR> structures = [];
    private readonly object gate = new();
    private readonly int capacity;
    private AurelianVulkanBuffer rays = null!;
    private AurelianVulkanBuffer hits = null!;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;

    public ulong BottomLevelAddress { get; private set; }
    public ulong TopLevelAddress { get; private set; }
    public int DispatchCount { get; private set; }

    public VulkanRayQueryScene(AurelianVulkanPlant plant, byte[] spirv,
        IReadOnlyList<RayQueryTriangle> triangles, IReadOnlyList<RayQuerySphere>? spheres = null, int capacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(spirv);
        spheres ??= [];
        if (capacity is < 1 or > 1_048_576 || triangles.Count > 1_000_000 || spheres.Count > 1_000_000
            || triangles.Count + spheres.Count == 0
            || spirv.Length < 20 || spirv.Length % 4 != 0)
        {
            throw new ArgumentException("Ray query scenes need geometry, aligned SPIR-V and a bounded batch capacity.");
        }
        if (!plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
            || !plant.Vk.TryGetDeviceExtension(plant.Instance, plant.Device, out acceleration!))
        {
            throw new NotSupportedException("This plant has not enabled hardware ray queries.");
        }
        this.plant = plant;
        this.capacity = capacity;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            float[] triangleWords = new float[Math.Max(4, checked(triangles.Count * 12))];
            for (int index = 0; index < triangles.Count; index++)
            {
                var triangle = triangles[index];
                RequireFinite(triangle.A);
                RequireFinite(triangle.B);
                RequireFinite(triangle.C);
                if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() < 1e-16f)
                    throw new ArgumentException("Degenerate ray query triangle.");
                WritePoint(triangleWords, index * 12, triangle.A);
                WritePoint(triangleWords, index * 12 + 4, triangle.B);
                WritePoint(triangleWords, index * 12 + 8, triangle.C);
                triangleWords[index * 12 + 3] = Bits(triangle.Layer);
                triangleWords[index * 12 + 7] = Bits(triangle.Mask);
            }
            var vertices = Buffer((ulong)triangleWords.Length * 4, VulkanBufferUsage.Storage
                | VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
            Write(vertices, triangleWords);
            float[] sphereWords = new float[Math.Max(8, checked(spheres.Count * 8))];
            float[] aabbWords = new float[Math.Max(6, checked(spheres.Count * 6))];
            for (int index = 0; index < spheres.Count; index++)
            {
                var sphere = spheres[index];
                RequireFinite(sphere.Center);
                if (!float.IsFinite(sphere.Radius) || sphere.Radius <= 0) throw new ArgumentException("Invalid ray query sphere radius.");
                Vector3 minimum = sphere.Center - new Vector3(sphere.Radius);
                Vector3 maximum = sphere.Center + new Vector3(sphere.Radius);
                RequireFinite(minimum);
                RequireFinite(maximum);
                WritePoint(sphereWords, index * 8, sphere.Center);
                sphereWords[index * 8 + 3] = sphere.Radius;
                sphereWords[index * 8 + 4] = Bits(sphere.Layer);
                sphereWords[index * 8 + 5] = Bits(sphere.Mask);
                WritePoint(aabbWords, index * 6, minimum);
                WritePoint(aabbWords, index * 6 + 3, maximum);
            }
            var sphereData = Buffer((ulong)sphereWords.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Write(sphereData, sphereWords);
            var instances = new List<ulong>();
            if (triangles.Count > 0)
            {
                var geometry = new AccelerationStructureGeometryKHR
                {
                    SType = StructureType.AccelerationStructureGeometryKhr,
                    GeometryType = GeometryTypeKHR.TrianglesKhr,
                    Geometry = new AccelerationStructureGeometryDataKHR
                    {
                        Triangles = new AccelerationStructureGeometryTrianglesDataKHR
                        {
                            SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                            VertexFormat = Format.R32G32B32Sfloat,
                            VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(vertices) },
                            VertexStride = 16,
                            MaxVertex = (uint)triangles.Count * 3 - 1,
                            IndexType = IndexType.NoneKhr,
                        },
                    },
                };
                BottomLevelAddress = Build(AccelerationStructureTypeKHR.BottomLevelKhr, geometry, (uint)triangles.Count);
                instances.Add(BottomLevelAddress);
            }
            if (spheres.Count > 0)
            {
                var aabbs = Buffer((ulong)aabbWords.Length * 4, VulkanBufferUsage.AccelerationStructureInput
                    | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
                Write(aabbs, aabbWords);
                var geometry = new AccelerationStructureGeometryKHR
                {
                    SType = StructureType.AccelerationStructureGeometryKhr,
                    GeometryType = GeometryTypeKHR.AabbsKhr,
                    Geometry = new AccelerationStructureGeometryDataKHR
                    {
                        Aabbs = new AccelerationStructureGeometryAabbsDataKHR
                        {
                            SType = StructureType.AccelerationStructureGeometryAabbsDataKhr,
                            Data = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(aabbs) },
                            Stride = 24,
                        },
                    },
                };
                instances.Add(Build(AccelerationStructureTypeKHR.BottomLevelKhr, geometry, (uint)spheres.Count));
            }
            byte[] instanceBytes = new byte[instances.Count * 64];
            for (int index = 0; index < instances.Count; index++)
            {
                Span<float> matrix = MemoryMarshal.Cast<byte, float>(instanceBytes.AsSpan(index * 64, 48));
                matrix[0] = matrix[5] = matrix[10] = 1;
                BitConverter.TryWriteBytes(instanceBytes.AsSpan(index * 64 + 48, 4), 0xff000000u);
                BitConverter.TryWriteBytes(instanceBytes.AsSpan(index * 64 + 56, 8), instances[index]);
            }
            var instanceBuffer = Buffer((ulong)instanceBytes.Length, VulkanBufferUsage.AccelerationStructureInput
                | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
            if (!instanceBuffer.Write(instanceBytes).Success) throw new InvalidOperationException("Instance upload failed.");
            var instanceGeometry = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.InstancesKhr,
                Geometry = new AccelerationStructureGeometryDataKHR
                {
                    Instances = new AccelerationStructureGeometryInstancesDataKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                        Data = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(instanceBuffer) },
                    },
                },
            };
            TopLevelAddress = Build(AccelerationStructureTypeKHR.TopLevelKhr, instanceGeometry, (uint)instances.Count);
            rays = Buffer((ulong)(4 + capacity * 12) * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            hits = Buffer((ulong)capacity * 64, VulkanBufferUsage.Storage, VulkanMemoryUsage.GpuToCpu);
            CreatePipeline(spirv, structures[^1], sphereData, vertices);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public RayQueryHit[] Trace(IReadOnlyList<RayQueryRequest> requests)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requests.Count > capacity) throw new ArgumentOutOfRangeException(nameof(requests));
            if (requests.Count == 0) return [];
            float[] words = new float[4 + requests.Count * 12];
            words[0] = Bits((uint)requests.Count);
            for (int index = 0; index < requests.Count; index++)
            {
                RayQueryRequest ray = requests[index];
                RequireFinite(ray.Origin);
                RequireFinite(ray.Direction);
                if (MathF.Abs(ray.Direction.LengthSquared() - 1) > 0.0001f
                    || !float.IsFinite(ray.MaximumDistance) || ray.MaximumDistance <= 0)
                    throw new ArgumentException("GPU rays need unit directions and positive finite ranges.");
                int offset = 4 + index * 12;
                WritePoint(words, offset, ray.Origin);
                WritePoint(words, offset + 4, ray.Direction);
                words[offset + 7] = ray.MaximumDistance;
                words[offset + 8] = Bits(ray.IncludedLayers);
                words[offset + 9] = Bits(ray.QueryLayer);
            }
            Write(rays, words);
            VulkanCommandBufferLease command = Begin();
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
            DescriptorSet set = descriptorSet;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, ((uint)requests.Count + 63) / 64, 1, 1);
            Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit, AccessFlags.HostReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit);
            Submit(command);
            DispatchCount++;
            float[] data = MemoryMarshal.Cast<byte, float>(hits.ReadBytes(requests.Count * 64)).ToArray();
            var result = new RayQueryHit[requests.Count];
            for (int index = 0; index < result.Length; index++)
            {
                int offset = index * 16;
                result[index] = new(BitConverter.SingleToUInt32Bits(data[offset]), BitConverter.SingleToUInt32Bits(data[offset + 1]),
                    data[offset + 4], new(data[offset + 8], data[offset + 9], data[offset + 10]),
                    new(data[offset + 12], data[offset + 13], data[offset + 14]), new(data[offset + 5], data[offset + 6]));
            }
            return result;
        }
    }

    private ulong Build(AccelerationStructureTypeKHR type, AccelerationStructureGeometryKHR geometry, uint count)
    {
        var info = new AccelerationStructureBuildGeometryInfoKHR
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = type,
            Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            GeometryCount = 1,
            PGeometries = &geometry,
        };
        var sizes = new AccelerationStructureBuildSizesInfoKHR { SType = StructureType.AccelerationStructureBuildSizesInfoKhr };
        acceleration.GetAccelerationStructureBuildSizes(plant.Device, AccelerationStructureBuildTypeKHR.DeviceKhr, &info, &count, &sizes);
        var backing = Buffer(sizes.AccelerationStructureSize, VulkanBufferUsage.AccelerationStructureStorage | VulkanBufferUsage.ShaderDeviceAddress,
            VulkanMemoryUsage.GpuOnly);
        var create = new AccelerationStructureCreateInfoKHR
        {
            SType = StructureType.AccelerationStructureCreateInfoKhr,
            Buffer = backing.NativeBuffer,
            Size = sizes.AccelerationStructureSize,
            Type = type,
        };
        Check(acceleration.CreateAccelerationStructure(plant.Device, &create, null, out AccelerationStructureKHR structure));
        structures.Add(structure);
        var properties = new PhysicalDeviceAccelerationStructurePropertiesKHR { SType = StructureType.PhysicalDeviceAccelerationStructurePropertiesKhr };
        var physical = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &properties };
        plant.Vk.GetPhysicalDeviceProperties2(plant.PhysicalDevice, &physical);
        ulong alignment = properties.MinAccelerationStructureScratchOffsetAlignment;
        using var scratch = Buffer(sizes.BuildScratchSize + alignment, VulkanBufferUsage.Storage | VulkanBufferUsage.ShaderDeviceAddress,
            VulkanMemoryUsage.GpuOnly);
        ulong address = Address(scratch);
        info.DstAccelerationStructure = structure;
        info.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = (address + alignment - 1) / alignment * alignment };
        var range = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = count };
        AccelerationStructureBuildRangeInfoKHR* rangePointer = &range;
        VulkanCommandBufferLease command = Begin();
        acceleration.CmdBuildAccelerationStructures(command.CommandBuffer, 1, &info, &rangePointer);
        Barrier(command.CommandBuffer, AccessFlags.AccelerationStructureWriteBitKhr,
            AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.ShaderReadBit,
            PipelineStageFlags.AccelerationStructureBuildBitKhr,
            PipelineStageFlags.AccelerationStructureBuildBitKhr | PipelineStageFlags.ComputeShaderBit);
        Submit(command);
        var addressInfo = new AccelerationStructureDeviceAddressInfoKHR
        {
            SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
            AccelerationStructure = structure,
        };
        ulong result = acceleration.GetAccelerationStructureDeviceAddress(plant.Device, &addressInfo);
        if (result == 0) throw new InvalidOperationException("Acceleration structure has no device address.");
        return result;
    }

    private void CreatePipeline(byte[] spirv, AccelerationStructureKHR tlas, AurelianVulkanBuffer spheres, AurelianVulkanBuffer triangles)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[5];
        for (uint index = 0; index < 5; index++) bindings[index] = new(index,
            index == 0 ? DescriptorType.AccelerationStructureKhr : DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        var layout = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 5, PBindings = bindings };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new(DescriptorType.AccelerationStructureKhr, 1);
        sizes[1] = new(DescriptorType.StorageBuffer, 4);
        var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 2, PPoolSizes = sizes };
        fixed (DescriptorPool* value = &descriptorPool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        DescriptorSetLayout descriptorLayout = setLayout;
        var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descriptorPool,
            DescriptorSetCount = 1, PSetLayouts = &descriptorLayout };
        fixed (DescriptorSet* value = &descriptorSet) Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, value));
        var structureWrite = new WriteDescriptorSetAccelerationStructureKHR
        {
            SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
            AccelerationStructureCount = 1,
            PAccelerationStructures = &tlas,
        };
        AurelianVulkanBuffer[] resources = [spheres, rays, hits, triangles];
        DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[4];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[5];
        writes[0] = new() { SType = StructureType.WriteDescriptorSet, PNext = &structureWrite, DstSet = descriptorSet,
            DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.AccelerationStructureKhr };
        for (int index = 0; index < resources.Length; index++)
        {
            bufferInfos[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
            writes[index + 1] = new() { SType = StructureType.WriteDescriptorSet, DstSet = descriptorSet, DstBinding = (uint)index + 1,
                DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &bufferInfos[index] };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 5, writes, 0, null);
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1, PSetLayouts = &descriptorLayout };
        fixed (PipelineLayout* value = &pipelineLayout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineLayoutInfo, null, value));
        ShaderModule module;
        fixed (byte* code = spirv)
        {
            var shader = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
            Check(plant.Vk.CreateShaderModule(plant.Device, &shader, null, out module));
        }
        nint entry = SilkMarshal.StringToPtr("RayQueryMain");
        try
        {
            var create = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)entry },
                Layout = pipelineLayout,
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
        var result = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "rayquery", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private ulong Address(AurelianVulkanBuffer buffer)
    {
        var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer.NativeBuffer };
        ulong address = plant.Vk.GetBufferDeviceAddress(plant.Device, &info);
        if (address == 0) throw new InvalidOperationException("Buffer has no device address.");
        return address;
    }

    private VulkanCommandBufferLease Begin()
    {
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        if (!command.Begin().Success) throw new InvalidOperationException("Ray query command begin failed.");
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit | AccessFlags.AccelerationStructureReadBitKhr,
            PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.AccelerationStructureBuildBitKhr);
        return command;
    }

    private void Submit(VulkanCommandBufferLease command)
    {
        if (!command.End().Success) throw new InvalidOperationException("Ray query command end failed.");
        var result = submitter.Submit(new(command, DebugName: "rayquery"));
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    private void Barrier(CommandBuffer command, AccessFlags source, AccessFlags destination, PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, float[] words)
    {
        if (!buffer.Write(MemoryMarshal.AsBytes(words.AsSpan())).Success) throw new InvalidOperationException("Ray query upload failed.");
    }
    private static float Bits(uint value) => BitConverter.UInt32BitsToSingle(value);
    private static void WritePoint(float[] words, int offset, Vector3 point)
    {
        words[offset] = point.X;
        words[offset + 1] = point.Y;
        words[offset + 2] = point.Z;
    }
    private static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new ArgumentException("Ray query values must be finite.");
    }
    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("Ray query Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
            if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
            if (pipelineLayout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
            if (descriptorPool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
            if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
            foreach (var structure in structures.AsEnumerable().Reverse()) acceleration.DestroyAccelerationStructure(plant.Device, structure, null);
            foreach (var buffer in buffers.AsEnumerable().Reverse()) buffer.Dispose();
            submitter.Dispose();
            commands.Dispose();
            fences.Dispose();
            allocator.Dispose();
            acceleration.Dispose();
        }
    }
}
