using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>One physical ring for both weight streaming and resident batches.
    /// Descriptors, query pairs and staging are immutable until timeline completion.
    /// The queue executes dependent scratch reuse in submission order.</summary>
    private sealed class SubmissionRing : IDisposable
    {
        internal sealed class Slot : IDisposable
        {
            public GpuTensor? Weights { get; set; }
            public AurelianVulkanBuffer? Staging { get; set; }
            public required Vulkan3DGpuTimings Timings { get; init; }
            public List<DispatchSlot> Descriptors { get; } = [];
            public ulong FenceValue { get; set; }
            public long Sequence { get; set; }
            public double PreparationMilliseconds { get; set; }
            public ulong UploadBytes { get; set; }

            public void Dispose()
            {
                foreach (var descriptor in Descriptors)
                {
                    descriptor.Parameters.Dispose();
                }
                Timings.Dispose();
                Staging?.Dispose();
                Weights?.Dispose();
            }
        }

        private readonly VulkanComputeMath owner;
        private readonly IComputeStreamController controller;
        private readonly DescriptorPool pool;
        private readonly List<Slot> slots = [];
        private int outstanding;
        private int next;
        private double milliseconds;
        private double transferMilliseconds;
        private bool timed = true;
        private bool disposed;
        public int Depth { get; }
        public int MaximumOutstanding { get; private set; }
        public int MaximumInFlight { get; private set; }
        public int CompletionWaits { get; private set; }
        public int Submissions { get; private set; }
        public int Dispatches { get; private set; }
        public ulong LastSubmittedFenceValue { get; private set; }
        public long UploadedBytes { get; private set; }
        public double PreparationMilliseconds { get; private set; }
        public double? GpuMilliseconds => timed ? milliseconds : null;
        public double? TransferGpuMilliseconds => timed ? transferMilliseconds : null;

        public SubmissionRing(VulkanComputeMath owner, IComputeStreamController controller,
            ComputeStreamForecast forecast, StreamingLinearPlan? weights = null)
        {
            this.owner = owner;
            this.controller = controller;
            int selected = controller.Plan(forecast);
            ComputeMathBatch.Require(selected is >= 1 and <= 4, "Stream controller depth must be 1–4.");
            Depth = Math.Min(selected, forecast.BatchCount);
            var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, (uint)(Depth * owner.MaximumDispatches * 4));
            var info = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = (uint)(Depth * owner.MaximumDispatches),
                PoolSizeCount = 1,
                PPoolSizes = &size,
            };
            Check(owner.plant.Vk.CreateDescriptorPool(owner.plant.Device, &info, null, out pool));
            try
            {
                for (int index = 0; index < Depth; index++)
                {
                    var slot = new Slot { Timings = new(owner.plant, ["compute-stream", "compute-transfer"]) };
                    slots.Add(slot);
                    if (weights is not null)
                    {
                        slot.Weights = owner.CreateTensor(weights.TileRows, weights.InputWidth + 1);
                        slot.Staging = owner.Buffer(weights.WeightBufferBytes,
                            VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Slot Acquire()
        {
            Harvest(next);
            return slots[next];
        }

        public void Submit(ComputeMathBatch batch, double preparationMilliseconds = 0,
            GpuTensor? upload = null, ReadOnlyMemory<byte> bytes = default)
        {
            ComputeMathBatch.Require(ReferenceEquals(batch.Owner, owner) && batch.DispatchCount is > 0
                && batch.DispatchCount <= owner.MaximumDispatches, "Stream needs a nonempty bounded batch from its owner.");
            var slot = Acquire();
            if (upload is not null)
            {
                owner.Validate(upload, GpuTensorFormat.UInt32);
                ComputeMathBatch.Require(bytes.Length == (long)upload.Elements * sizeof(uint),
                    "Stream upload must match the complete packed tensor.");
                if (slot.Staging is null || slot.Staging.SizeBytes < (ulong)bytes.Length)
                {
                    slot.Staging?.Dispose();
                    slot.Staging = null;
                    slot.Staging = owner.Buffer((ulong)bytes.Length,
                        VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
                }
                Require(slot.Staging.Write(bytes.Span).Success, "Stream packed staging write failed.");
            }
            foreach (var dispatch in batch.Dispatches)
            {
                owner.Validate(dispatch.Input, dispatch.Input.Format);
                owner.Validate(dispatch.Auxiliary, dispatch.Auxiliary.Format);
                owner.Validate(dispatch.Output, dispatch.Output.Format);
                owner.EnsurePipeline(dispatch.Shader);
            }
            while (slot.Descriptors.Count < batch.DispatchCount)
            {
                slot.Descriptors.Add(owner.CreateSlot(pool));
            }
            for (int index = 0; index < batch.DispatchCount; index++)
            {
                owner.Configure(slot.Descriptors[index], batch.Dispatches[index]);
            }
            var command = owner.commands.Rent(owner.fences.CommandListFence.LastKnownCompletedValue);
            Require(command.Begin().Success, "Stream command begin failed.");
            slot.Timings.Reset(command);
            GpuTensor? copyDestination = upload ?? slot.Weights;
            slot.UploadBytes = 0;
            if (copyDestination is not null)
            {
                slot.UploadBytes = upload is not null ? (ulong)bytes.Length : slot.Staging!.SizeBytes;
            }
            if (copyDestination is not null && slot.Staging is { } staging)
            {
                owner.Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
                    PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
                slot.Timings.Mark(command, 2, PipelineStageFlags.TransferBit);
                ulong copyBytes = upload is not null ? (ulong)bytes.Length : staging.SizeBytes;
                var copy = new BufferCopy(0, 0, copyBytes);
                owner.plant.Vk.CmdCopyBuffer(command.CommandBuffer, staging.NativeBuffer, copyDestination.Buffer.NativeBuffer, 1, &copy);
            }
            else
            {
                slot.Timings.Mark(command, 2, PipelineStageFlags.TransferBit);
            }
            slot.Timings.Mark(command, 3, PipelineStageFlags.TransferBit);
            owner.Barrier(command, AccessFlags.HostWriteBit | AccessFlags.TransferWriteBit | AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.HostBit | PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit);
            slot.Timings.Mark(command, 0, PipelineStageFlags.ComputeShaderBit);
            for (int index = 0; index < batch.DispatchCount; index++)
            {
                var dispatch = batch.Dispatches[index];
                var set = slot.Descriptors[index].Set;
                owner.plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, owner.pipelines[dispatch.Shader]);
                owner.plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute, owner.pipelineLayout, 0, 1, &set, 0, null);
                owner.plant.Vk.CmdDispatch(command.CommandBuffer, dispatch.GroupsX, dispatch.GroupsY, 1);
                owner.Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            }
            slot.Timings.Mark(command, 1, PipelineStageFlags.ComputeShaderBit);
            Require(command.End().Success, "Stream command end failed.");
            var submission = owner.submitter.Submit(new(command, WaitForCompletion: false, DebugName: "compute-math.stream"));
            // A submitted fence survives a possible command-retirement failure.
            slot.FenceValue = submission.SignalFenceValue ?? 0;
            LastSubmittedFenceValue = Math.Max(LastSubmittedFenceValue, slot.FenceValue);
            Require(submission.Success && slot.FenceValue != 0, "Stream submission failed.");
            slot.Sequence = ++Submissions;
            slot.PreparationMilliseconds = preparationMilliseconds;
            Dispatches += batch.DispatchCount;
            UploadedBytes += bytes.Length;
            PreparationMilliseconds += preparationMilliseconds;
            outstanding++;
            MaximumOutstanding = Math.Max(MaximumOutstanding, outstanding);
            Require(owner.fences.CommandListFence.QueryCompletedValue().Success, "Stream timeline query failed.");
            int inFlight = slots.Count(item => item.FenceValue > owner.fences.CommandListFence.LastKnownCompletedValue);
            MaximumInFlight = Math.Max(MaximumInFlight, inFlight);
            controller.Observe(new(slot.Sequence, next, false, outstanding, preparationMilliseconds, null)
            {
                UploadBytes = slot.UploadBytes,
            });
            next = (next + 1) % Depth;
        }

        public void Drain()
        {
            foreach (int index in Enumerable.Range(0, Depth).OrderBy(index => slots[index].Sequence))
            {
                Harvest(index);
            }
        }

        private void Harvest(int index)
        {
            var slot = slots[index];
            if (slot.FenceValue == 0)
            {
                return;
            }
            Require(owner.fences.CommandListFence.QueryCompletedValue().Success, "Stream timeline query failed.");
            if (owner.fences.CommandListFence.LastKnownCompletedValue < slot.FenceValue)
            {
                CompletionWaits++;
                Require(owner.fences.CommandListFence.WaitForValue(slot.FenceValue, 30_000_000_000).Success,
                    "Stream completion wait failed.");
            }
            var timings = slot.Timings.Read();
            double? cost = timings.FirstOrDefault()?.Milliseconds;
            double? transferCost = timings.Skip(1).FirstOrDefault()?.Milliseconds;
            if (transferCost is { } transfer)
            {
                transferMilliseconds += transfer;
            }
            else
            {
                timed = false;
            }
            if (cost is { } value)
            {
                milliseconds += value;
            }
            else
            {
                timed = false;
            }
            slot.FenceValue = 0;
            outstanding--;
            controller.Observe(new(slot.Sequence, index, true, outstanding, slot.PreparationMilliseconds, cost)
            {
                UploadBytes = slot.UploadBytes,
                TransferGpuMilliseconds = transferCost,
            });
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            // Only exceptional exits require an idle wait. Normal callers drain.
            if (slots.Any(slot => slot.FenceValue != 0))
            {
                Check(owner.plant.Vk.DeviceWaitIdle(owner.plant.Device));
            }
            foreach (var slot in slots)
            {
                slot.Dispose();
            }
            owner.plant.Vk.DestroyDescriptorPool(owner.plant.Device, pool, null);
        }
    }
}
