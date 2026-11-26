using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Inference;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference.Benchmarks;
using Aurelian.WhisperBenchmark;
using static Aurelian.WhisperBenchmark.TinyEnglishCheckpoint;

string[] options = args.Skip(5).ToArray();
string? queueOption = options.FirstOrDefault(option => option.StartsWith("queue=", StringComparison.Ordinal));
string? queuePolicy = queueOption?["queue=".Length..];
if (args.Length < 4)
{
    throw new ArgumentException("Usage: Aurelian.WhisperBenchmark CHECKPOINT MEL_F32 OUTPUT_JSON SAMPLES [per-head|batched|reuse|direct|gemv|gemv-scratch|fused] [persistent-read] [persistent-upload] [host-logits] [batch-cross] [join-logits] [half-projections] [half-attention] [cooperative-attention|attention-32|attention-64] [packed-vocab] [cooperative-convolution] [direct-projections|reuse-projections] [profile-attention] [profile-gpu] [general-norm] [rewrite-bindings] [projection-epilogues] [capture] [queue=fixed-one|fixed-two|dominatus|dominatus-per-request] [step] [validate]");
}
int sampleCount = int.Parse(args[3], CultureInfo.InvariantCulture);
string mode = args.Length >= 5 ? args[4] : "per-head";
if (sampleCount is < 1 or > 100 || mode is not ("per-head" or "batched" or "reuse" or "direct" or "gemv" or "gemv-scratch" or "fused")
    || options.Any(option => option != queueOption && option is not ("validate" or "persistent-read" or "persistent-upload" or "host-logits" or "batch-cross" or "step" or "capture" or "half-attention" or "cooperative-attention" or "attention-32" or "attention-64" or "packed-vocab" or "cooperative-convolution" or "direct-projections" or "reuse-projections" or "half-projections" or "join-logits" or "profile-attention" or "profile-gpu" or "general-norm" or "rewrite-bindings" or "projection-epilogues"))
    || (options.Contains("host-logits") && !options.Contains("persistent-read"))
    || (options.Contains("batch-cross") && mode is not ("direct" or "gemv" or "gemv-scratch" or "fused"))
    || ((options.Contains("profile-gpu") || options.Contains("profile-attention")) && queuePolicy is null)
    || (options.Contains("profile-gpu") && (!options.Contains("batch-cross") || options.Contains("profile-attention")))
    || (options.Any(option => option is "half-attention" or "cooperative-attention" or "attention-32" or "attention-64" or "packed-vocab" or "cooperative-convolution" or "direct-projections" or "reuse-projections") && mode != "fused")
    || options.Count(option => option is "attention-32" or "attention-64" or "cooperative-attention") > 1
    || (options.Contains("direct-projections") && options.Contains("reuse-projections"))
    || (queuePolicy is not null && mode != "fused")
    || options.Distinct().Count() != options.Length)
{
    throw new ArgumentException("Expected 1–100 samples, a supported execution mode and optional validate.");
}

