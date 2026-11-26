using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Inference;
using Aurelian.Inference.Benchmarks;
using Aurelian.WhisperBenchmark;
using static Aurelian.WhisperBenchmark.TinyEnglishCheckpoint;

string[] options = args.Skip(5).ToArray();
string? queueOption = options.FirstOrDefault(option => option.StartsWith("queue=", StringComparison.Ordinal));
string? queuePolicy = queueOption?["queue=".Length..];
if (args.Length < 4)
{
    throw new ArgumentException("Usage: Aurelian.WhisperBenchmark CHECKPOINT MEL_F32 OUTPUT_JSON SAMPLES [per-head|batched|reuse|direct|gemv|gemv-scratch|fused] [persistent-read] [persistent-upload] [host-logits] [batch-cross] [queue=fixed-one|fixed-two|dominatus|dominatus-per-request] [step] [validate]");
}
int sampleCount = int.Parse(args[3], CultureInfo.InvariantCulture);
string mode = args.Length >= 5 ? args[4] : "per-head";
if (sampleCount is < 1 or > 100 || mode is not ("per-head" or "batched" or "reuse" or "direct" or "gemv" or "gemv-scratch" or "fused")
    || options.Any(option => option != queueOption && option is not ("validate" or "persistent-read" or "persistent-upload" or "host-logits" or "batch-cross" or "step"))
    || (options.Contains("host-logits") && !options.Contains("persistent-read"))
    || (options.Contains("batch-cross") && mode is not ("direct" or "gemv" or "gemv-scratch" or "fused"))
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
using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "Aurelian Whisper benchmark",
    MaximumTensorElements = 32 * 1024 * 1024,
    EnableValidation = options.Contains("validate"),
});
bool useGemv = mode is "gemv" or "gemv-scratch" or "fused";
using var model = new TinyEnglishModel(session.Compute, checkpoint, batchAcrossHeads: mode != "per-head",
    reuseAttention: mode == "reuse", directAttention: mode == "direct" || useGemv, vectorProjection: useGemv,
    reuseScratch: mode is "gemv-scratch" or "fused", fusedAttention: mode == "fused",
    reuseReadback: options.Contains("persistent-read"), reuseUpload: options.Contains("persistent-upload"),
    hostLogits: options.Contains("host-logits"), batchCross: options.Contains("batch-cross"));
const int maximumTokens = 64;
int decoderBatches = options.Contains("batch-cross") ? 2 : 2 * Layers + 2;
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
        IReadOnlyDictionary<string, OperationTiming> Operations, QueueSample? Queue);

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

    internal sealed class InferenceMetrics(bool queued)
    {
        private readonly Dictionary<string, OperationTiming> operations = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, OperationTiming> Operations => operations;

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
