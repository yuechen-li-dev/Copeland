using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference.Benchmarks;

namespace Aurelian.Inference.Whisper;

internal sealed record InferenceSample(double TotalMs, double EncodeMs, double PrefillMs,
    double DecodeMs, List<int> Tokens, int Readbacks, ulong Allocations, long ManagedBytes,
    IReadOnlyDictionary<string, OperationTiming> Operations, QueueSample? Queue,
    IReadOnlyList<EncoderAttentionGpuTiming>? EncoderAttentionProfile,
    Dictionary<string, Dictionary<string, KernelTime>>? KernelProfile,
    EncoderUploadTiming[]? EncoderUploads, List<PrefetchTiming>? PrefetchCopies, EncoderBatchInterval[]? EncoderBatches,
    CheckpointTileSample? CheckpointTiles);

internal sealed record EncoderBatchInterval(string Operation,
    Aurelian.Graphics.Vulkan.Native3D.Native3DCompletedGpuFrame? Compute,
    Aurelian.Graphics.Vulkan.Native3D.Native3DCompletedGpuFrame? Transfer);

internal sealed record EncoderUploadTiming(ulong Bytes, double StagingMs, double? CopyMs,
    double? PreComputeGapMs, double? CopyInGapMs);

internal sealed record EncoderAttentionGpuTiming(double KeyScatterMs, double ValueScatterMs,
    double QueryPackMs, double KeyValuePackMs, double AttentionMs)
{
    internal static EncoderAttentionGpuTiming From(IReadOnlyList<double> intervals, bool packed)
    {
        if (intervals.Count != (packed ? 5 : 3))
        {
            throw new InvalidDataException("Unexpected encoder attention dispatch sequence.");
        }
        return new(intervals[0], intervals[1], packed ? intervals[2] : 0,
            packed ? intervals[3] : 0, intervals[^1]);
    }
}

internal sealed class OperationTiming
{
    public double WallMs { get; set; }
    public double GpuMs { get; set; }
    public int TimedSubmissions { get; set; }
    public int ComputeSubmissions { get; set; }
    public int TransferCalls { get; set; }
    public int Dispatches { get; set; }
    public int CompletionWaits { get; set; }
}

internal sealed class KernelTime
{
    public int Dispatches { get; set; }
    public double GpuMs { get; set; }
}

internal sealed class InferenceMetrics(bool queued, bool profileGpu)
{
    private readonly Dictionary<string, OperationTiming> operations = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, OperationTiming> Operations => operations;

    public List<(string Operation, QueuedDispatchProfile Profile)>? Profiles { get; } = profileGpu ? [] : null;

    public Dictionary<string, Dictionary<string, KernelTime>>? CollectProfiles()
    {
        if (Profiles is null)
        {
            return null;
        }
        var groups = new Dictionary<string, Dictionary<string, KernelTime>>();
        foreach (var (operation, profile) in Profiles)
        {
            if (!groups.TryGetValue(operation, out var kernels))
            {
                kernels = new();
                groups.Add(operation, kernels);
            }
            for (int i = 0; i < profile.ShaderNames.Count; i++)
            {
                string shader = profile.ShaderNames[i];
                if (!kernels.TryGetValue(shader, out var timing))
                {
                    timing = new();
                    kernels.Add(shader, timing);
                }
                timing.Dispatches++;
                timing.GpuMs += profile.DispatchMilliseconds[i];
            }
        }
        return groups;
    }

    public void Record(string name, double wallMs, double? gpuMs, int submissions, int dispatches, int waits)
    {
        OperationTiming timing = GetTiming(name);
        timing.WallMs += wallMs;
        if (gpuMs is { } gpu)
        {
            timing.GpuMs += gpu;
            timing.TimedSubmissions += submissions;
        }
        timing.ComputeSubmissions += submissions;
        timing.Dispatches += dispatches;
        if (!queued)
        {
            timing.CompletionWaits += waits;
        }
    }

    public void RecordTransfer(string name, double wallMs)
    {
        OperationTiming timing = GetTiming(name);
        timing.WallMs += wallMs;
        timing.TransferCalls++;
    }

    private OperationTiming GetTiming(string name)
    {
        if (!operations.TryGetValue(name, out OperationTiming? timing))
        {
            timing = new();
            operations.Add(name, timing);
        }
        return timing;
    }
}
