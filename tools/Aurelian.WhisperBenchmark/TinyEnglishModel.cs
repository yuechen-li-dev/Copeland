using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Inference.Benchmarks;
using Aurelian.Graphics.Vulkan.ComputeMath;
using static Aurelian.WhisperBenchmark.TinyEnglishCheckpoint;

namespace Aurelian.WhisperBenchmark;

// A single 30-second encoder window and bounded greedy no-timestamp decoding.
// The graph uses existing kernels and owns its resident weights for the run.
internal sealed class TinyEnglishModel : IDisposable
{
    private sealed record Linear(GpuTensor Weight, GpuTensor? Bias);
    private sealed record Attention(GpuTensor Norm, Linear Query, Linear Key, Linear Value, Linear Output);
    private sealed record Mlp(GpuTensor Norm, Linear Up, Linear Down);
    private sealed record Block(Attention Self, Mlp Mlp, Attention? Cross);
    private sealed record Cache(GpuTensor Key, GpuTensor Value);

    private readonly VulkanComputeMath math;
    private readonly bool batchAcrossHeads;
    private readonly bool vectorProjection;
    private readonly float[]? hostLogits;
    private readonly bool fusedAttention;
    private readonly bool batchCross;
    private readonly bool halfAttention;
    private readonly bool joinLogits;
    private readonly Dictionary<(int Rows, int Columns, GpuTensorFormat Format), GpuTensor> projectionScratch = [];
    public bool FuseProjectionEpilogues { get; init; }
    public bool ProfileStream { get; init; }
    public bool ProfileGpu { get; init; }
    public bool CooperativeAttention { get; init; }
    public bool DirectProjectionTiles { get; init; }
    public bool ReuseProjectionTiles { get; init; }
    public bool ProfileAttention { get; init; }
    public string? CaptureDirectory { get; set; }
    private readonly bool reuseReadback;
    private readonly bool reuseUpload;
    private readonly VulkanComputeMath.TiledAttentionWorkspace? crossWorkspace;
    private readonly VulkanComputeMath.TiledAttentionWorkspace? encoderWorkspace;
    private readonly Dictionary<(int Rows, int Columns), Stack<GpuTensor>>? scratchPool;
    private readonly GpuTensor? crossScores;
    private readonly GpuTensor? crossProbabilities;
    private readonly List<GpuTensor> resident = [];
    private readonly Linear conv1;
    private readonly Linear conv2;
    private readonly GpuTensor audioPositions;
    private readonly GpuTensor textPositions;
    private readonly GpuTensor embedding;
    private readonly GpuTensor? packedVocabulary;
    public int AttentionKeyTile { get; init; } = 16;
    private readonly GpuTensor encodedNorm;
    private readonly GpuTensor decodedNorm;
    private readonly ComputeBatchUpload?[] uploads = new ComputeBatchUpload?[Layers];
    public ulong MaximumUploadBytes { get; }
    public ulong EncoderWeightBytes { get; }
    private readonly CheckpointEncoderBlock[]? checkpointBlocks;
    private readonly CheckpointTileController? tileController;
    private readonly IComputeStreamController? previousController;
    private readonly bool prefetchEncoder;
    public bool CopyAtUse { get; init; }
    public int PinnedBlocks { get; }
    private readonly Block?[] encoder = new Block?[Layers];
    private readonly Block[] decoder = new Block[Layers];
    private readonly Cache[] selfCache = new Cache[Layers];
    private readonly Cache[] crossCache = new Cache[Layers];

