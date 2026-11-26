using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference;
using Aurelian.Inference.Gemma4;

internal static class GemmaResidencyBenchmark
{
    private const int Layers = 6;
    private const int PromptLength = 64;
    private const int DecodeLength = 16;
    private const int Repetitions = 3;
    private const long WeightBudget = 1536L * 1024 * 1024;

    public static void Run(VulkanComputeMath compute, SafeTensorFile source, QuantizedCheckpoint packed,
        string modelDirectory, string outputDirectory, string device, bool streamed, bool queued)
    {
        using var model = new Gemma4TextModel(compute, source, packed, capacity: PromptLength + DecodeLength,
            residentWeightBudget: streamed ? 0 : WeightBudget, hostWeightBudget: WeightBudget,
            useQueuedExecution: queued);
        var tokenizer = new HuggingFaceBpeTokenizer(Path.Combine(modelDirectory, "tokenizer.json"));
        int[] tokens = tokenizer.Encode(Gemma4Chat.FormatUser(
            "Describe how a video editor prepares a documentary for a first screening. " +
            "Include the work of checking the picture, balancing dialogue and sound effects, " +
            "reviewing captions with the producer, and preparing a version for the audience. " +
            "Explain how the process changes when the film contains interviews recorded in several languages. " +
            "Give examples of the decisions the editor makes before sending a finished copy to the venue."));
        int[] prompt = tokens[..PromptLength];
        int[][] decode = tokens[PromptLength..(PromptLength + DecodeLength)].Select(id => new[] { id }).ToArray();

        var runs = new List<object>();
        for (int run = 0; run <= Repetitions; run++)
        {
            model.Reset();
            var prefill = Measure(compute, model, [prompt], queued);
            var incremental = Measure(compute, model, decode, queued);
            Console.WriteLine($"Run {run}: prefill {prefill.WallMilliseconds:F2} ms, decode {incremental.WallMilliseconds:F2} ms");
            if (run == 0)
            {
                continue;
            }

            foreach (var stage in new[] { prefill, incremental })
            {
                bool hasExpectedResidency = streamed
                    ? stage.UploadedWeightBytes > 0 && stage.HostCacheHits == stage.LinearCalls
                    : stage.UploadedWeightBytes == 0 && stage.DeviceCacheHits == stage.LinearCalls;
                if (!hasExpectedResidency)
                {
                    throw new InvalidDataException("The measured stage did not meet the requested packed-weight residency policy.");
                }
            }
            runs.Add(new { Prefill = prefill, Decode = incremental });
        }

        string residency = streamed ? "streamed" : "resident";
        string submission = queued ? "queued" : "sync";
        string path = Path.Combine(outputDirectory, $"residency-{residency}-{submission}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Graph = "Gemma 4 12B Unified transformer prefix, excluding vocabulary head",
            Device = device,
            packed.Manifest.Encoding,
            packed.Manifest.SourceSha256,
            Layers,
            Prompt = prompt,
            Decode = decode.Select(row => row[0]).ToArray(),
            Streamed = streamed,
            Queued = queued,
            model.ResidentWeightBytes,
            model.WeightHostCacheBytes,
            Memory = compute.MemoryTelemetry,
            Runs = runs,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
    }

    private static Stage Measure(VulkanComputeMath compute, Gemma4TextModel model, int[][] steps, bool queued)
    {
        var before = model.WeightTimings;
        long uploadedBefore = model.UploadedWeightBytes;
        GpuTensor? final = null;
        double elapsed;
        string hash;
        try
        {
            long start = Stopwatch.GetTimestamp();
            foreach (int[] step in steps)
            {
                final?.Dispose();
                final = model.Evaluate(step, Layers);
            }
            elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            float[] output = compute.Read(final!);
            if (Array.Exists(output, value => !float.IsFinite(value)))
            {
                throw new InvalidDataException("Nonfinite transformer activation.");
            }
            hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(output.AsSpan())));
        }
        finally
        {
            final?.Dispose();
        }

        var after = model.WeightTimings;
        var queue = model.QueueMeasurements.TakeLast(queued ? steps.Length : 0).ToArray();
        return new(elapsed, model.UploadedWeightBytes - uploadedBefore,
            after.Calls - before.Calls, after.GpuCacheHits - before.GpuCacheHits,
            after.HostCacheHits - before.HostCacheHits, after.ReadMilliseconds - before.ReadMilliseconds,
            after.AllocationMilliseconds - before.AllocationMilliseconds,
            after.ExecuteMilliseconds - before.ExecuteMilliseconds,
            after.GpuMilliseconds - before.GpuMilliseconds, hash, queue);
    }

    private sealed record Stage(double WallMilliseconds, long UploadedWeightBytes, long LinearCalls,
        long DeviceCacheHits, long HostCacheHits, double PackedReadMilliseconds,
        double PackedAllocationMilliseconds, double LinearExecuteWallMilliseconds,
        double? SynchronousLinearGpuMilliseconds, string OutputSha256, ComputeQueueMetrics[] Queue);
}