byte[] melBytes = File.ReadAllBytes(args[1]);
if (melBytes.Length != AudioRows * 2 * MelBins * sizeof(float))
{
    throw new InvalidDataException("Expected a 3,000 x 80 frame-major little-endian FP32 mel fixture.");
}
float[] mel = MemoryMarshal.Cast<byte, float>(melBytes).ToArray();
var initialization = Stopwatch.StartNew();
using var checkpoint = new TinyEnglishCheckpoint(args[0]);
bool packedProjections = options.Any(option => option is "half-projections" or "direct-projections" or "reuse-projections");
bool cooperativeAttention = options.Any(option => option is "cooperative-attention" or "attention-32" or "attention-64");
int attentionKeyTile = 16;
if (options.Contains("attention-32"))
{
    attentionKeyTile = 32;
}
else if (options.Contains("attention-64"))
{
    attentionKeyTile = 64;
}
bool cooperativeConvolution = options.Contains("cooperative-convolution");
using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "Aurelian Whisper benchmark",
    MaximumTensorElements = 32 * 1024 * 1024,
    UseCooperativeLinear = packedProjections || cooperativeAttention || cooperativeConvolution,
    EnableValidation = options.Contains("validate"),
});
session.Compute.ReuseDispatchBindings = !options.Contains("rewrite-bindings");
session.Compute.UseWidthSpecializedLayerNorm = !options.Contains("general-norm");
bool useGemv = mode is "gemv" or "gemv-scratch" or "fused";
using var model = new TinyEnglishModel(session.Compute, checkpoint, batchAcrossHeads: mode != "per-head",
    reuseAttention: mode == "reuse", directAttention: mode == "direct" || useGemv, vectorProjection: useGemv,
    reuseScratch: mode is "gemv-scratch" or "fused", fusedAttention: mode == "fused",
    reuseReadback: options.Contains("persistent-read"), reuseUpload: options.Contains("persistent-upload"),
    hostLogits: options.Contains("host-logits"), batchCross: options.Contains("batch-cross"),
    halfProjections: packedProjections, halfAttention: cooperativeAttention || options.Contains("half-attention"),
    joinLogits: options.Contains("join-logits"), packVocabulary: options.Contains("packed-vocab"),
    cooperativeConvolution: cooperativeConvolution)
{
    FuseProjectionEpilogues = options.Contains("projection-epilogues"),
    CooperativeAttention = cooperativeAttention,
    AttentionKeyTile = attentionKeyTile,
    DirectProjectionTiles = options.Contains("direct-projections") || options.Contains("reuse-projections"),
    ReuseProjectionTiles = options.Contains("reuse-projections"),
    ProfileGpu = options.Contains("profile-gpu"),
    ProfileAttention = options.Contains("profile-attention"),
};
const int maximumTokens = 64;
int decoderBatches = options.Contains("batch-cross") ? 2 : 2 * Layers + 2;
if (options.Contains("join-logits"))
{
    decoderBatches--;
}
int encoderBatches = 2 + 3 * Layers;
using var queue = queuePolicy is null ? null : new BenchmarkQueue(session.Compute, queuePolicy,
    encoderBatches + (maximumTokens + 1) * decoderBatches, sampleCount + 2);
double initializationMs = initialization.Elapsed.TotalMilliseconds;
Console.WriteLine(session.Plant.Facts.PhysicalDeviceName);
var samples = new List<InferenceSample>(sampleCount);
var warmupMs = new List<double>(2);
bool stepRequests = options.Contains("step");
for (int index = -2; index < sampleCount; index++)
{
    if (stepRequests && index >= 0)
    {
        BenchmarkQueue.WaitForRequest();
    }
    if (options.Contains("capture") && index == sampleCount - 1)
    {
        model.CaptureDirectory = Directory.CreateDirectory(args[2] + ".capture").FullName;
    }
    InferenceSample sample = model.Transcribe(mel, maximumTokens, queue);
    Console.WriteLine(FormattableString.Invariant(
        $"Iteration {index + 1}: {sample.TotalMs:F2} ms, {sample.Tokens.Count} outputs. {checkpoint.Decode(sample.Tokens)}"));
    if (sample.Tokens[^1] != EndOfText)
    {
        throw new InvalidDataException("Tiny.en did not emit end-of-text within the benchmark token budget.");
    }
    if (index < 0)
    {
        warmupMs.Add(sample.TotalMs);
    }
    else
    {
        samples.Add(sample);
    }
}

File.WriteAllText(args[2], JsonSerializer.Serialize(new
{
    Runtime = "Aurelian",
    Mode = mode,
    Options = options,
    QueuePolicy = queuePolicy,
    Controller = queue?.Evidence,
    Device = session.Plant.Facts.PhysicalDeviceName,
    Validation = session.Plant.Facts.EnabledValidationLayers,
    InitializationMs = initializationMs,
    WarmupMs = warmupMs,
    Samples = samples,
    Transcript = checkpoint.Decode(samples[0].Tokens),
    Memory = session.Compute.MemoryTelemetry,
}, new JsonSerializerOptions { WriteIndented = true }));

namespace Aurelian.WhisperBenchmark
{
    internal sealed record InferenceSample(double TotalMs, double EncodeMs, double PrefillMs,
        double DecodeMs, List<int> Tokens, int Readbacks, ulong Allocations, long ManagedBytes,
        IReadOnlyDictionary<string, OperationTiming> Operations, QueueSample? Queue,
        IReadOnlyList<EncoderAttentionGpuTiming>? EncoderAttentionProfile,
        Dictionary<string, Dictionary<string, KernelTime>>? KernelProfile);

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
}
