using System.Diagnostics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Native3D;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    private readonly HashSet<PreparedComputeBatch> preparedBatches = [];

    /// <summary>Snapshots a resident batch and records it once. The caller retains
    /// all tensors until disposal. Contents may change between synchronous replays;
    /// shape, descriptors and dispatch parameters remain fixed. Uses the existing
    /// command pool, submitter and timeline fence. Cannot overlap a queued scope.</summary>
    public PreparedComputeBatch Prepare(ComputeMathBatch batch)
        => Prepare(batch, enableGpuTimings: true);

    /// <summary>GPU timestamp instrumentation can be omitted for latency-sensitive replay.</summary>
    public PreparedComputeBatch Prepare(ComputeMathBatch batch, bool enableGpuTimings)
        => Prepare(batch, new PreparedComputeOptions(EnableGpuTimings: enableGpuTimings));

    public PreparedComputeBatch Prepare(ComputeMathBatch batch, PreparedComputeOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);
        ComputeMathBatch.Require(ReferenceEquals(batch.Owner, this) && batch.DispatchCount > 0
            && queuedScope is null, "Prepared batches need nonempty work from this owner outside a queued scope.");
        var prepared = new PreparedComputeBatch(this, batch.Dispatches.ToArray(), options);
        preparedBatches.Add(prepared);
        return prepared;
    }

    public sealed class PreparedComputeBatch : IDisposable
    {
        private readonly VulkanComputeMath owner;
        private readonly List<DispatchSlot> slots = [];
        private readonly GpuTensor[] tensors;
        private VulkanCommandBufferLease? command;
        private Vulkan3DGpuTimings? timings;
        private DescriptorPool descriptorPool;
        private bool disposed;
        private bool failed;
        public int DispatchCount { get; }
        public bool HasGpuTimings => timings is not null;
        public bool HasDeviceLocalParameters { get; }

        internal PreparedComputeBatch(VulkanComputeMath owner, ComputeMathDispatch[] dispatches, PreparedComputeOptions options)
        {
            this.owner = owner;
            if (options.ElideIndependentBarriers)
            {
                for (int first = 0; first < dispatches.Length; first++)
                {
                    for (int second = first + 1; second < dispatches.Length; second++)
                    {
                        var a = dispatches[first];
                        var b = dispatches[second];
                        ComputeMathBatch.Require(!ReferenceEquals(a.Output.Buffer, b.Input.Buffer)
                            && !ReferenceEquals(a.Output.Buffer, b.Auxiliary.Buffer)
                            && !ReferenceEquals(a.Output.Buffer, b.Output.Buffer)
                            && !ReferenceEquals(b.Output.Buffer, a.Input.Buffer)
                            && !ReferenceEquals(b.Output.Buffer, a.Auxiliary.Buffer),
                            "Barrier elision requires independent dispatch allocations; read/write and write/write hazards are rejected.");
                    }
                }
            }
            DispatchCount = dispatches.Length;
            HasDeviceLocalParameters = options.DeviceLocalParameters;
            tensors = dispatches.SelectMany(dispatch => new[]
                { dispatch.Input, dispatch.Auxiliary, dispatch.Output }).Distinct().ToArray();
            try
            {
                foreach (var tensor in tensors)
                {
                    owner.Validate(tensor, tensor.Format);
                }
                foreach (var dispatch in dispatches)
                {
                    owner.EnsurePipeline(dispatch.Shader);
                }
                var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, (uint)DispatchCount * 4);
                var poolInfo = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    MaxSets = (uint)DispatchCount, PoolSizeCount = 1, PPoolSizes = &size,
                };
                Check(owner.plant.Vk.CreateDescriptorPool(owner.plant.Device, &poolInfo, null, out descriptorPool));
                foreach (var dispatch in dispatches)
                {
                    var slot = owner.CreateSlot(descriptorPool, options.DeviceLocalParameters);
                    slots.Add(slot);
                    owner.Configure(slot, dispatch);
                }
                if (options.EnableGpuTimings)
                {
                    timings = new(owner.plant, ["prepared-compute"]);
                }
                command = owner.commands.Rent(owner.fences.CommandListFence.LastKnownCompletedValue);
                Require(command.Begin(reusable: true).Success, "Prepared compute recording failed.");
                timings?.Reset(command);
                owner.Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
                    PipelineStageFlags.ComputeShaderBit);
                timings?.Mark(command, 0);
                for (int index = 0; index < dispatches.Length; index++)
                {
                    var dispatch = dispatches[index];
                    var set = slots[index].Set;
                    owner.plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, owner.pipelines[dispatch.Shader]);
                    owner.plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute,
                        owner.pipelineLayout, 0, 1, &set, 0, null);
                    owner.plant.Vk.CmdDispatch(command.CommandBuffer, dispatch.GroupsX, dispatch.GroupsY, 1);
                    if (!options.ElideIndependentBarriers || index == dispatches.Length - 1)
                    {
                        owner.Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
                    }
                }
                timings?.Mark(command, 1);
                Require(command.End().Success, "Prepared compute command end failed.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Replays synchronously. An explicit polling budget can trade
        /// CPU work for lower wait latency; zero uses a blocking wait.</summary>
        public ComputeMathResult Execute(uint spinWaitNanoseconds = 0)
            => ExecuteCore(spinWaitNanoseconds, measureCpuTime: false, out _);

        /// <summary>Measures CPU validation, queue submission, completion and query retrieval.
        /// Instrumentation overhead is included; use Execute for latency measurements.</summary>
        public PreparedComputeProfile ExecuteProfiled(uint spinWaitNanoseconds = 0)
        {
            ExecuteCore(spinWaitNanoseconds, measureCpuTime: true, out var profile);
            return profile!;
        }

        private ComputeMathResult ExecuteCore(uint spinWaitNanoseconds, bool measureCpuTime,
            out PreparedComputeProfile? profile)
        {
            long started = measureCpuTime ? Stopwatch.GetTimestamp() : 0;
            ObjectDisposedException.ThrowIf(disposed, this);
            ObjectDisposedException.ThrowIf(owner.disposed, owner);
            ComputeMathBatch.Require(!failed && owner.queuedScope is null,
                "A failed prepared batch cannot replay; prepared replay cannot overlap a queued scope.");
            foreach (var tensor in tensors)
            {
                owner.Validate(tensor, tensor.Format);
                ComputeMathBatch.Require(!tensor.Buffer.IsDisposed, "Prepared tensor buffers must remain live until replay completes.");
            }
            long validated = measureCpuTime ? Stopwatch.GetTimestamp() : 0;
            var submitted = owner.submitter.Submit(new(command!, DebugName: "compute-math.prepared")
            {
                RetainForReplay = true,
                SpinWaitNanoseconds = spinWaitNanoseconds,
                MeasureCpuTime = measureCpuTime,
            });
            if (!submitted.Success)
            {
                failed = true;
                // A failed wait may leave work in flight. Establish completion before
                // descriptors, parameter storage or queries can be disposed.
                _ = owner.plant.Vk.DeviceWaitIdle(owner.plant.Device);
                Require(false, string.Join("; ", submitted.Diagnostics.Select(item => item.Message)));
            }
            long completed = measureCpuTime ? Stopwatch.GetTimestamp() : 0;
            var result = new ComputeMathResult(DispatchCount, timings?.Read().SingleOrDefault()?.Milliseconds);
            profile = measureCpuTime ? new(result, submitted.CpuTimings,
                Stopwatch.GetElapsedTime(started, validated).TotalMicroseconds,
                Stopwatch.GetElapsedTime(completed).TotalMicroseconds) : null;
            return result;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (command is not null && !command.IsRetired)
            {
                owner.commands.Retire(command, owner.fences.CommandListFence.LastKnownCompletedValue);
            }
            timings?.Dispose();
            foreach (var slot in slots)
            {
                slot.Parameters.Dispose();
            }
            if (descriptorPool.Handle != 0)
            {
                owner.plant.Vk.DestroyDescriptorPool(owner.plant.Device, descriptorPool, null);
            }
            owner.preparedBatches.Remove(this);
        }
    }
}

public sealed record PreparedComputeProfile(
    ComputeMathResult Result,
    VulkanSubmitCpuTimings? Submission,
    double ValidationMicroseconds,
    double TimestampReadMicroseconds);

/// <summary>Prepared parameters are immutable and uploaded once. Device-local storage
/// avoids shader reads from host memory on discrete GPUs. Host-visible storage remains
/// available as a diagnostic control; transfers happen only during preparation.
/// Independent barrier elision rejects inter-dispatch allocation hazards and
/// retains the initial and final dependencies, including across replays.</summary>
public sealed record PreparedComputeOptions(bool EnableGpuTimings = true, bool DeviceLocalParameters = true,
    bool ElideIndependentBarriers = false);
