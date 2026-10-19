using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record ComputeMathResult(int Dispatches, double? GpuMilliseconds);

/// <summary>Device-local tensors and bounded dispatch batches on the existing plant queue.
/// Upload and readback are explicit; an Execute call submits the whole GPU chain once.</summary>
public sealed unsafe partial class VulkanComputeMath : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Func<string, byte[]> compile;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanBufferUploader uploader;
    private readonly Vulkan3DGpuTimings timings;
    private readonly List<GpuTensor> tensors = [];
    private readonly List<DispatchSlot> slots = [];
    private readonly Dictionary<string, Pipeline> pipelines = new(StringComparer.Ordinal);
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private PipelineLayout pipelineLayout;
    private bool disposed;
    private AurelianVulkanBuffer? unsignedUploadStaging;

    private sealed record DispatchSlot(DescriptorSet Set, AurelianVulkanBuffer Parameters);
    public int MaximumDispatches { get; }
    public int MaximumTensorElements { get; }
    public int ReadbackCount { get; private set; }
    /// <summary>Owned buffer allocations including transfer staging and dispatch parameters.
    /// Excludes opaque driver allocations, pipelines and other plant users.</summary>
    public VulkanMemoryAllocatorTelemetry MemoryTelemetry => allocator.Telemetry;
    public bool CooperativeMatrixEnabled => plant.Facts.CooperativeMatrixEnabled;
    public IComputeStreamController? StreamController { get; set; }
    /// <summary>Opt-in FP16 inputs with FP32 cooperative accumulation. Requires
    /// numerical qualification for the model; small/unaligned affines retain FP32.</summary>
    public bool UseCooperativeLinear { get; set; }
    /// <summary>Preferred model attention query tile. Larger tiles amortize queue
    /// and barrier overhead without changing per-query arithmetic. AttentionTiled
    /// validates the value and keeps its explicit scratch allocation budget.</summary>
    public int AttentionQueryTileRows { get; set; } = 256;

    public VulkanComputeMath(AurelianVulkanPlant plant, Func<string, byte[]> compile, int maximumDispatches = 512,
        int maximumTensorElements = 1048576)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(compile);
        ComputeMathBatch.Require(maximumDispatches is >= 1 and <= 512, "Dispatch budget must be 1–512.");
        ComputeMathBatch.Require(maximumTensorElements is > 0 and <= 134217728, "Explicit tensor budget must be 1–134217728 elements.");
        this.plant = plant;
        this.compile = compile;
        MaximumDispatches = maximumDispatches;
        MaximumTensorElements = maximumTensorElements;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        uploader = new(plant, allocator, commands, fences);
        timings = new(plant, ["compute-math"]);
        try
        {
            CreateLayouts();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public GpuTensor CreateTensor(int rows, int columns, GpuTensorFormat format = GpuTensorFormat.Float32)
    {
        ComputeMathBatch.Require(rows is > 0 and <= 8192 && columns is > 0 and <= 65536
            && (long)rows * columns <= MaximumTensorElements && Enum.IsDefined(format),
            "Tensor dimensions must be positive, rows <= 8192, columns <= 65536 and elements within the explicit owner budget.");
        return AllocateTensor(rows, columns, format);
    }

    /// <summary>Resident sparse feature or topology rows. Attention retains its
    /// own token bounds; sparse callers tile matrix work and expanded features.
    /// Every allocation still observes the owner budget and device buffer range.</summary>
    public GpuTensor CreateSparseTensor(int rows, int columns, GpuTensorFormat format = GpuTensorFormat.Float32)
    {
        ComputeMathBatch.Require(rows is > 0 and <= 4194304 && columns is > 0 and <= 4096
            && (long)rows * columns <= MaximumTensorElements
            && format is GpuTensorFormat.Float32 or GpuTensorFormat.UInt32,
            "Sparse tensors require 1..4194304 rows, 1..4096 columns, FP32/UInt32 and the explicit element budget.");
        return AllocateTensor(rows, columns, format);
    }

    private GpuTensor AllocateTensor(int rows, int columns, GpuTensorFormat format)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ulong elements = (ulong)rows * (ulong)columns;
        ulong bytes = format switch
        {
            GpuTensorFormat.Complex32 => elements * 8,
            GpuTensorFormat.PackedFloat16 => (elements + 1) / 2 * 4,
            _ => elements * 4,
        };
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out var properties);
        ComputeMathBatch.Require(bytes <= properties.Limits.MaxStorageBufferRange, "Tensor exceeds this device's storage-buffer range.");
        var buffer = Buffer(bytes, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource
            | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly);
        var tensor = new GpuTensor(this, buffer, rows, columns, format);
        tensors.Add(tensor);
        return tensor;
    }

    public void Upload(GpuTensor tensor, ReadOnlySpan<float> values)
    {
        Validate(tensor, tensor.Format);
        ComputeMathBatch.Require(tensor.Format != GpuTensorFormat.UInt32, "Unsigned tensors require UploadUInt32, without float reinterpretation.");
        int count = tensor.Format == GpuTensorFormat.Complex32 ? tensor.Elements * 2 : tensor.Elements;
        ComputeMathBatch.Require(values.Length == count, "Upload length must match the tensor representation.");
        foreach (float value in values)
        {
            ComputeMathBatch.Require(float.IsFinite(value)
                && (tensor.Format != GpuTensorFormat.PackedFloat16 || MathF.Abs(value) <= 65504),
                "Uploaded values must be finite and representable in the tensor format.");
        }
        byte[] bytes;
        if (tensor.Format == GpuTensorFormat.PackedFloat16)
        {
            var packed = new uint[(values.Length + 1) / 2];
            for (int index = 0; index < values.Length; index++)
            {
                packed[index / 2] |= (uint)BitConverter.HalfToUInt16Bits((Half)values[index]) << (index % 2 * 16);
            }
            bytes = MemoryMarshal.AsBytes(packed.AsSpan()).ToArray();
        }
        else
        {
            bytes = MemoryMarshal.AsBytes(values).ToArray();
        }
        queuedScope?.Flush();
        var result = uploader.Upload(new(tensor.Buffer, bytes, DebugName: "compute-math.upload"));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    }

    public ComputeMathBatch CreateBatch()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(this);
    }

    public void UploadUInt32(GpuTensor tensor, ReadOnlySpan<uint> values)
    {
        Validate(tensor, GpuTensorFormat.UInt32);
        ComputeMathBatch.Require(values.Length == tensor.Elements, "Unsigned upload length must match the tensor shape.");
        queuedScope?.Flush();
        var result = uploader.Upload(new(tensor.Buffer, MemoryMarshal.AsBytes(values).ToArray(), DebugName: "compute-math.upload-u32"));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    }

    /// <summary>Uploads an already packed UInt32 byte representation without a
    /// second managed copy. The synchronous uploader has consumed the memory on return.</summary>
    public void UploadUInt32Bytes(GpuTensor tensor, ReadOnlyMemory<byte> bytes)
    {
        Validate(tensor, GpuTensorFormat.UInt32);
        ComputeMathBatch.Require(bytes.Length == (long)tensor.Elements * sizeof(uint),
            "Packed unsigned upload length must match the tensor shape.");
        queuedScope?.Flush();
        var staging = UnsignedUploadBuffer(bytes.Length);
        var result = uploader.Upload(new(tensor.Buffer, bytes, DebugName: "compute-math.upload-u32"), staging);
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    }

    private AurelianVulkanBuffer UnsignedUploadBuffer(int byteCount)
    {
        if (unsignedUploadStaging is null || unsignedUploadStaging.SizeBytes < (ulong)byteCount)
        {
            unsignedUploadStaging?.Dispose();
            unsignedUploadStaging = null;
            unsignedUploadStaging = Buffer((ulong)byteCount,
                VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
        }
        return unsignedUploadStaging;
    }

    public ComputeMathResult Execute(ComputeMathBatch batch)
    {
        return ExecuteCore(batch, null, default);
    }

    /// <summary>Copies one packed tensor and executes its consumers in the same
    /// submission. The supplied host memory is consumed before this method returns.</summary>
    public ComputeMathResult ExecuteWithUInt32Upload(ComputeMathBatch batch,
        GpuTensor destination, ReadOnlyMemory<byte> bytes)
    {
        Validate(destination, GpuTensorFormat.UInt32);
        ComputeMathBatch.Require(bytes.Length == (long)destination.Elements * sizeof(uint),
            "Packed upload length must match the tensor shape.");
        return ExecuteCore(batch, destination, bytes);
    }

    private ComputeMathResult ExecuteCore(ComputeMathBatch batch, GpuTensor? upload, ReadOnlyMemory<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ComputeMathBatch.Require(ReferenceEquals(batch.Owner, this) && batch.DispatchCount > 0,
            "Execute requires a nonempty batch from this compute owner.");
        if (queuedScope is not null)
        {
            return queuedScope.Submit(batch, upload, bytes);
        }
        foreach (var dispatch in batch.Dispatches)
        {
            Validate(dispatch.Input, dispatch.Input.Format);
            Validate(dispatch.Auxiliary, dispatch.Auxiliary.Format);
            Validate(dispatch.Output, dispatch.Output.Format);
            EnsurePipeline(dispatch.Shader);
        }
        while (slots.Count < batch.DispatchCount)
        {
            slots.Add(CreateSlot());
        }
        for (int index = 0; index < batch.DispatchCount; index++)
        {
            Configure(slots[index], batch.Dispatches[index]);
        }
        if (upload is not null)
        {
            Require(UnsignedUploadBuffer(bytes.Length).Write(bytes.Span).Success, "Packed upload staging write failed.");
        }
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Compute batch begin failed.");
        timings.Reset(command);
        if (upload is not null)
        {
            Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
                PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
            var copy = new BufferCopy(0, 0, (ulong)bytes.Length);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, unsignedUploadStaging!.NativeBuffer,
                upload.Buffer.NativeBuffer, 1, &copy);
        }
        Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
            PipelineStageFlags.ComputeShaderBit);
        timings.Mark(command, 0);
        for (int index = 0; index < batch.DispatchCount; index++)
        {
            var dispatch = batch.Dispatches[index];
            var set = slots[index].Set;
            plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipelines[dispatch.Shader]);
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
            plant.Vk.CmdDispatch(command.CommandBuffer, dispatch.GroupsX, dispatch.GroupsY, 1);
            Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }
        timings.Mark(command, 1);
        Submit(command, "compute-math.batch");
        return new(batch.DispatchCount, timings.Read().FirstOrDefault()?.Milliseconds);
    }

    public float[] Read(GpuTensor tensor)
    {
        Validate(tensor, tensor.Format);
        ComputeMathBatch.Require(tensor.Format is GpuTensorFormat.Float32 or GpuTensorFormat.Complex32,
            "Read returns FP32 or interleaved complex32 values, not packed half or unsigned words.");
        queuedScope?.Flush();
        using var staging = Buffer(tensor.Buffer.SizeBytes, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu);
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Compute readback begin failed.");
        Barrier(command, AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit, AccessFlags.TransferReadBit,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit);
        var copy = new BufferCopy(0, 0, tensor.Buffer.SizeBytes);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, tensor.Buffer.NativeBuffer, staging.NativeBuffer, 1, &copy);
        Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        Submit(command, "compute-math.readback");
        ReadbackCount++;
        return MemoryMarshal.Cast<byte, float>(staging.ReadBytes((int)tensor.Buffer.SizeBytes)).ToArray();
    }

    internal void Validate(GpuTensor tensor, GpuTensorFormat format)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ComputeMathBatch.Require(ReferenceEquals(tensor.Owner, this) && !tensor.Disposed && tensor.Format == format,
            "Tensors must be live, have the declared format and belong to this compute owner.");
    }

    internal void Release(GpuTensor tensor)
    {
        if (queuedScope is not null)
        {
            queuedScope.Retire(tensor.Buffer);
        }
        else
        {
            tensor.Buffer.Dispose();
        }
        if (!disposed)
        {
            tensors.Remove(tensor);
        }
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory)
    {
        var created = VulkanBufferFactory.Create(plant, allocator,
            new(plant.Context.Id, bytes, usage, memory, "compute-math", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        Require(created.Success, string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        return created.Buffer!;
    }

    private void Barrier(VulkanCommandBufferLease command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private void Submit(VulkanCommandBufferLease command, string name)
    {
        Require(command.End().Success, "Compute command end failed.");
        var submitted = submitter.Submit(new(command, DebugName: name));
        Require(submitted.Success, string.Join("; ", submitted.Diagnostics.Select(item => item.Message)));
    }

    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException("AUR-COMPUTE-MATH-002: " + message);
        }
    }

    private static void Check(Result result) => Require(result == Result.Success, "Vulkan operation failed: " + result);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        queuedScope?.Dispose();
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        foreach (var prepared in preparedBatches.ToArray())
        {
            prepared.Dispose();
        }
        timings.Dispose();
        foreach (var pipeline in pipelines.Values)
        {
            plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        }
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        if (pipelineLayout.Handle != 0)
        {
            plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        }
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        foreach (var slot in slots)
        {
            slot.Parameters.Dispose();
        }
        foreach (var tensor in tensors)
        {
            tensor.Dispose();
        }
        uploader.Dispose();
        unsignedUploadStaging?.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
