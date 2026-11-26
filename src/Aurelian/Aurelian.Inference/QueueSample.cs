using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Benchmarks;

internal sealed record QueueSample(int Depth, int Submissions, int Dispatches, int CompletionWaits,
    int PeakOutstandingSincePlan, int PeakInFlightSincePlan, double GpuMs)
{
    public long PrefetchSubmissions { get; init; }
    public long UploadedBytes { get; init; }
    public double CpuRecordingMs { get; init; }
    public double CpuSubmissionMs { get; init; }
    public double CpuCompletionWaitMs { get; init; }

    public static QueueSample? Between(ComputeQueueMetrics? start, ComputeQueueMetrics? end)
    {
        if (start is null || end is null)
        {
            return null;
        }
        double gpuMs = end.GpuMilliseconds - start.GpuMilliseconds
            ?? throw new InvalidOperationException("The queued benchmark requires GPU timestamps.");
        return new(end.Depth, end.Submissions - start.Submissions, end.Dispatches - start.Dispatches,
            end.CompletionWaits - start.CompletionWaits, end.MaximumOutstanding, end.MaximumInFlight, gpuMs)
        {
            PrefetchSubmissions = end.PrefetchSubmissions - start.PrefetchSubmissions,
            UploadedBytes = end.UploadedBytes - start.UploadedBytes,
            CpuRecordingMs = end.CpuRecordingMilliseconds - start.CpuRecordingMilliseconds,
            CpuSubmissionMs = end.CpuSubmissionMilliseconds - start.CpuSubmissionMilliseconds,
            CpuCompletionWaitMs = end.CpuCompletionWaitMilliseconds - start.CpuCompletionWaitMilliseconds,
        };
    }
}

