using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>Prepare a token graph with a retained upload, mutable position parameters and a readback.</summary>
    public PreparedComputeBatch PrepareTokenReplay(ComputeMathBatch batch, ComputeBatchUpload upload, GpuTensor output,
        bool enableGpuTimings = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(upload);
        Require(ReferenceEquals(batch.Owner, this) && batch.TokenReplayCapacity > 0 && batch.DispatchCount > 0
            && queuedScope is null && !PushDispatchParameters,
            "Token replay requires a token batch, storage parameters and no queued scope.");
        Validate(output, GpuTensorFormat.Float32);
        foreach (var destination in upload.Destinations)
        {
            Validate(destination, destination.Format);
        }
        var prepared = new PreparedComputeBatch(this, batch.Dispatches.ToArray(), new(enableGpuTimings),
            upload, output, batch.TokenReplayCapacity);
        preparedBatches.Add(prepared);
        return prepared;
    }

    private sealed class TokenReplayResources : IDisposable
    {
        private readonly VulkanComputeMath owner;
        private readonly ComputeBatchUpload upload;
        private readonly GpuTensor output;
        private readonly byte[] packets;
        private readonly (int Index, ComputeMathDispatch Dispatch, TokenReplayUpdate Update)[] updates;
        private readonly int stride;
        private readonly int capacity;
        private readonly AurelianVulkanBuffer staging;
        private readonly AurelianVulkanBuffer readback;

        public AurelianVulkanBuffer Parameters { get; }

        public TokenReplayResources(VulkanComputeMath owner, ComputeMathDispatch[] dispatches,
            ComputeBatchUpload upload, GpuTensor output, int capacity)
        {
            this.owner = owner;
            this.upload = upload;
            this.output = output;
            this.capacity = capacity;
            owner.plant.Vk.GetPhysicalDeviceProperties(owner.plant.PhysicalDevice, out var device);
            int alignment = checked((int)Math.Max(4ul, device.Limits.MinStorageBufferOffsetAlignment));
            int packetBytes = sizeof(KernelParameters) + sizeof(DispatchIndirectCommand);
            stride = checked((packetBytes + alignment - 1) / alignment * alignment);
            packets = new byte[checked(stride * dispatches.Length)];
            var mutable = new List<(int, ComputeMathDispatch, TokenReplayUpdate)>();
            for (int index = 0; index < dispatches.Length; index++)
            {
                var dispatch = dispatches[index];
                WritePacket(index, dispatch.Parameters, dispatch.GroupsX, dispatch.GroupsY);
                if (dispatch.TokenUpdate is { } update) { mutable.Add((index, dispatch, update)); }
            }
            updates = mutable.ToArray();

            Parameters = owner.Buffer((ulong)packets.Length, VulkanBufferUsage.Storage | VulkanBufferUsage.Indirect
                | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly);
            try
            {
                staging = owner.Buffer((ulong)upload.Payload.Length + (ulong)packets.Length,
                    VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
                try
                {
                    readback = owner.Buffer(output.Buffer.SizeBytes, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu);
                }
                catch
                {
                    staging.Dispose();
                    throw;
                }
            }
            catch
            {
                Parameters.Dispose();
                throw;
            }
        }

        public ulong ParameterOffset(int index) => (ulong)index * (ulong)stride;
        public ulong IndirectOffset(int index) => ParameterOffset(index) + (ulong)sizeof(KernelParameters);

        private void WritePacket(int index, KernelParameters parameters, uint groupsX, uint groupsY)
        {
            Span<byte> packet = packets.AsSpan(index * stride);
            MemoryMarshal.Write(packet, in parameters);
            var indirect = new DispatchIndirectCommand(groupsX, groupsY, 1);
            MemoryMarshal.Write(packet[sizeof(KernelParameters)..], in indirect);
        }

        public void Stage(int position, int readbackElements)
        {
            Require(position >= 0 && position < capacity && readbackElements == output.Elements,
                "Replay position and readback must match the prepared graph.");
            foreach (var destination in upload.Destinations)
            {
                owner.Validate(destination, destination.Format);
            }
            owner.Validate(output, GpuTensorFormat.Float32);
            foreach (var (index, dispatch, update) in updates)
            {
                var parameters = dispatch.Parameters;
                uint groupsY = dispatch.GroupsY;
                update.Apply(position, ref parameters, ref groupsY);
                WritePacket(index, parameters, dispatch.GroupsX, groupsY);
            }
            Require(staging.Write(upload.Payload.Span).Success
                && staging.Write(packets, (ulong)upload.Payload.Length).Success, "Token replay staging write failed.");
        }

        public void RecordUpload(VulkanCommandBufferLease command)
        {
            owner.Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit
                | AccessFlags.IndirectCommandReadBit,
                AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
                PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.DrawIndirectBit,
                PipelineStageFlags.TransferBit);
            owner.RecordCopies(command, staging, upload);
            var copy = new BufferCopy((ulong)upload.Payload.Length, 0, (ulong)packets.Length);
            owner.plant.Vk.CmdCopyBuffer(command.CommandBuffer, staging.NativeBuffer, Parameters.NativeBuffer, 1, &copy);
            owner.Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.IndirectCommandReadBit,
                PipelineStageFlags.TransferBit, PipelineStageFlags.DrawIndirectBit);
        }

        public void RecordReadback(VulkanCommandBufferLease command)
        {
            owner.Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
            var copy = new BufferCopy(0, 0, output.Buffer.SizeBytes);
            owner.plant.Vk.CmdCopyBuffer(command.CommandBuffer, output.Buffer.NativeBuffer, readback.NativeBuffer, 1, &copy);
            owner.Barrier(command, AccessFlags.TransferWriteBit, AccessFlags.HostReadBit,
                PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit);
        }

        public void Read(Span<float> destination) => readback.ReadInto(MemoryMarshal.AsBytes(destination));

        public void Dispose()
        {
            readback.Dispose();
            staging.Dispose();
            Parameters.Dispose();
        }
    }
}