    public TinyEnglishModel(VulkanComputeMath math, TinyEnglishCheckpoint weights, bool batchAcrossHeads,
        bool reuseAttention, bool directAttention, bool vectorProjection, bool reuseScratch,
        bool fusedAttention, bool reuseReadback, bool reuseUpload, bool hostLogits, bool batchCross,
        bool halfProjections, bool halfAttention, bool joinLogits, bool packVocabulary = false, bool cooperativeConvolution = false,
        bool packedLookup = false, int? encoderBudgetMiB = null, bool prefetchEncoder = false,
        bool checkpointTiles = false)
    {
        EncoderBlockUpload? stream = null;
        this.prefetchEncoder = prefetchEncoder;
        if (checkpointTiles)
        {
            checkpointBlocks = new CheckpointEncoderBlock[Layers];
            tileController = new();
            previousController = math.StreamController;
        }
        this.math = math;
        this.batchAcrossHeads = batchAcrossHeads;
        this.vectorProjection = vectorProjection;
        this.hostLogits = hostLogits ? new float[Vocabulary] : null;
        this.fusedAttention = fusedAttention;
        this.batchCross = batchCross;
        this.halfAttention = halfAttention;
        this.joinLogits = joinLogits;
        if (halfProjections && !math.CooperativeMatrixEnabled)
        {
            throw new NotSupportedException("Packed encoder projections require cooperative FP16 multiply with FP32 accumulation.");
        }
        this.reuseReadback = reuseReadback;
        this.reuseUpload = reuseUpload;
        try
        {
            if (reuseScratch)
            {
                scratchPool = new();
                if (!fusedAttention)
                {
                    encoderWorkspace = math.CreateTiledAttentionWorkspace(AudioRows, AudioRows, HeadWidth, math.AttentionQueryTileRows);
                }
            }
            if (reuseAttention)
            {
                crossWorkspace = math.CreateTiledAttentionWorkspace(1, AudioRows, HeadWidth, math.AttentionQueryTileRows);
            }
            if (directAttention)
            {
                crossScores = Allocate(Heads, AudioRows);
                crossProbabilities = Allocate(Heads, AudioRows);
            }
            conv1 = Convolution("encoder.conv1", MelBins);
            conv2 = Convolution("encoder.conv2", Width);
            audioPositions = Upload(AudioRows, Width, weights.Read("encoder.positional_embedding", Width, AudioRows));
            textPositions = Upload(TextRows, Width, weights.Read("decoder.positional_embedding", Width, TextRows));
            float[] tokenWeights = weights.Read("decoder.token_embedding.weight", Width, Vocabulary);
            if (packVocabulary || packedLookup)
            {
                packedVocabulary = math.CreateSparseTensor(Vocabulary, Width, GpuTensorFormat.PackedFloat16);
                resident.Add(packedVocabulary);
                math.Upload(packedVocabulary, tokenWeights);
            }
            embedding = packedLookup ? packedVocabulary! : Upload(Vocabulary, Width, tokenWeights);
            encodedNorm = Norm("encoder.ln_post");
            decodedNorm = Norm("decoder.ln");
            ulong fixedBytes = conv1.Weight.Buffer.SizeBytes + conv2.Weight.Buffer.SizeBytes
                + conv1.Bias!.Buffer.SizeBytes + conv2.Bias!.Buffer.SizeBytes
                + audioPositions.Buffer.SizeBytes + encodedNorm.Buffer.SizeBytes;
            // Q/K/V/out and the two MLP matrices have 12 width-squared entries.
            // Norms and biases add 12 width-sized FP32 vectors.
            int blockBytes = 12 * Width * Width * (halfProjections ? 2 : 4) + 12 * Width * sizeof(float);
            int streamingSlots = prefetchEncoder ? 2 : 1;
            int slots = Layers;
            if (encoderBudgetMiB is { } budget)
            {
                long available = (long)budget * 1024 * 1024 - (long)fixedBytes;
                if (available < blockBytes * streamingSlots)
                {
                    throw new ArgumentOutOfRangeException(nameof(encoderBudgetMiB),
                        $"Encoder needs at least {fixedBytes + (ulong)(blockBytes * streamingSlots)} weight bytes.");
                }
                slots = (int)Math.Min(Layers, available / blockBytes);
            }
            PinnedBlocks = slots == Layers ? Layers : slots - streamingSlots;
            MaximumUploadBytes = slots == Layers ? 0 : (ulong)blockBytes;
            EncoderWeightBytes = fixedBytes + (ulong)(slots * blockBytes);
            List<GpuTensor>[] deviceSlots = [[], []];
            for (int layer = 0; layer < Layers; layer++)
            {
                string prefix = $"encoder.blocks.{layer}";
                if (checkpointBlocks is not null)
                {
                    checkpointBlocks[layer] = CheckpointEncoderBlock.Bind(weights, prefix, Norm);
                }
                else
                {
                    if (layer >= PinnedBlocks)
                    {
                        stream = new(deviceSlots[layer % streamingSlots], blockBytes);
                    }
                    encoder[layer] = LoadBlock(prefix, cross: false);
                    uploads[layer] = stream?.Finish();
                    stream = null;
                }
                decoder[layer] = LoadBlock($"decoder.blocks.{layer}", cross: true);
                selfCache[layer] = AllocateCache(TextRows);
                crossCache[layer] = AllocateCache(AudioRows);
            }
            if (checkpointBlocks is not null)
            {
                PinnedBlocks = 0;
                // All norms remain resident. The widest tile is the MLP down matrix.
                var largest = checkpointBlocks[0].Down.Plan;
                ulong tileWeights = 2 * largest.WeightBufferBytes + (ulong)(2 * largest.InputWidth * largest.TileRows);
                EncoderWeightBytes = fixedBytes + (ulong)(Layers * 4 * Width * sizeof(float)) + tileWeights;
                math.StreamController = tileController;
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        GpuTensor Upload(int rows, int columns, float[] values, GpuTensorFormat format = GpuTensorFormat.Float32)
        {
            if (stream is not null)
            {
                return stream.Add(rows, columns, format, values, Allocate);
            }
            GpuTensor tensor = Allocate(rows, columns, format);
            math.Upload(tensor, values);
            return tensor;
        }

        GpuTensor Allocate(int rows, int columns, GpuTensorFormat format = GpuTensorFormat.Float32)
        {
            GpuTensor tensor = rows == Vocabulary
                ? math.CreateSparseTensor(rows, columns, format)
                : math.CreateTensor(rows, columns, format);
            resident.Add(tensor);
            return tensor;
        }

        Cache AllocateCache(int rows) => new(Allocate(rows, Width), Allocate(rows, Width));

        GpuTensor Norm(string name) => Upload(1, 2 * Width,
            [.. weights.Read(name + ".weight", Width), .. weights.Read(name + ".bias", Width)]);

        Linear Affine(string name, int inputWidth, int outputWidth, bool bias = true)
        {
            float[] source = weights.Read(name + ".weight", inputWidth, outputWidth);
            bool encoderProjection = name.StartsWith("encoder.", StringComparison.Ordinal)
                || name.EndsWith(".cross_attn.key", StringComparison.Ordinal)
                || name.EndsWith(".cross_attn.value", StringComparison.Ordinal);
            GpuTensor weight;
            if (halfProjections && encoderProjection)
            {
                var transposed = new float[source.Length];
                for (int row = 0; row < outputWidth; row++)
                {
                    for (int col = 0; col < inputWidth; col++)
                    {
                        transposed[col * outputWidth + row] = source[row * inputWidth + col];
                    }
                }
                weight = Upload(inputWidth, outputWidth, transposed, GpuTensorFormat.PackedFloat16);
            }
            else
            {
                weight = Upload(outputWidth, inputWidth, source);
            }
            return new(weight, bias ? Upload(1, outputWidth, weights.Read(name + ".bias", outputWidth)) : null);
        }

        Attention LoadAttention(string name) => new(Norm(name + "_ln"),
            Affine(name + ".query", Width, Width),
            Affine(name + ".key", Width, Width, bias: false),
            Affine(name + ".value", Width, Width),
            Affine(name + ".out", Width, Width));

        Block LoadBlock(string name, bool cross) => new(LoadAttention(name + ".attn"),
            new(Norm(name + ".mlp_ln"), Affine(name + ".mlp.0", Width, Width * 4),
                Affine(name + ".mlp.2", Width * 4, Width)),
            cross ? LoadAttention(name + ".cross_attn") : null);

        Linear Convolution(string name, int channels)
        {
            float[] source = weights.Read(name + ".weight", 3, channels, Width);
            var kernel = new float[source.Length];
            for (int output = 0; output < Width; output++)
            {
                for (int input = 0; input < channels; input++)
                {
                    for (int tap = 0; tap < 3; tap++)
                    {
                        kernel[(tap * channels + input) * Width + output] =
                            source[(output * channels + input) * 3 + tap];
                    }
                }
            }
            GpuTensor weight = math.CreateTensor(3 * channels, Width,
                cooperativeConvolution ? GpuTensorFormat.PackedFloat16 : GpuTensorFormat.Float32);
            resident.Add(weight);
            math.Upload(weight, kernel);
            return new(weight, Upload(1, Width, weights.Read(name + ".bias", 1, Width)));
        }
    }

    public InferenceSample Transcribe(float[] mel, int maximumTokens, BenchmarkQueue? queue)
    {
        long managedStart = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        var metrics = new InferenceMetrics(queue is not null, ProfileGpu || ProfileStream);
        int readbacks = math.ReadbackCount;
        ulong allocations = math.MemoryTelemetry.AllocationCount;
        using var requestScope = queue?.OpenRequestScope();
        var scope = requestScope ?? queue?.Retained;
        var initialMetrics = scope?.Metrics;
        var timer = Stopwatch.StartNew();
        List<QueuedDispatchProfile>? profiles = ProfileAttention ? new(Layers) : null;
        List<PrefetchTiming>? copyTimings = (ProfileGpu || ProfileStream) && prefetchEncoder ? [] : null;
        tileController?.Begin(ProfileStream);
        Encode(mel, metrics, profiles, scope, copyTimings);
        double encode = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        Evaluate(StartOfText, 0, metrics);
        int next = Evaluate(NoTimestamps, 1, metrics);
        double prefill = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var tokens = new List<int>(maximumTokens);
        while (true)
        {
            tokens.Add(next);
            if (next == EndOfText || tokens.Count == maximumTokens)
            {
                break;
            }
            next = Evaluate(next, tokens.Count + 1, metrics);
        }
        double decode = timer.Elapsed.TotalMilliseconds;
        requestScope?.Dispose();
        var queueMetrics = QueueSample.Between(initialMetrics, scope?.Metrics);
        return new(total.Elapsed.TotalMilliseconds, encode, prefill, decode,
            tokens, math.ReadbackCount - readbacks, math.MemoryTelemetry.AllocationCount - allocations,
            GC.GetAllocatedBytesForCurrentThread() - managedStart, metrics.Operations, queueMetrics,
            profiles?.Select(profile => EncoderAttentionGpuTiming.From(profile.DispatchMilliseconds, halfAttention)).ToArray(),
            ProfileGpu ? metrics.CollectProfiles() : null, metrics.Profiles?.Where(item => item.Operation == "encoder-qkv")
                .Select(item => new EncoderUploadTiming(item.Profile.UploadBytes, item.Profile.StagingMs,
                    item.Profile.CopyMs, item.Profile.PreComputeGapMs, item.Profile.CopyInGapMs)).ToArray(),
            copyTimings, metrics.Profiles?.Where(item => item.Operation.StartsWith("encoder-", StringComparison.Ordinal))
                .Select(item => new EncoderBatchInterval(item.Operation, item.Profile.ComputeInterval, item.Profile.TransferInterval)).ToArray(), tileController?.Sample);
    }

    private void Encode(float[] mel, InferenceMetrics metrics, List<QueuedDispatchProfile>? profiles,
        VulkanComputeMath.QueuedComputeScope? scope, List<PrefetchTiming>? copyTimings)
    {
        using var scratch = new ScratchScope(math, scratchPool);
        var audio = scratch.Create(AudioRows * 2, MelBins);
        var first = scratch.Create(AudioRows * 2, Width);
        var firstColumns = scratch.Create(first.Rows, MelBins * 3);
        var second = scratch.Create(AudioRows, Width);
        var secondColumns = scratch.Create(second.Rows, Width * 3);
        var hidden = scratch.Create(AudioRows, Width);

        var timer = Stopwatch.StartNew();
        math.Upload(audio, mel, reuseUpload);
        metrics.RecordTransfer("mel-upload", timer.Elapsed.TotalMilliseconds);
        var batch = math.CreateBatch();
        if (!CopyAtUse)
        {
            Stage(0);
        }
        Convolve(batch, audio, conv1, firstColumns, first, stride: 1);
        Convolve(batch, first, conv2, secondColumns, second, stride: 2);
        batch.Activate(second, hidden, TensorActivation.Residual, audioPositions);
        Execute(batch, metrics, "encoder-convolution");

        for (int layer = 0; layer < Layers; layer++)
        {
            Stage(CopyAtUse ? layer : layer + 1);
            if (prefetchEncoder && uploads[layer] is not null)
            {
                scope!.JoinPrefetch(layer % 2);
            }
            EncodeBlock(hidden, encoder[layer], checkpointBlocks?[layer], metrics, profiles, prefetchEncoder ? null : uploads[layer]);
        }
        batch = math.CreateBatch();
        batch.Normalize(hidden, encodedNorm, hidden, Normalization.LayerNorm);
        for (int layer = 0; layer < Layers; layer++)
        {
            Attention cross = decoder[layer].Cross!;
            Project(batch, hidden, cross.Key, crossCache[layer].Key);
            Project(batch, hidden, cross.Value, crossCache[layer].Value);
        }
        Execute(batch, metrics, "encoder-cross-cache");
        if (CaptureDirectory is not null)
        {
            Capture("encoder.f32", math.Read(hidden));
        }

        void Stage(int layer)
        {
            if (prefetchEncoder && layer < Layers && uploads[layer] is { } plan)
            {
                var timing = scope!.Prefetch(plan, layer % 2, copyTimings is not null);
                if (timing is not null)
                {
                    copyTimings!.Add(timing);
                }
            }
        }
    }

    private void EncodeBlock(GpuTensor hidden, Block? block, CheckpointEncoderBlock? tiles, InferenceMetrics metrics, List<QueuedDispatchProfile>? profiles,
        ComputeBatchUpload? upload)
    {
        using var scratch = new ScratchScope(math, scratchPool);
        var normalized = scratch.Create(hidden.Rows, Width);
        var query = scratch.Create(hidden.Rows, Width);
        var key = scratch.Create(hidden.Rows, Width);
        var value = scratch.Create(hidden.Rows, Width);
        var attention = scratch.Create(hidden.Rows, Width);
        var projected = scratch.Create(hidden.Rows, Width);
        var expanded = scratch.Create(hidden.Rows, 4 * Width);
        var batch = math.CreateBatch();
        batch.Normalize(hidden, tiles?.AttentionNorm ?? block!.Self.Norm, normalized, Normalization.LayerNorm);
        if (tiles is not null)
        {
            Execute(batch, metrics, "encoder-attention-norm");
            tiles.Query.Run(math, normalized, query);
            tiles.Key.Run(math, normalized, key);
            tiles.Value.Run(math, normalized, value);
        }
        else
        {
            Project(batch, normalized, block!.Self.Query, query);
            Project(batch, normalized, block.Self.Key, key);
            Project(batch, normalized, block.Self.Value, value);
            Execute(batch, metrics, "encoder-qkv", upload);
        }
        if (fusedAttention)
        {
            var packedKeyValue = scratch.Create(2 * key.Rows, Width);
            batch = math.CreateBatch();
            batch.Scatter(key, packedKeyValue);
            batch.Scatter(value, packedKeyValue, firstRow: key.Rows);
            if (halfAttention)
            {
                var halfQuery = AlignedScratch(query, GpuTensorFormat.PackedFloat16);
                var halfKeyValue = AlignedScratch(packedKeyValue, GpuTensorFormat.PackedFloat16);
                batch.PackMatrixHalf(query, halfQuery);
                batch.PackMatrixHalf(packedKeyValue, halfKeyValue);
                if (CooperativeAttention)
                {
                    batch.CooperativeAttention64(halfQuery, halfKeyValue, attention, key.Rows, AttentionKeyTile);
                }
                else
                {
                    batch.FusedAttention64Packed(halfQuery, halfKeyValue, attention, key.Rows);
                }
            }
            else
            {
                batch.FusedAttention64(query, packedKeyValue, attention);
            }
            if (profiles is null)
            {
                Execute(batch, metrics, "encoder-attention");
            }
            else
            {
                var timer = Stopwatch.StartNew();
                profiles.Add(math.EnqueueWithDispatchTimings(batch));
                metrics.Record("encoder-attention", timer.Elapsed.TotalMilliseconds, null, 1, batch.DispatchCount, 0);
            }
        }
        else
        {
            Attend(query, new(key, value), attention, metrics, "encoder-attention", encoderWorkspace);
        }
        batch = math.CreateBatch();
        if (tiles is not null)
        {
            tiles.Output.Run(math, attention, projected);
            batch.Activate(hidden, hidden, TensorActivation.Residual, projected);
            batch.Normalize(hidden, tiles.MlpNorm, normalized, Normalization.LayerNorm);
            Execute(batch, metrics, "encoder-output-norm");
            tiles.Up.Run(math, normalized, expanded);
            batch = math.CreateBatch();
            batch.Activate(expanded, expanded, TensorActivation.Gelu);
            Execute(batch, metrics, "encoder-mlp-gelu");
            tiles.Down.Run(math, expanded, projected);
            batch = math.CreateBatch();
            batch.Activate(hidden, hidden, TensorActivation.Residual, projected);
            Execute(batch, metrics, "encoder-mlp-residual");
            return;
        }
        ProjectResidual(batch, hidden, attention, block!.Self.Output, projected);
        FeedForward(batch, hidden, block.Mlp, normalized, expanded, projected);
        Execute(batch, metrics, "encoder-output-mlp");
    }

    private int Evaluate(int token, int position, InferenceMetrics metrics)
    {
        using var allocations = new ScratchScope(math, scratchPool);
        var hidden = allocations.Create(1, Width);
        var scratch = new DecoderScratch(allocations, position + 1);
        var batch = math.CreateBatch();
        batch.Gather(embedding, hidden, firstRow: token);
        batch.Gather(textPositions, scratch.Normalized, firstRow: position);
        batch.Activate(hidden, hidden, TensorActivation.Residual, scratch.Normalized);
        for (int layer = 0; layer < Layers; layer++)
        {
            Block block = decoder[layer];
            Cache cache = selfCache[layer];
            batch.Normalize(hidden, block.Self.Norm, scratch.Normalized, Normalization.LayerNorm);
            Project(batch, scratch.Normalized, block.Self.Query, scratch.Query);
            Project(batch, scratch.Normalized, block.Self.Key, scratch.Key);
            Project(batch, scratch.Normalized, block.Self.Value, scratch.Value);
            batch.Scatter(scratch.Key, cache.Key, firstRow: position);
            batch.Scatter(scratch.Value, cache.Value, firstRow: position);
            batch.Scale(scratch.Query, scratch.Query, 1f / MathF.Sqrt(HeadWidth));
            batch.GroupedAttentionScores(scratch.Query, cache.Key, scratch.Scores,
                Heads, Heads, HeadWidth, position, position + 1);
            batch.ReduceRows(scratch.Scores, scratch.Probabilities);
            batch.GroupedAttentionValues(scratch.Probabilities, cache.Value, scratch.Attended,
                Heads, Heads, HeadWidth);
            ProjectResidual(batch, hidden, scratch.Attended, block.Self.Output, scratch.Projected);

            Attention cross = block.Cross!;
            batch.Normalize(hidden, cross.Norm, scratch.Normalized, Normalization.LayerNorm);
            Project(batch, scratch.Normalized, cross.Query, scratch.Query);
            if (batchCross)
            {
                RecordCrossAttention(batch, scratch.Query, crossCache[layer], scratch.Attended);
            }
            else
            {
                Execute(batch, metrics, "decoder-blocks");
                AttendToCrossCache(scratch.Query, crossCache[layer], scratch.Attended, metrics);
                batch = math.CreateBatch();
            }
            ProjectResidual(batch, hidden, scratch.Attended, cross.Output, scratch.Projected);
            FeedForward(batch, hidden, block.Mlp, scratch.Normalized, scratch.Expanded, scratch.Projected);
        }
        if (!joinLogits)
        {
            Execute(batch, metrics, "decoder-blocks");
            batch = math.CreateBatch();
        }
        var logits = allocations.Create(1, Vocabulary);
        batch.Normalize(hidden, decodedNorm, scratch.Normalized, Normalization.LayerNorm);
        if (packedVocabulary is null)
        {
            MultiplyProjection(batch, scratch.Normalized, embedding, logits);
        }
        else
        {
            batch.MultiplyVector(scratch.Normalized, packedVocabulary, logits);
        }
        Execute(batch, metrics, joinLogits ? "decoder-body-and-logits" : "decoder-logits");
        var timer = Stopwatch.StartNew();
        float[] values;
        if (hostLogits is { } destination)
        {
            math.ReadInto(logits, destination);
            values = destination;
        }
        else
        {
            values = math.Read(logits, reuseReadback);
        }
        metrics.RecordTransfer("decoder-logits-readback", timer.Elapsed.TotalMilliseconds);
        if (CaptureDirectory is not null)
        {
            Capture($"logits-{position:D3}.f32", values);
        }
        int next = 0;
        for (int index = 0; index <= EndOfText; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                throw new InvalidDataException("Whisper decoder produced non-finite logits.");
            }
            if (values[index] > values[next])
            {
                next = index;
            }
        }
        return next;
    }

    private void Capture(string name, float[] values)
    {
        using var stream = File.Create(Path.Combine(CaptureDirectory!, name));
        stream.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private GpuTensor AlignedScratch(GpuTensor source, GpuTensorFormat format)
    {
        int rows = (source.Rows + 15) / 16 * 16;
        var key = (rows, source.Columns, format);
        if (!projectionScratch.TryGetValue(key, out var result))
        {
            result = math.CreateTensor(rows, source.Columns, format);
            resident.Add(result);
            projectionScratch.Add(key, result);
        }
        return result;
    }

    private void Convolve(ComputeMathBatch batch, GpuTensor input, Linear weights,
        GpuTensor columns, GpuTensor output, int stride)
    {
        if (weights.Weight.Format == GpuTensorFormat.PackedFloat16)
        {
            batch.Im2Col1D(input, columns, kernelWidth: 3, padding: 1, stride);
            var packed = AlignedScratch(columns, GpuTensorFormat.PackedFloat16);
            var product = AlignedScratch(output, GpuTensorFormat.Float32);
            batch.PackMatrixHalf(columns, packed);
            batch.ProjectCooperativeTiles(packed, weights.Weight, product, reuseTiles: true);
            batch.Gather(product, output);
        }
        else
        {
            batch.Convolve1D(input, weights.Weight, columns, output, kernelWidth: 3, padding: 1, stride);
        }
        batch.Activate(output, output, TensorActivation.Bias, weights.Bias);
        batch.Activate(output, output, TensorActivation.Gelu);
    }

    private void MultiplyProjection(ComputeMathBatch batch, GpuTensor input, GpuTensor weight, GpuTensor output)
    {
        if (weight.Format == GpuTensorFormat.PackedFloat16)
        {
            var operand = AlignedScratch(input, GpuTensorFormat.PackedFloat16);
            var product = AlignedScratch(output, GpuTensorFormat.Float32);
            batch.PackMatrixHalf(input, operand);
            if (DirectProjectionTiles)
            {
                batch.ProjectCooperativeTiles(operand, weight, product, ReuseProjectionTiles);
            }
            else
            {
                batch.MultiplyCooperative(operand, weight, product);
            }
            batch.Gather(product, output);
        }
        else
        {
            batch.ProjectDense(input, weight, output, useVectorKernel: vectorProjection);
        }
    }

    private void Project(ComputeMathBatch batch, GpuTensor input, Linear layer, GpuTensor output)
    {
        MultiplyProjection(batch, input, layer.Weight, output);
        if (layer.Bias is { } bias)
        {
            batch.Activate(output, output, TensorActivation.Bias, bias);
        }
    }

    private void ProjectResidual(ComputeMathBatch batch, GpuTensor hidden, GpuTensor input,
        Linear layer, GpuTensor projected)
    {
        if (FuseProjectionEpilogues && layer.Bias is { } bias)
        {
            MultiplyProjection(batch, input, layer.Weight, projected);
            batch.Activate(projected, hidden, TensorActivation.BiasResidual, bias);
            return;
        }
        Project(batch, input, layer, projected);
        batch.Activate(hidden, hidden, TensorActivation.Residual, projected);
    }

    private void FeedForward(ComputeMathBatch batch, GpuTensor hidden, Mlp mlp,
        GpuTensor normalized, GpuTensor expanded, GpuTensor projected)
    {
        batch.Normalize(hidden, mlp.Norm, normalized, Normalization.LayerNorm);
        if (FuseProjectionEpilogues && mlp.Up.Bias is { } bias)
        {
            MultiplyProjection(batch, normalized, mlp.Up.Weight, expanded);
            batch.Activate(expanded, expanded, TensorActivation.BiasGelu, bias);
        }
        else
        {
            Project(batch, normalized, mlp.Up, expanded);
            batch.Activate(expanded, expanded, TensorActivation.Gelu);
        }
        ProjectResidual(batch, hidden, expanded, mlp.Down, projected);
    }

    private void Execute(ComputeMathBatch batch, InferenceMetrics metrics, string operation, ComputeBatchUpload? upload = null)
    {
        var timer = Stopwatch.StartNew();
        if (metrics.Profiles is { } profiles && (ProfileGpu || operation.StartsWith("encoder-", StringComparison.Ordinal)))
        {
            profiles.Add((operation, math.EnqueueWithDispatchTimings(batch, upload, timeDispatches: ProfileGpu)));
            metrics.Record(operation, timer.Elapsed.TotalMilliseconds, null, 1, batch.DispatchCount, 0);
            return;
        }
        ComputeMathResult result = upload is null ? math.Execute(batch) : math.EnqueueWithUpload(batch, upload);
        metrics.Record(operation, timer.Elapsed.TotalMilliseconds, result.GpuMilliseconds, 1, result.Dispatches, 1);
    }

    private void AttendToCrossCache(GpuTensor query, Cache cache, GpuTensor output, InferenceMetrics metrics)
    {
        const string operation = "decoder-cross-attention";
        if (crossScores is not null)
        {
            var batch = math.CreateBatch();
            RecordCrossAttention(batch, query, cache, output);
            Execute(batch, metrics, operation);
        }
        else
        {
            Attend(query, cache, output, metrics, operation, crossWorkspace);
        }
    }

    private void RecordCrossAttention(ComputeMathBatch batch, GpuTensor query, Cache cache, GpuTensor output)
    {
        batch.AttendToCache(query, cache.Key, cache.Value, crossScores!, crossProbabilities!, output, HeadWidth);
    }

    private void Attend(GpuTensor query, Cache cache, GpuTensor output, InferenceMetrics metrics, string operation,
        VulkanComputeMath.TiledAttentionWorkspace? workspace = null)
    {
        var timer = Stopwatch.StartNew();
        TiledAttentionResult result = math.AttentionTiled(query, cache.Key, cache.Value, output,
            headWidth: HeadWidth, queryTileRows: math.AttentionQueryTileRows, roundOutput: false,
            batchAcrossHeads: batchAcrossHeads, workspace: workspace);
        metrics.Record(operation, timer.Elapsed.TotalMilliseconds, result.GpuMilliseconds,
            result.Submissions, result.Dispatches, result.CompletionWaits);
    }

    public void Dispose()
    {
        if (tileController is not null && ReferenceEquals(math.StreamController, tileController))
        {
            math.StreamController = previousController;
        }
        if (scratchPool is not null)
        {
            foreach (var available in scratchPool.Values)
            {
                foreach (var tensor in available)
                {
                    tensor.Dispose();
                }
            }
            scratchPool.Clear();
        }
        encoderWorkspace?.Dispose();
        crossWorkspace?.Dispose();
        foreach (GpuTensor tensor in resident)
        {
            tensor.Dispose();
        }
        resident.Clear();
    }

    private sealed class DecoderScratch(ScratchScope scope, int cacheRows)
    {
        public GpuTensor Normalized { get; } = scope.Create(1, Width);
        public GpuTensor Query { get; } = scope.Create(1, Width);
        public GpuTensor Key { get; } = scope.Create(1, Width);
        public GpuTensor Value { get; } = scope.Create(1, Width);
        public GpuTensor Attended { get; } = scope.Create(1, Width);
        public GpuTensor Projected { get; } = scope.Create(1, Width);
        public GpuTensor Expanded { get; } = scope.Create(1, Width * 4);
        public GpuTensor Scores { get; } = scope.Create(Heads, cacheRows);
        public GpuTensor Probabilities { get; } = scope.Create(Heads, cacheRows);
    }

    // This benchmark executes synchronously. Each scope exclusively holds its
    // tensors until its final submission completes, including nested scopes.
    private sealed class ScratchScope(VulkanComputeMath math,
        Dictionary<(int Rows, int Columns), Stack<GpuTensor>>? pool) : IDisposable
    {
        private readonly List<GpuTensor> tensors = [];

        public GpuTensor Create(int rows, int columns)
        {
            if (pool is null || !pool.TryGetValue((rows, columns), out var available) || !available.TryPop(out var tensor))
            {
                tensor = math.CreateTensor(rows, columns);
            }
            tensors.Add(tensor);
            return tensor;
        }

        public void Dispose()
        {
            for (int index = tensors.Count - 1; index >= 0; index--)
            {
                GpuTensor tensor = tensors[index];
                if (pool is null)
                {
                    tensor.Dispose();
                }
                else
                {
                    var shape = (tensor.Rows, tensor.Columns);
                    if (!pool.TryGetValue(shape, out var available))
                    {
                        available = new();
                        pool.Add(shape, available);
                    }
                    available.Push(tensor);
                }
            }
            tensors.Clear();
        }
    }
}
