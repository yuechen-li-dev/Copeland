using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record ComputeQueueMetrics(int Submissions, int Dispatches, int Depth,
    int CompletionWaits, int MaximumOutstanding, int MaximumInFlight,
    long UploadedBytes, double? GpuMilliseconds, int MaximumRetiredBuffers,
    double? TransferGpuMilliseconds);

public sealed unsafe partial class VulkanComputeMath
{
    private QueuedComputeScope? queuedScope;

    /// <summary>Uses the existing physical submission ring to queue ordered batches.
    /// The forecast bounds submissions and upload bytes per slot. Tensor disposal
    /// retires its allocation at a Vulkan fence; Read, immediate Upload and scope
    /// disposal are completion boundaries. Scopes are sequential and cannot nest.</summary>
    public QueuedComputeScope BeginQueuedScope(IComputeStreamController controller, ComputeStreamForecast forecast)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(forecast);
        ComputeMathBatch.Require(queuedScope is null && forecast.BatchCount is > 0 and <= 65536
            && forecast.DispatchesPerBatch is > 0 && forecast.DispatchesPerBatch <= MaximumDispatches
            && forecast.UploadBytes <= (ulong)MaximumTensorElements * sizeof(uint),
            "Queued scope requires a nonnested, bounded forecast from this compute owner.");
        queuedScope = new(this, controller, forecast);
        return queuedScope;
    }

    public sealed class QueuedComputeScope : IDisposable
    {
        private readonly VulkanComputeMath owner;
        private readonly SubmissionRing ring;
        private readonly ComputeStreamForecast forecast;
        private readonly List<(AurelianVulkanBuffer Buffer, ulong Fence)> retired = [];
        private bool disposed;
        private int maximumRetiredBuffers;

        internal QueuedComputeScope(VulkanComputeMath owner, IComputeStreamController controller,
            ComputeStreamForecast forecast)
        {
            this.owner = owner;
            this.forecast = forecast;
            ring = new(owner, controller, forecast);
        }

        public ComputeQueueMetrics Metrics => new(ring.Submissions, ring.Dispatches, ring.Depth,
            ring.CompletionWaits, ring.MaximumOutstanding, ring.MaximumInFlight,
            ring.UploadedBytes, ring.GpuMilliseconds, maximumRetiredBuffers, ring.TransferGpuMilliseconds);

        internal ComputeMathResult Submit(ComputeMathBatch batch, GpuTensor? upload, ReadOnlyMemory<byte> bytes)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ComputeMathBatch.Require(ring.Submissions < forecast.BatchCount
                && batch.DispatchCount <= forecast.DispatchesPerBatch && (ulong)bytes.Length <= forecast.UploadBytes,
                "Queued work exceeds its authored submission, dispatch or transfer forecast.");
            ring.Submit(batch, upload: upload, bytes: bytes);
            CollectCompleted();
            // GPU cost is available only after harvest, through Metrics.
            return new(batch.DispatchCount, null);
        }

        internal void Retire(AurelianVulkanBuffer buffer)
        {
            ulong fence = ring.LastSubmittedFenceValue;
            if (fence <= owner.fences.CommandListFence.LastKnownCompletedValue)
            {
                buffer.Dispose();
                return;
            }
            retired.Add((buffer, fence));
            maximumRetiredBuffers = Math.Max(maximumRetiredBuffers, retired.Count);
        }

        public void Flush()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ring.Drain();
            CollectCompleted();
        }

        private void CollectCompleted()
        {
            ulong completed = owner.fences.CommandListFence.LastKnownCompletedValue;
            for (int index = retired.Count - 1; index >= 0; index--)
            {
                if (retired[index].Fence <= completed)
                {
                    retired[index].Buffer.Dispose();
                    retired.RemoveAt(index);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            try
            {
                Flush();
            }
            finally
            {
                // On failure, the ring establishes device idle before freeing
                // descriptors/staging; no advisory prediction authorizes release.
                ring.Dispose();
                foreach (var item in retired)
                {
                    item.Buffer.Dispose();
                }
                retired.Clear();
                owner.queuedScope = null;
                disposed = true;
            }
        }
    }
}
