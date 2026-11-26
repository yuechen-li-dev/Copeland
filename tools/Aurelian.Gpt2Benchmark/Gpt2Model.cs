using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Inference.Benchmarks;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Gpt2Benchmark;

internal sealed record Prediction(int Token, float Score);
internal sealed record Gpt2Sample(double PrefillMs, double DecodeMs, double PrefillGpuMs, double DecodeGpuMs,
    IReadOnlyList<Prediction> Predictions, double DecodeReadbackMs, ulong DecodeAllocations, long DecodeManagedBytes,
    IReadOnlyDictionary<GpuPhase, double>? PrefillGpuPhases, IReadOnlyDictionary<GpuPhase, double>? DecodeGpuPhases, QueueSample? Queue,
    ulong RequestAllocations, long RequestManagedBytes, double RequestMs);

internal enum GpuPhase
{
    Embedding, Normalization, QueryKeyValue, CacheWrites, Attention, AttentionOutput,
    FeedForwardUp, Gelu, FeedForwardDown, Vocabulary
}

// GPT-2 small with learned positions and a resident FP32 cache. The benchmark
// supplies fixed input IDs so both runtimes evaluate the same sequence.
internal sealed class Gpt2Model : IDisposable
{
    private const int Width = 768;
    private const int Heads = 12;
    private const int HeadWidth = Width / Heads;
    private const int Expansion = 3072;
    private const int Vocabulary = 50257;
    private const int PromptLength = 32;
    private const int DecodeSteps = 32;
    public const int EvaluationsPerRequest = 1 + DecodeSteps;
    private const int Context = PromptLength + DecodeSteps;

    private sealed record Linear(GpuTensor Weight, GpuTensor Bias);
    private sealed record Block(GpuTensor AttentionNorm, Linear Qkv, Linear AttentionOutput, GpuTensor FeedForwardNorm,
        Linear Up, Linear Down, GpuTensor KeyCache, GpuTensor ValueCache);
    private sealed record Scratch(GpuTensor Hidden, GpuTensor Normalized, GpuTensor Qkv, GpuTensor Query,
        GpuTensor Key, GpuTensor Value, GpuTensor Attention, GpuTensor Projected, GpuTensor Expanded);
    private sealed record AttentionScratch(GpuTensor Scores, GpuTensor Probabilities);

    public Stream? LogitsCapture { private get; set; }

    private readonly VulkanComputeMath math;
    private readonly bool vectorProjection;
    private readonly float[]? hostLogits;
    private readonly bool profileDispatches;
    private readonly bool reuseStaging;
    private readonly bool widthNorm;
    private readonly bool prefillHead;
    private readonly bool prefillDown;
    private readonly bool fusedQkv;
    private readonly bool prefillAttentionKernel;
    private readonly bool lastRowMlp;
    private readonly List<GpuTensor> tensors = [];
    private readonly Block[] blocks = new Block[12];
    private readonly GpuTensor embeddings;
    private readonly GpuTensor positions;
    private readonly GpuTensor outputWeights;
    private readonly GpuTensor outputNorm;
    private readonly GpuTensor lastRow;
    private readonly GpuTensor logits;
    private readonly Scratch prefill;
    private readonly Scratch decode;
    private readonly AttentionScratch prefillAttention;
    private readonly AttentionScratch[] decodeAttention = new AttentionScratch[DecodeSteps];

