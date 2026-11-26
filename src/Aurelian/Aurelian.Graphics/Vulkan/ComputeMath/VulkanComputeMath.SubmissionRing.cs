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
            public DispatchTimestamps? DispatchQueries { get; set; }
            public QueuedDispatchProfile? PendingProfile { get; set; }
            public required Vulkan3DGpuTimings Timings { get; init; }
            public List<DispatchSlot> Descriptors { get; } = [];
            public ulong FenceValue { get; set; }
            public long Sequence { get; set; }
            public double PreparationMilliseconds { get; set; }
            public ulong UploadBytes { get; set; }
            public double CpuRecordingMilliseconds { get; set; }
            public double CpuSubmissionMilliseconds { get; set; }

            public void Dispose()
            {
                foreach (var descriptor in Descriptors)
                {
                    descriptor.Parameters?.Dispose();
                }
                DispatchQueries?.Dispose();
                Timings.Dispose();
                Staging?.Dispose();
                Weights?.Dispose();
            }
        }

        private readonly VulkanComputeMath owner;
        private readonly IComputeStreamController controller;
        private readonly DescriptorPool pool;
        private readonly List<Slot> slots = [];
        private ulong? previousEndTicks;
        private long previousSequence;
        private int outstanding;
        private int next;
        private double milliseconds;
        private double transferMilliseconds;
        private bool timed = true;
        private bool disposed;
        public double CpuRecordingMilliseconds { get; private set; }
        public double CpuSubmissionMilliseconds { get; private set; }
        public double CpuCompletionWaitMilliseconds { get; private set; }
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
            var size = new DescriptorPoolSize(DescriptorType.StorageBuffer,
                (uint)(Depth * owner.MaximumDispatches) * owner.DescriptorBindingCount);
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
            int admissionLimit = controller.AdmissionLimit(Depth);
            Require(admissionLimit >= 1 && admissionLimit <= Depth,
                "Stream admission must remain within physical ring capacity.");
            while (outstanding >= admissionLimit)
            {
                int oldest = Enumerable.Range(0, Depth)
                    .Where(index => slots[index].FenceValue != 0)
                    .MinBy(index => slots[index].Sequence);
                Harvest(oldest);
            }
            Harvest(next);
            return slots[next];
        }

        public void Submit(ComputeMathBatch batch, double preparationMilliseconds = 0,
            GpuTensor? upload = null, ReadOnlyMemory<byte> bytes = default, QueuedDispatchProfile? profile = null,
            ComputeBatchUpload? plan = null, IReadOnlyList<Commanding.Submit.VulkanTimelineWait>? waits = null)
        {
            ComputeMathBatch.Require(ReferenceEquals(batch.Owner, owner) && batch.DispatchCount is > 0
                && batch.DispatchCount <= owner.MaximumDispatches, "Stream needs a nonempty bounded batch from its owner.");
            var slot = Acquire();
            long recordingStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (upload is not null)
            {
                owner.Validate(upload, GpuTensorFormat.UInt32);
                ComputeMathBatch.Require(bytes.Length == (long)upload.Elements * sizeof(uint),
                    "Stream upload must match the complete packed tensor.");
            }
            if (plan is not null)
            {
                foreach (var destination in plan.Destinations)
                {
                    owner.Validate(destination, destination.Format);
                }
            }
            if (upload is not null || plan is not null)
            {
                if (slot.Staging is null || slot.Staging.SizeBytes < (ulong)bytes.Length)
                {
                    slot.Staging?.Dispose();
                    slot.Staging = null;
                    slot.Staging = owner.Buffer((ulong)bytes.Length,
                        VulkanBufferUsage.TransferSource, VulkanMemoryUsage.CpuToGpu);
                }
                long stagingStart = System.Diagnostics.Stopwatch.GetTimestamp();
                Require(slot.Staging.Write(bytes.Span).Success, "Stream packed staging write failed.");
                if (profile is not null)
                {
                    profile.StagingMs = System.Diagnostics.Stopwatch.GetElapsedTime(stagingStart).TotalMilliseconds;
                }
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
            DispatchTimestamps? queries = null;
            if (profile is { Count: > 0 })
            {
                queries = slot.DispatchQueries ??= new(owner.plant, owner.MaximumDispatches + 1);
            }
            var command = owner.commands.Rent(owner.fences.CommandListFence.LastKnownCompletedValue);
            Require(command.Begin().Success, "Stream command begin failed.");
            queries?.Begin(command, batch.DispatchCount);
            slot.Timings.Reset(command);
            slot.Timings.BeginFrame(command, Submissions + 1);
            GpuTensor? copyDestination = upload ?? slot.Weights;
            slot.UploadBytes = 0;
            if (copyDestination is not null)
            {
                slot.UploadBytes = upload is not null ? (ulong)bytes.Length : slot.Staging!.SizeBytes;
            }
            if (plan is not null)
            {
                slot.UploadBytes = (ulong)bytes.Length;
            }
            if ((copyDestination is not null || plan is not null) && slot.Staging is { } staging)
            {
                owner.Barrier(command, AccessFlags.HostWriteBit | AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
                    PipelineStageFlags.HostBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
                slot.Timings.Mark(command, 2, PipelineStageFlags.TransferBit);
                if (plan is not null)
                {
                    owner.RecordCopies(command, staging, plan);
                }
                else
                {
                    ulong size = upload is null ? staging.SizeBytes : (ulong)bytes.Length;
                    var copy = new BufferCopy(0, 0, size);
                    owner.plant.Vk.CmdCopyBuffer(command.CommandBuffer, staging.NativeBuffer, copyDestination!.Buffer.NativeBuffer, 1, &copy);
                }
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
            queries?.Mark(command, 0);
            for (int index = 0; index < batch.DispatchCount; index++)
            {
                var dispatch = batch.Dispatches[index];
                owner.RecordDispatch(command.CommandBuffer, slot.Descriptors[index], dispatch);
                owner.Barrier(command, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
                queries?.Mark(command, index + 1);
            }
            slot.Timings.Mark(command, 1, PipelineStageFlags.ComputeShaderBit);
            slot.Timings.EndFrame(command);
            Require(command.End().Success, "Stream command end failed.");
            slot.CpuRecordingMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(recordingStart).TotalMilliseconds;
            long submissionStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var submission = owner.submitter.Submit(new(command, WaitForCompletion: false, DebugName: "compute-math.stream") { TimelineWaits = waits ?? [] });
            slot.CpuSubmissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(submissionStart).TotalMilliseconds;
            // A submitted fence survives a possible command-retirement failure.
            slot.FenceValue = submission.SignalFenceValue ?? 0;
            LastSubmittedFenceValue = Math.Max(LastSubmittedFenceValue, slot.FenceValue);
            Require(submission.Success && slot.FenceValue != 0, "Stream submission failed.");
            slot.PendingProfile = profile;
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
            double waitMilliseconds = 0;
            if (owner.fences.CommandListFence.LastKnownCompletedValue < slot.FenceValue)
            {
                CompletionWaits++;
                long waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                Require(owner.fences.CommandListFence.WaitForValue(slot.FenceValue, 30_000_000_000).Success,
                    "Stream completion wait failed.");
                waitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;
            }
            var timings = slot.Timings.Read();
            var computeInterval = slot.Timings.ReadInterval(0);
            var transferInterval = slot.Timings.ReadInterval(1);
            double? cost = timings.FirstOrDefault()?.Milliseconds;
            double? transferCost = timings.Skip(1).FirstOrDefault()?.Milliseconds;
            if (slot.PendingProfile is { } profile)
            {
                profile.CompletedIntervals = profile.Count > 0 ? slot.DispatchQueries!.Read(profile.Count) : [];
                profile.ComputeInterval = computeInterval;
                profile.TransferInterval = slot.UploadBytes > 0 ? transferInterval : null;
                profile.UploadBytes = slot.UploadBytes;
                profile.CopyMs = slot.UploadBytes == 0 ? 0 : transferCost;
                if (computeInterval is { } current && previousEndTicks is { } end
                    && previousSequence + 1 == slot.Sequence && end <= current.StartTicks)
                {
                    double msPerTick = current.NanosecondsPerTick / 1_000_000d;
                    profile.PreComputeGapMs = (current.StartTicks - end) * msPerTick;
                    if (transferInterval is { } copy)
                    {
                        ulong start = Math.Max(end, copy.StartTicks);
                        ulong stop = Math.Min(current.StartTicks, copy.EndTicks);
                        profile.CopyInGapMs = stop > start && slot.UploadBytes > 0 ? (stop - start) * msPerTick : 0;
                    }
                }
                slot.PendingProfile = null;
            }
            previousEndTicks = computeInterval?.EndTicks;
            previousSequence = slot.Sequence;
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
            CpuRecordingMilliseconds += slot.CpuRecordingMilliseconds;
            CpuSubmissionMilliseconds += slot.CpuSubmissionMilliseconds;
            CpuCompletionWaitMilliseconds += waitMilliseconds;
            slot.FenceValue = 0;
            outstanding--;
            controller.Observe(new(slot.Sequence, index, true, outstanding, slot.PreparationMilliseconds, cost)
            {
                UploadBytes = slot.UploadBytes,
                TransferGpuMilliseconds = transferCost,
                ComputeStartTicks = computeInterval?.StartTicks,
                ComputeEndTicks = computeInterval?.EndTicks,
                TransferStartTicks = transferInterval?.StartTicks,
                TransferEndTicks = transferInterval?.EndTicks,
                NanosecondsPerTick = computeInterval?.NanosecondsPerTick,
                CpuRecordingMilliseconds = slot.CpuRecordingMilliseconds,
                CpuSubmissionMilliseconds = slot.CpuSubmissionMilliseconds,
                CpuCompletionWaitMilliseconds = waitMilliseconds,
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
