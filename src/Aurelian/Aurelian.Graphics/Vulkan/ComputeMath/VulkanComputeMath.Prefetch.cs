using System.Diagnostics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed class PrefetchTiming
{
    public int Bytes { get; internal init; }
    public double StagingMs { get; internal init; }
    public double SlotWaitMs { get; internal set; }
    public double QueueSubmitMs { get; internal set; }
    public double HostEnqueueMs { get; internal set; }
    public Native3DCompletedGpuFrame? Interval { get; internal set; }
}

public sealed unsafe partial class VulkanComputeMath
{
    private void RecordCopies(VulkanCommandBufferLease command, AurelianVulkanBuffer staging, ComputeBatchUpload plan)
    {
        ulong offset = 0;
        foreach (var destination in plan.Destinations)
        {
            var copy = new BufferCopy(offset, 0, destination.Buffer.SizeBytes);
            plant.Vk.CmdCopyBuffer(command.CommandBuffer, staging.NativeBuffer, destination.Buffer.NativeBuffer, 1, &copy);
            offset += destination.Buffer.SizeBytes;
        }
    }

    private sealed class PrefetchQueue : IDisposable
    {
        private sealed class Slot
        {
            public AurelianVulkanBuffer? Staging;
            public Vulkan3DGpuTimings? Queries;
            public GpuTensor[]? Destinations;
            public ComputeBatchUpload? Writing;
            public PrefetchTiming? Profile;
            public ulong Fence;
            public bool Joined = true;
        }

        private readonly VulkanComputeMath owner;
        private readonly VulkanFenceBundle fences;
        private readonly VulkanCommandBufferPool commands;
        private readonly VulkanCommandSubmitter submitter;
        private readonly Slot[] slots = [new(), new()];
        public long Submissions { get; private set; }
        public long UploadedBytes { get; private set; }

        public PrefetchQueue(VulkanComputeMath owner)
        {
            this.owner = owner;
            var queue = owner.plant.ComputeQueue ?? throw new NotSupportedException("Prefetch requires a second same-family Vulkan queue.");
            fences = VulkanFenceBundle.Create(owner.plant);
            try
            {
                commands = VulkanCommandBufferPool.Create(owner.plant);
                submitter = new(owner.plant, commands, fences, queue);
            }
            catch
            {
                fences.Dispose();
                throw;
            }
        }

        public PrefetchTiming? Stage(ComputeBatchUpload plan, int index, ulong computeFence, bool profile)
        {
            long enqueueStart = profile ? Stopwatch.GetTimestamp() : 0;
            var slot = slots[index];
            ComputeMathBatch.Require(slot.Joined, "Consume a prefetched slot before reusing it.");
            foreach (var tensor in plan.Destinations)
            {
                owner.Validate(tensor, tensor.Format);
            }
            if (slot.Destinations is null)
            {
                var other = slots[1 - index].Destinations;
                ComputeMathBatch.Require(other is null || !plan.Destinations.Intersect(other).Any(),
                    "Prefetch slots require disjoint weight buffers.");
                slot.Destinations = plan.Destinations;
            }
            else
            {
                ComputeMathBatch.Require(slot.Destinations.AsSpan().SequenceEqual(plan.Destinations, EqualityComparer<GpuTensor>.Default),
                    "Reuse a prefetch slot with its original destination buffers.");
            }
            long waitStart = profile ? Stopwatch.GetTimestamp() : 0;
            Harvest(slot);
            double waitMs = profile ? Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds : 0;
            ulong bytes = (ulong)plan.Payload.Length;
            if (slot.Staging is null || slot.Staging.SizeBytes < bytes)
            {
                slot.Staging?.Dispose();
                slot.Staging = null;
                slot.Staging = owner.Buffer(bytes, VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
            }
            long start = Stopwatch.GetTimestamp();
            Require(slot.Staging.Write(plan.Payload.Span).Success, "Prefetch staging write failed.");
            var timing = profile ? new PrefetchTiming { Bytes = plan.Payload.Length,
                SlotWaitMs = waitMs, StagingMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds } : null;
            var queries = profile ? slot.Queries ??= new(owner.plant, ["weight-copy"]) : null;
            var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
            Require(command.Begin().Success, "Prefetch command begin failed.");
            queries?.Reset(command);
            queries?.BeginFrame(command, UploadedBytes);
            queries?.Mark(command, 0, PipelineStageFlags.TransferBit);
            owner.RecordCopies(command, slot.Staging, plan);
            queries?.Mark(command, 1, PipelineStageFlags.TransferBit);
            queries?.EndFrame(command);
            Require(command.End().Success, "Prefetch command end failed.");
            // Queue submission publishes the host writes. The two timeline edges
            // order the old compute reads before overwrite, and new reads after copy.
            long submitStart = profile ? Stopwatch.GetTimestamp() : 0;
            var result = submitter.Submit(new(command, WaitForCompletion: false, DebugName: "compute-math.prefetch")
            {
                TimelineWaits = computeFence == 0 ? [] : [new(owner.fences.CommandListFence, computeFence)],
            });
            if (timing is not null)
            {
                timing.QueueSubmitMs = Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds;
            }
            if (!result.Success)
            {
                Check(owner.plant.Vk.DeviceWaitIdle(owner.plant.Device));
                throw new InvalidOperationException("Prefetch submission failed.");
            }
            slot.Fence = result.SignalFenceValue!.Value;
            slot.Joined = false;
            slot.Writing = plan;
            slot.Profile = timing;
            Submissions++;
            UploadedBytes += plan.Payload.Length;
            if (timing is not null)
            {
                timing.HostEnqueueMs = Stopwatch.GetElapsedTime(enqueueStart).TotalMilliseconds;
            }
            return timing;
        }

        public VulkanTimelineWait[] Join(int index)
        {
            var slot = slots[index];
            ComputeMathBatch.Require(!slot.Joined, "No pending upload in this slot.");
            slot.Joined = true;
            return [new(fences.CommandListFence, slot.Fence)];
        }

        public void FinishWriting(AurelianVulkanBuffer buffer)
        {
            foreach (var slot in slots)
            {
                if (slot.Writing is { } plan)
                {
                    foreach (var tensor in plan.Destinations)
                    {
                        if (ReferenceEquals(tensor.Buffer, buffer))
                        {
                            Harvest(slot);
                            break;
                        }
                    }
                }
            }
        }

        public void Drain()
        {
            foreach (var slot in slots)
            {
                Harvest(slot);
            }
        }

        private void Harvest(Slot slot)
        {
            if (slot.Writing is null)
            {
                return;
            }
            Require(fences.CommandListFence.WaitForValue(slot.Fence, 30_000_000_000).Success,
                "Prefetch copy completion wait failed.");
            if (slot.Profile is { } timing)
            {
                timing.Interval = slot.Queries!.ReadInterval(0);
            }
            slot.Writing = null;
            slot.Profile = null;
        }

        public void Dispose()
        {
            if (slots.Any(slot => slot.Writing is not null))
            {
                Check(owner.plant.Vk.DeviceWaitIdle(owner.plant.Device));
            }
            foreach (var slot in slots)
            {
                slot.Queries?.Dispose();
                slot.Staging?.Dispose();
            }
            submitter.Dispose();
            commands.Dispose();
            fences.Dispose();
        }
    }
}