    public Gpt2Model(VulkanComputeMath math, Gpt2Checkpoint checkpoint, bool vectorProjection,
        bool profileDispatches, bool reuseStaging, bool widthNorm, bool hostLogits, bool prefillHead, bool prefillDown, bool fusedQkv,
        bool prefillAttentionKernel, bool lastRowMlp)
    {
        this.math = math;
        this.vectorProjection = vectorProjection;
        this.hostLogits = hostLogits ? new float[Vocabulary] : null;
        this.profileDispatches = profileDispatches;
        this.reuseStaging = reuseStaging;
        this.widthNorm = widthNorm;
        this.prefillHead = prefillHead;
        this.prefillDown = prefillDown;
        this.fusedQkv = fusedQkv;
        this.prefillAttentionKernel = prefillAttentionKernel;
        this.lastRowMlp = lastRowMlp;
        try
        {
            embeddings = Upload("token_embd.weight", Vocabulary, Width, Width, Vocabulary);
            positions = Upload("position_embd.weight", 1024, Width, Width, 1024);
            outputWeights = Upload("output.weight", Vocabulary, Width, Width, Vocabulary);
            outputNorm = LoadNorm("output_norm");
            for (int layer = 0; layer < blocks.Length; layer++)
            {
                string prefix = $"blk.{layer}.";
                var keys = Create(fusedQkv || prefillAttentionKernel ? 2 * Context : Context, Width);
                blocks[layer] = new(LoadNorm(prefix + "attn_norm"),
                    LoadLinear(prefix + "attn_qkv", Width, 3 * Width),
                    LoadLinear(prefix + "attn_output", Width, Width),
                    LoadNorm(prefix + "ffn_norm"),
                    LoadLinear(prefix + "ffn_up", Width, Expansion),
                    LoadLinear(prefix + "ffn_down", Expansion, Width),
                    keys, fusedQkv || prefillAttentionKernel ? keys : Create(Context, Width));
            }

            lastRow = Create(1, Width);
            logits = Create(1, Vocabulary);
            prefill = CreateScratch(PromptLength);
            decode = CreateScratch(1);
            prefillAttention = CreateAttention(PromptLength, PromptLength);
            for (int index = 0; index < decodeAttention.Length; index++)
            {
                decodeAttention[index] = CreateAttention(1, PromptLength + index + 1);
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        GpuTensor Upload(string name, int rows, int columns, params int[] shape)
        {
            var tensor = Create(rows, columns);
            math.Upload(tensor, checkpoint.Read(name, shape));
            return tensor;
        }

        GpuTensor LoadNorm(string name)
        {
            var tensor = Create(1, Width * 2);
            math.Upload(tensor, [.. checkpoint.Read(name + ".weight", Width), .. checkpoint.Read(name + ".bias", Width)]);
            return tensor;
        }

        Linear LoadLinear(string name, int input, int output) =>
            new(Upload(name + ".weight", output, input, input, output), Upload(name + ".bias", 1, output, output));
    }

    public Gpt2Sample Run(BenchmarkQueue? queue)
    {
        long requestStart = Stopwatch.GetTimestamp();
        long requestBytes = GC.GetAllocatedBytesForCurrentThread();
        ulong requestAllocations = math.MemoryTelemetry.AllocationCount;
        using var requestScope = queue?.OpenRequestScope();
        var scope = requestScope ?? queue?.Retained;
        var initialMetrics = scope?.Metrics;
        var predictions = new List<Prediction>(DecodeSteps + 1);
        Dictionary<GpuPhase, double>? prefillPhases = profileDispatches ? new() : null;
        Dictionary<GpuPhase, double>? decodePhases = profileDispatches ? new() : null;
        var timer = Stopwatch.StartNew();
        var (first, prefillGpu, _) = Evaluate(prefill, prefillAttention, firstToken: 100, position: 0, prefillPhases);
        predictions.Add(first);
        double prefillMs = timer.Elapsed.TotalMilliseconds;
        var afterPrefill = scope?.Metrics;
        timer.Restart();
        double decodeGpu = 0;
        double decodeReadback = 0;
        long managedStart = GC.GetAllocatedBytesForCurrentThread();
        ulong allocations = math.MemoryTelemetry.AllocationCount;
        for (int step = 0; step < DecodeSteps; step++)
        {
            var (prediction, gpuTime, readback) = Evaluate(decode, decodeAttention[step], 500 + step, PromptLength + step, decodePhases);
            predictions.Add(prediction);
            if (scope is null)
            {
                decodeGpu += gpuTime ?? throw new InvalidOperationException("Missing decode timestamps.");
            }
            decodeReadback += readback;
        }
        double decodeMs = timer.Elapsed.TotalMilliseconds;
        long decodeBytes = GC.GetAllocatedBytesForCurrentThread() - managedStart;
        requestScope?.Dispose();
        var finalMetrics = scope?.Metrics;
        if (scope is not null)
        {
            prefillGpu = afterPrefill!.GpuMilliseconds - initialMetrics!.GpuMilliseconds;
            decodeGpu = (finalMetrics!.GpuMilliseconds - afterPrefill.GpuMilliseconds)
                ?? throw new InvalidOperationException("Missing queued decode timestamps.");
        }
        return new(prefillMs, decodeMs, prefillGpu ?? throw new InvalidOperationException("Missing prefill timestamps."),
            decodeGpu, predictions, decodeReadback, math.MemoryTelemetry.AllocationCount - allocations,
            decodeBytes, prefillPhases, decodePhases, QueueSample.Between(initialMetrics, finalMetrics),
            math.MemoryTelemetry.AllocationCount - requestAllocations, GC.GetAllocatedBytesForCurrentThread() - requestBytes,
            Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
    }

    private (Prediction, double? GpuMs, double ReadbackMs) Evaluate(Scratch s, AttentionScratch attention,
        int firstToken, int position, Dictionary<GpuPhase, double>? phases)
    {
        var batch = math.CreateBatch();
        List<(GpuPhase Phase, int End)>? boundaries = phases is null ? null : [];
        int rows = s.Hidden.Rows;
        int keyCount = position + rows;
        bool trimFinalMlp = lastRowMlp && rows > 1;
        batch.Gather(embeddings, s.Hidden, firstRow: firstToken);
        batch.Gather(positions, s.Normalized, firstRow: position);
        batch.Activate(s.Hidden, s.Hidden, TensorActivation.Residual, s.Normalized);
        Mark(GpuPhase.Embedding);
        foreach (Block block in blocks)
        {
            Normalize(batch, s.Hidden, block.AttentionNorm, s.Normalized);
            Mark(GpuPhase.Normalization);
            Project(batch, s.Normalized, block.Qkv, s.Qkv);
            Mark(GpuPhase.QueryKeyValue);
            int valueOffset = fusedQkv || prefillAttentionKernel ? Context : 0;
            if (fusedQkv && rows == 1)
            {
                batch.DecodeQkv(s.Qkv, block.KeyCache, s.Query, position, 1f / MathF.Sqrt(HeadWidth));
            }
            else
            {
                batch.Gather(s.Qkv, s.Query);
                batch.Gather(s.Qkv, s.Key, firstColumn: Width);
                batch.Gather(s.Qkv, s.Value, firstColumn: 2 * Width);
                batch.Scatter(s.Key, block.KeyCache, firstRow: position);
                batch.Scatter(s.Value, block.ValueCache, firstRow: valueOffset + position);
                batch.Scale(s.Query, s.Query, 1f / MathF.Sqrt(HeadWidth));
            }
            Mark(GpuPhase.CacheWrites);
            if (prefillAttentionKernel && rows > 1)
            {
                batch.CausalPrefillAttention64(s.Query, block.KeyCache, s.Attention);
            }
            else
            {
                batch.GroupedAttentionScores(s.Query, block.KeyCache, attention.Scores,
                    Heads, Heads, HeadWidth, position, keyCount, Context, roundBFloat16: false);
                batch.ReduceRows(attention.Scores, attention.Probabilities);
                batch.GroupedAttentionValues(attention.Probabilities, block.ValueCache, s.Attention,
                    Heads, Heads, HeadWidth, roundBFloat16: false, firstValueRow: valueOffset);
            }
            Mark(GpuPhase.Attention);
            Project(batch, s.Attention, block.AttentionOutput, s.Projected);
            batch.Activate(s.Hidden, s.Hidden, TensorActivation.Residual, s.Projected);
            Mark(GpuPhase.AttentionOutput);
            if (trimFinalMlp && ReferenceEquals(block, blocks[^1]))
            {
                // All layer caches are complete. Only this row contributes to the requested logits.
                batch.Gather(s.Hidden, decode.Hidden, firstRow: rows - 1);
                s = decode;
            }
            Normalize(batch, s.Hidden, block.FeedForwardNorm, s.Normalized);
            Mark(GpuPhase.Normalization);
            Project(batch, s.Normalized, block.Up, s.Expanded);
            Mark(GpuPhase.FeedForwardUp);
            batch.Activate(s.Expanded, s.Expanded, TensorActivation.Gelu);
            Mark(GpuPhase.Gelu);
            Project(batch, s.Expanded, block.Down, s.Projected);
            batch.Activate(s.Hidden, s.Hidden, TensorActivation.Residual, s.Projected);
            Mark(GpuPhase.FeedForwardDown);
        }
        if (!trimFinalMlp)
        {
            batch.Gather(s.Hidden, lastRow, firstRow: rows - 1);
        }
        Normalize(batch, trimFinalMlp ? decode.Hidden : lastRow, outputNorm, lastRow);
        Mark(GpuPhase.Normalization);
        Multiply(batch, lastRow, outputWeights, logits, allowVector: prefillHead || rows == 1);
        Mark(GpuPhase.Vocabulary);
        ComputeMathResult execution;
        if (phases is not null && boundaries is not null)
        {
            ComputeDispatchProfile profile = math.ExecuteWithDispatchTimings(batch);
            execution = profile.Execution;
            int index = 0;
            foreach (var (phase, end) in boundaries)
            {
                double milliseconds = 0;
                while (index < end)
                {
                    milliseconds += profile.DispatchMilliseconds[index++];
                }
                phases[phase] = phases.GetValueOrDefault(phase) + milliseconds;
            }
        }
        else
        {
            execution = math.Execute(batch);
        }
        long readbackStart = Stopwatch.GetTimestamp();
        float[] values;
        if (hostLogits is { } destination)
        {
            math.ReadInto(logits, destination);
            values = destination;
        }
        else
        {
            values = math.Read(logits, reuseStaging);
        }
        double readbackMs = Stopwatch.GetElapsedTime(readbackStart).TotalMilliseconds;
        if (LogitsCapture is { } capture)
        {
            capture.Write(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        int best = 0;
        for (int index = 0; index < values.Length; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                throw new InvalidDataException("GPT-2 produced non-finite logits.");
            }
            if (values[index] > values[best])
            {
                best = index;
            }
        }
        return (new(best, values[best]), execution.GpuMilliseconds, readbackMs);

        void Mark(GpuPhase phase) => boundaries?.Add((phase, batch.DispatchCount));
    }

    private void Normalize(ComputeMathBatch batch, GpuTensor input, GpuTensor weights, GpuTensor output)
    {
        batch.Normalize(input, weights, output, Normalization.LayerNorm, 1e-5f, specializeWidth: widthNorm);
    }

    private void Project(ComputeMathBatch batch, GpuTensor input, Linear linear, GpuTensor output)
    {
        Multiply(batch, input, linear.Weight, output, allowVector: input.Rows == 1);
        batch.Activate(output, output, TensorActivation.Bias, linear.Bias);
    }

    private void Multiply(ComputeMathBatch batch, GpuTensor input, GpuTensor weight, GpuTensor output, bool allowVector)
    {
        batch.ProjectDense(input, weight, output, useVectorKernel: allowVector && vectorProjection,
            usePromptTile: prefillDown);
    }

    private GpuTensor Create(int rows, int columns)
    {
        GpuTensor tensor = rows == Vocabulary ? math.CreateSparseTensor(rows, columns) : math.CreateTensor(rows, columns);
        tensors.Add(tensor);
        return tensor;
    }

    private Scratch CreateScratch(int rows) => new(Create(rows, Width), Create(rows, Width),
        Create(rows, 3 * Width), Create(rows, Width), Create(rows, Width), Create(rows, Width),
        Create(rows, Width), Create(rows, Width), Create(rows, Expansion));

    private AttentionScratch CreateAttention(int rows, int keys) =>
        new(Create(rows * Heads, keys), Create(rows * Heads, keys));

    public void Dispose()
    {
        foreach (GpuTensor tensor in tensors)
        {
            tensor.Dispose();
        }
        tensors.Clear();
    }
}
