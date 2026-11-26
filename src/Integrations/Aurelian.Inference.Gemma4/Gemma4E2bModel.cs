using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Gemma4;

public sealed record Gemma4StageProfile(double WallMs, double GpuMs, double InputMs, int Submissions, int Dispatches)
{
    public IReadOnlyDictionary<string, double>? ShaderMs { get; init; }
    public IReadOnlyDictionary<string, double>? FeedForwardMs { get; init; }
    public IReadOnlyList<ComputeBatchCpuTimings>? BatchCpu { get; init; }
    public double? InputDecodeMs { get; init; }
    public double? ReadbackMs { get; init; }
    public int InputSubmissions { get; init; }
}

/// <summary>Single-sequence text inference for the Gemma 4 E2B GGUF Q4_0/Q6_K profile.</summary>
public sealed class Gemma4E2bModel : ICausalTextModel, IDisposable
{
    public const int VocabularySize = 262144;
    private const int Width = 1536;
    private const int LayerCount = 35;
    private const int KvLayers = 15;
    private const int Heads = 8;
    private const int LayerInput = 256;
    private const int AllLayerInputs = LayerCount * LayerInput;
    private const int HeadUploadRows = 32768;
    private const int LogitStorageColumns = 65536;
    private const string Tokens = "token_embd.weight";
    private const string PerLayerTokens = "per_layer_token_embd.weight";
    private readonly VulkanComputeMath compute;
    private readonly GgufCheckpoint checkpoint;
    private readonly List<GpuTensor> owned = [];
    private readonly Layer[] layers = new Layer[LayerCount];
    private readonly GpuTensor[] headWeights;
    private readonly GpuTensor outputNorm, layerInputProjection, layerInputNorm, onesLocal, onesGlobal, ropeFactors;
    private readonly GpuTensor headInput, headSlice, logitStorage, argmaxPartials, argmaxOutput;
    private readonly byte[] tokenBlock = new byte[GgufCheckpoint.RowBytes(GgufTensorType.Q6_K, Width)];
    private readonly byte[] layerTokenBlock = new byte[GgufCheckpoint.RowBytes(GgufTensorType.Q6_K, AllLayerInputs)];
    private readonly float[] logitReadback = new float[VocabularySize];
    private Scratch? decodeScratch, promptScratch;
    private VulkanComputeMath.PreparedComputeBatch? tokenReplay;
    private bool disposed;

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity = 640)
        : this(compute, checkpoint, capacity, PackedQ4Layout.RowMajor)
    {
    }

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity, PackedQ4Layout q4Layout)
        : this(compute, checkpoint, capacity, q4Layout, fuseKeyCache: false)
    {
    }

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity,
        PackedQ4Layout q4Layout, bool fuseKeyCache)
        : this(compute, checkpoint, capacity, q4Layout, fuseKeyCache, PackedQ6Layout.RowMajor, HeadUploadRows)
    {
    }

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity,
        PackedQ4Layout q4Layout, bool fuseKeyCache, PackedQ6Layout headLayout, int headShardRows)
        : this(compute, checkpoint, capacity, q4Layout, fuseKeyCache, headLayout, headShardRows, fuseGateUp: false)
    {
    }

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity,
        PackedQ4Layout q4Layout, bool fuseKeyCache, PackedQ6Layout headLayout, int headShardRows, bool fuseGateUp)
        : this(compute, checkpoint, capacity, q4Layout, fuseKeyCache, headLayout, headShardRows, fuseGateUp, fuseQueryRope: false)
    {
    }

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity,
        PackedQ4Layout q4Layout, bool fuseKeyCache, PackedQ6Layout headLayout, int headShardRows, bool fuseGateUp,
        bool fuseQueryRope)
    {
        ArgumentNullException.ThrowIfNull(compute);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (capacity is < 1 or > 1024 || compute.MaximumDispatches < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "E2B requires 1–1024 positions and at least 64 dispatches per batch.");
        }
        ValidateProfile(checkpoint);
        this.compute = compute;
        this.checkpoint = checkpoint;
        ContextCapacity = capacity;
        if (!Enum.IsDefined(q4Layout)) { throw new ArgumentOutOfRangeException(nameof(q4Layout)); }
        if (fuseGateUp && q4Layout != PackedQ4Layout.Interleaved8)
        {
            throw new ArgumentException("Fused gate/up requires interleaved Q4 weights.", nameof(fuseGateUp));
        }
        if (!Enum.IsDefined(headLayout)) { throw new ArgumentOutOfRangeException(nameof(headLayout)); }
        if (headShardRows is not (HeadUploadRows or LogitStorageColumns))
        {
            throw new ArgumentOutOfRangeException(nameof(headShardRows));
        }
        Q4Layout = q4Layout;
        FuseKeyCache = fuseKeyCache;
        FuseQueryRope = fuseQueryRope;
        FuseGateUp = fuseGateUp;
        HeadLayout = headLayout;
        HeadShardRows = headShardRows;
        headWeights = new GpuTensor[VocabularySize / HeadShardRows];
        try
        {
            outputNorm = Vector("output_norm.weight", Width);
            layerInputNorm = Vector("per_layer_proj_norm.weight", LayerInput, 1 / MathF.Sqrt(2));
            ropeFactors = Vector("rope_freqs.weight", 256);
            onesLocal = VectorOnes(256);
            onesGlobal = VectorOnes(512);
            checkpoint.RequireTensor("per_layer_model_proj.weight", GgufTensorType.Float16, Width, AllLayerInputs);
            var denseBytes = new byte[Width * AllLayerInputs * 2];
            checkpoint.ReadRows("per_layer_model_proj.weight", 0, AllLayerInputs, denseBytes);
            float[] dense = new float[Width * AllLayerInputs];
            ReadOnlySpan<Half> half = MemoryMarshal.Cast<byte, Half>(denseBytes);
            for (int index = 0; index < dense.Length; index++) { dense[index] = (float)half[index]; }
            layerInputProjection = Own(compute.CreateSparseTensor(AllLayerInputs, Width));
            compute.Upload(layerInputProjection, dense, reuseStaging: true);

            float[]? fusedFactors = FuseKeyCache || FuseQueryRope ? ReadVector("rope_freqs.weight", 256) : null;
            for (int index = 0; index < layers.Length; index++)
            {
                layers[index] = LoadLayer(index, fusedFactors);
            }
            LoadHeadWeights();
            headInput = Own(compute.CreateTensor(1, Width));
            headSlice = Own(compute.CreateTensor(1, HeadShardRows));
            logitStorage = Own(compute.CreateTensor(VocabularySize / LogitStorageColumns, LogitStorageColumns));
            argmaxPartials = Own(compute.CreateTensor(VocabularySize / 1024, 2));
            argmaxOutput = Own(compute.CreateTensor(1, 2));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int ContextCapacity { get; }
    public static int InputUploadBytesPerToken => (Width + AllLayerInputs) * sizeof(float);
    public PackedQ4Layout Q4Layout { get; }
    public PackedQ6Layout HeadLayout { get; }
    public int HeadShardRows { get; }
    public bool FuseKeyCache { get; }
    public bool FuseGateUp { get; }
    public int DownProjectionTiles { get; init; }
    public bool MergeDownNormalization { get; init; }
    public int Position { get; private set; }
    public bool LayerSubmissions { get; init; }
    public bool PruneSharedRows { get; init; } = true;
    public bool PackedPrefill { get; init; } = true;
    public bool PackedDecode { get; init; } = true;
    public int PackedDecodeLanes { get; init; } = 16;
    public bool CooperativePrefill { get; init; } = true;
    public bool ReusePrefillInputPacking { get; init; } = true;
    public bool ProfileDispatches { get; init; }
    public bool BatchInputUpload { get; init; }
    public int FusedDecodeMaxKeys { get; init; } = 128;
    public int SplitDecodeTileKeys { get; init; }
    public bool FuseQueryRope { get; }
    public bool FusedPrefillAttention { get; init; } = true;
    public bool WidePrefillKeyTile { get; init; } = true;
    public bool BoundedNorm { get; init; } = true;
    public bool SpecializeDecodeNorm { get; init; }
    public bool DeviceHead { get; init; } = true;
    public Gemma4StageProfile? BodyProfile { get; private set; }
    public Gemma4StageProfile? HeadProfile { get; private set; }
    public double? ReplayPreparationMs { get; private set; }
    public int ReplayedTokens { get; private set; }

    private void LoadHeadWeights()
    {
        int rowWords = tokenBlock.Length / sizeof(uint);
        byte[] packed = new byte[HeadUploadRows * tokenBlock.Length];
        byte[]? interleaved = HeadLayout == PackedQ6Layout.Interleaved8 ? new byte[packed.Length] : null;
        for (int shard = 0; shard < headWeights.Length; shard++)
        {
            var weights = Own(compute.CreateSparseTensor(HeadShardRows, rowWords, GpuTensorFormat.UInt32));
            headWeights[shard] = weights;
            for (int row = 0; row < HeadShardRows; row += HeadUploadRows)
            {
                checkpoint.ReadRows(Tokens, shard * HeadShardRows + row, HeadUploadRows, packed);
                if (interleaved is not null)
                {
                    GgufBlocks.RepackQ6_KInterleaved8(packed, MemoryMarshal.Cast<byte, uint>(interleaved.AsSpan()), Width);
                }
                compute.UploadUInt32Region(weights, interleaved ?? packed, row * rowWords);
            }
        }
    }

    public static void ValidateProfile(GgufCheckpoint file)
    {
        if (file.Metadata<string>("general.architecture") != "gemma4"
            || file.Metadata<uint>("gemma4.block_count") != LayerCount
            || file.Metadata<uint>("gemma4.embedding_length") != Width
            || file.Metadata<uint>("gemma4.embedding_length_per_layer_input") != LayerInput
            || file.Metadata<uint>("gemma4.attention.shared_kv_layers") != LayerCount - KvLayers
            || file.Metadata<uint>("gemma4.attention.head_count") != Heads
            || file.Metadata<uint>("gemma4.attention.head_count_kv") != 1
            || file.Metadata<uint>("gemma4.attention.key_length") != 512
            || file.Metadata<uint>("gemma4.attention.value_length") != 512
            || file.Metadata<uint>("gemma4.attention.key_length_swa") != 256
            || file.Metadata<uint>("gemma4.attention.value_length_swa") != 256
            || file.Metadata<uint>("gemma4.attention.sliding_window") != 512
            || file.Metadata<float>("gemma4.rope.freq_base") != 1000000
            || file.Metadata<float>("gemma4.rope.freq_base_swa") != 10000
            || file.Metadata<float>("gemma4.attention.layer_norm_rms_epsilon") != 1e-6f
            || file.Metadata<float>("gemma4.final_logit_softcapping") != 30
            || !file.Metadata<bool[]>("gemma4.attention.sliding_window_pattern")
                .SequenceEqual(Enumerable.Range(0, LayerCount).Select(index => index % 5 != 4))
            || !file.Metadata<int[]>("gemma4.feed_forward_length")
                .SequenceEqual(Enumerable.Range(0, LayerCount).Select(index => index < KvLayers ? 6144 : 12288)))
        {
            throw new InvalidDataException("GGUF does not match the Gemma 4 E2B text profile.");
        }
        file.RequireTensor(Tokens, GgufTensorType.Q6_K, Width, VocabularySize);
        file.RequireTensor(PerLayerTokens, GgufTensorType.Q6_K, AllLayerInputs, VocabularySize);
    }

    public GpuTensor Evaluate(IReadOnlyList<int> tokens)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Q4Layout != PackedQ4Layout.RowMajor && (!PackedPrefill || !PackedDecode || PackedDecodeLanes != 16))
        {
            throw new InvalidOperationException("Interleaved weights require packed prefill and 16-lane packed decode.");
        }
        if (DownProjectionTiles is not (0 or 1 or 2 or 4) || (MergeDownNormalization && DownProjectionTiles < 2)
            || (DownProjectionTiles != 0 && Q4Layout != PackedQ4Layout.Interleaved8))
        {
            throw new InvalidOperationException("Tiled down projection requires interleaved Q4 weights and one, two or four tiles.");
        }
        int rows = tokens.Count;
        if (rows == 0 || rows > ContextCapacity - Position || tokens.Any(id => id is < 0 or >= VocabularySize))
        {
            throw new ArgumentException("E2B token IDs and positions must fit the vocabulary and context.", nameof(tokens));
        }
        long start = Stopwatch.GetTimestamp();
        int end = Position + rows;
        bool prune = PruneSharedRows && rows > 1;
        var initial = GetScratch(rows);
        var final = prune ? GetScratch(1) : initial;
        var result = compute.CreateTensor(final.Rows, Width);
        try
        {
            long decodeStart = compute.MeasureBatchCpuTime ? Stopwatch.GetTimestamp() : 0;
            for (int row = 0; row < rows; row++)
            {
                ReadEmbedding(tokens[row], initial, row);
            }
            double? inputDecodeMs = decodeStart == 0 ? null : Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds;
            ComputeBatchUpload? inputUpload = null;
            if (BatchInputUpload)
            {
                ValidateEmbeddings(initial.HostEmbeddings);
                inputUpload = initial.EmbeddingUpload;
            }
            else
            {
                compute.Upload(initial.Hidden, initial.HostInput, reuseStaging: true);
                compute.Upload(initial.LayerTokenInput, initial.HostLayerInput, reuseStaging: true);
            }
            double inputMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            bool fused = UseFusedAttention(rows, end) || UseSplitAttention(rows, end, prune: false);
            using var scores = fused ? null : compute.CreateTensor(rows * Heads, end);
            using var probabilities = fused ? null : compute.CreateTensor(rows * Heads, end);
            bool prunedFused = UseFusedAttention(1, end);
            using var prunedScores = prune && !prunedFused ? compute.CreateTensor(Heads, end) : null;
            using var prunedProbabilities = prune && !prunedFused ? compute.CreateTensor(Heads, end) : null;
            var batch = compute.CreateBatch();
            double gpuMs = 0;
            int submissions = 0;
            int dispatches = 0;
            Dictionary<string, double>? shaderMs = ProfileDispatches ? new(StringComparer.Ordinal) : null;
            Dictionary<string, double>? feedForwardMs = ProfileDispatches ? new(StringComparer.Ordinal) : null;
            List<FeedForwardDispatches>? feedForward = ProfileDispatches ? [] : null;
            List<ComputeBatchCpuTimings>? batchCpu = compute.MeasureBatchCpuTime ? [] : null;
            void Submit()
            {
                var timing = Execute(batch, shaderMs, inputUpload, feedForward, feedForwardMs);
                feedForward?.Clear();
                inputUpload = null;
                if (timing.CpuTimings is { } cpu) { batchCpu?.Add(cpu); }
                gpuMs += timing.GpuMilliseconds.GetValueOrDefault();
                dispatches += timing.Dispatches;
                submissions++;
                batch = compute.CreateBatch();
            }

            RecordEmbeddingProjection(batch, initial);
            for (int index = 0; index < LayerCount; index++)
            {
                if (batch.DispatchCount > compute.MaximumDispatches - 64) { Submit(); }
                if (prune && index == KvLayers) { batch.Gather(initial.Hidden, final.Hidden, rows - 1); }
                bool pruned = prune && index >= KvLayers;
                var scratch = pruned ? final : initial;
                RecordLayer(batch, index, scratch, initial.LayerInput, end - scratch.Rows,
                    pruned ? prunedScores : scores, pruned ? prunedProbabilities : probabilities, pruned ? rows - 1 : 0,
                    feedForward);
                if (LayerSubmissions) { Submit(); }
            }
            batch.Gather(final.Hidden, result);
            Submit();
            Position = end;
            BodyProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, gpuMs, inputMs, submissions, dispatches)
            {
                ShaderMs = shaderMs,
                FeedForwardMs = feedForwardMs,
                BatchCpu = batchCpu,
                InputDecodeMs = inputDecodeMs,
                InputSubmissions = BatchInputUpload ? 0 : 2,
            };
            return result;
        }
        catch
        {
            result.Dispose();
            Reset();
            throw;
        }
    }

    /// <summary>Advance one supplied token and return its full post-softcap logits.</summary>
    public ReadOnlySpan<float> DecodeLogitsWithReplay(int token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (token is < 0 or >= VocabularySize || Position >= ContextCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(token), "The token and position must fit the model.");
        }
        if (!BatchInputUpload || !DeviceHead || !FuseKeyCache || !FuseQueryRope || LayerSubmissions
            || ProfileDispatches || !PackedDecode || PackedDecodeLanes != 16 || Q4Layout != PackedQ4Layout.Interleaved8
            || SplitDecodeTileKeys == 0)
        {
            throw new InvalidOperationException("Token replay requires the optimized E2B decode path and batch input upload.");
        }
        if (!UseSplitAttention(1, Position + 1, prune: false))
        {
            using var hidden = Evaluate([token]);
            return Logits(hidden);
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            var work = GetScratch(1);
            ReadEmbedding(token, work, 0);
            ValidateEmbeddings(work.HostEmbeddings);
            double inputMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (tokenReplay is null)
            {
                long prepareStart = Stopwatch.GetTimestamp();
                var batch = compute.CreateTokenReplayBatch(Position, ContextCapacity);
                RecordEmbeddingProjection(batch, work);
                for (int index = 0; index < LayerCount; index++)
                {
                    RecordLayer(batch, index, work, work.LayerInput, Position, null, null, 0, null);
                }
                RecordHead(batch, work.Hidden);
                tokenReplay = compute.PrepareTokenReplay(batch, work.EmbeddingUpload, logitStorage,
                    enableGpuTimings: compute.MeasureBatchCpuTime);
                ReplayPreparationMs = Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds;
            }
            var result = tokenReplay.ExecuteToken(Position, logitReadback);
            Position++;
            ReplayedTokens++;
            BodyProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, result.GpuMilliseconds.GetValueOrDefault(),
                inputMs, 1, result.Dispatches);
            HeadProfile = new(0, 0, 0, 0, 0);
            return logitReadback;
        }
        catch
        {
            tokenReplay?.Dispose();
            tokenReplay = null;
            Reset();
            throw;
        }
    }

    private void ReadEmbedding(int token, Scratch scratch, int row)
    {
        checkpoint.ReadRows(Tokens, token, 1, tokenBlock);
        GgufBlocks.DecodeQ6_K(tokenBlock, scratch.HostInput.Slice(row * Width, Width));
        checkpoint.ReadRows(PerLayerTokens, token, 1, layerTokenBlock);
        GgufBlocks.DecodeQ6_K(layerTokenBlock, scratch.HostLayerInput.Slice(row * AllLayerInputs, AllLayerInputs));
    }

    private static void ValidateEmbeddings(Span<float> embeddings)
    {
        foreach (float value in embeddings)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("E2B input embeddings contain a nonfinite value.");
            }
        }
    }

    private void RecordEmbeddingProjection(ComputeMathBatch batch, Scratch work)
    {
        batch.Scale(work.Hidden, work.Hidden, MathF.Sqrt(Width), roundBFloat16: false);
        batch.ProjectDense(work.Hidden, layerInputProjection, work.LayerInput);
        Norm(batch, work.LayerInput, layerInputNorm, work.LayerInput, LayerInput, Width * 1e-6f);
        batch.Scale(work.LayerTokenInput, work.LayerTokenInput, MathF.Sqrt(LayerInput / 2f), roundBFloat16: false);
        batch.Activate(work.LayerInput, work.LayerInput, TensorActivation.Residual, work.LayerTokenInput);
    }

    private void RecordLayer(ComputeMathBatch batch, int index, Scratch work, GpuTensor layerInput,
        int position, GpuTensor? scores, GpuTensor? probabilities, int firstLayerInputRow,
        List<FeedForwardDispatches>? feedForward)
    {
        Layer layer = layers[index];
        bool global = index % 5 == 4;
        int headWidth = global ? 512 : 256;
        GpuTensor query = global ? work.QueryGlobal : work.QueryLocal;
        GpuTensor attention = global ? work.AttentionGlobal : work.AttentionLocal;
        float theta = global ? 1000000 : 10000;
        GpuTensor? factors = global ? ropeFactors : null;
        Layer cache = layer;
        Norm(batch, work.Hidden, layer.AttnNorm, work.Norm, Width);
        Project(batch, work.Norm, layer.Query, query);
        if (FuseQueryRope)
        {
            batch.NormalizeRotateQueries(query, layer.QueryNorm, headWidth, position, theta);
        }
        else
        {
            Norm(batch, query, layer.QueryNorm, query, headWidth);
            batch.RotateHeads(query, query, headWidth, position, theta, roundBFloat16: false, frequencyFactors: factors);
        }
        if (index < KvLayers)
        {
            GpuTensor key = global ? work.KeyGlobal : work.KeyLocal;
            GpuTensor value = global ? work.ValueGlobal : work.ValueLocal;
            // Q, K and V read the same normalized activation and use one K tile.
            Project(batch, work.Norm, layer.Key!, key, reusePacking: ReusePrefillInputPacking);
            Project(batch, work.Norm, layer.Value!, value, reusePacking: ReusePrefillInputPacking);
            if (!FuseKeyCache)
            {
                Norm(batch, key, layer.KeyNorm!, key, headWidth);
            }
            Norm(batch, value, global ? onesGlobal : onesLocal, value, headWidth);
            if (FuseKeyCache)
            {
                batch.NormalizeRotateCacheKey(key, layer.KeyNorm!, layer.Cache!, position, theta);
            }
            else
            {
                batch.RotateHeads(key, key, headWidth, position, theta, roundBFloat16: false, frequencyFactors: factors);
                batch.Scatter(key, layer.Cache!, position);
            }
            batch.Scatter(value, layer.Cache!, position + ContextCapacity);
        }
        else
        {
            cache = layers[global ? KvLayers - 1 : KvLayers - 2];
        }
        int window = global ? 1024 : 512;
        if (UseSplitAttention(work.Rows, position + work.Rows, prune: layerInput.Rows != work.Rows))
        {
            int capacity = (ContextCapacity + SplitDecodeTileKeys - 1) / SplitDecodeTileKeys;
            var partials = work.Tile(compute, Workspace.Attention, Heads * capacity, headWidth + 2);
            batch.SplitDecodeAttention(query, cache.Cache!, partials, attention, Heads, headWidth, position, window,
                SplitDecodeTileKeys);
        }
        else if (UseFusedAttention(work.Rows, position + work.Rows))
        {
            batch.FusedGroupedAttention(query, cache.Cache!, attention, Heads, headWidth, position, window,
                wideKeyTile: WidePrefillKeyTile && work.Rows > 1);
        }
        else
        {
            batch.GroupedAttentionScores(query, cache.Cache!, scores!, Heads, 1, headWidth, position,
                position + work.Rows, window, roundBFloat16: false);
            batch.ReduceRows(scores!, probabilities!);
            batch.GroupedAttentionValues(probabilities!, cache.Cache!, attention, Heads, 1, headWidth,
                roundBFloat16: false, firstValueRow: ContextCapacity);
        }
        Project(batch, attention, layer.AttnOut, work.Branch);
        Norm(batch, work.Branch, layer.AttnPostNorm, work.Branch, Width);
        batch.Activate(work.Hidden, work.Hidden, TensorActivation.Residual, work.Branch);

        Norm(batch, work.Hidden, layer.FfnNorm, work.Norm, Width);
        GpuTensor gate = index < KvLayers ? work.Gate : work.GateWide;
        GpuTensor up = index < KvLayers ? work.Up : work.UpWide;
        int first = batch.DispatchCount;
        if (FuseGateUp && work.Rows == 1)
        {
            batch.ProjectPackedQ4GateUp(work.Norm, layer.Gate, gate);
            feedForward?.Add(new("FusedGateUp", first, batch.DispatchCount));
        }
        else
        {
            int weightRows = FuseGateUp ? 2 * gate.Columns : gate.Columns;
            Project(batch, work.Norm, layer.Gate, gate, weightRows: weightRows);
            feedForward?.Add(new("Gate", first, batch.DispatchCount));
            // Gate and up also read the same packed input, before GELU mutates gate.
            first = batch.DispatchCount;
            Project(batch, work.Norm, layer.Up, up, reusePacking: ReusePrefillInputPacking,
                firstWeightRow: FuseGateUp ? gate.Columns : 0, weightRows: weightRows);
            feedForward?.Add(new("Up", first, batch.DispatchCount));
            first = batch.DispatchCount;
            batch.GeluGate(gate, up, gate);
            feedForward?.Add(new("GeluGate", first, batch.DispatchCount));
        }
        first = batch.DispatchCount;
        bool mergeDownNormalization = work.Rows == 1 && MergeDownNormalization;
        if (work.Rows == 1 && DownProjectionTiles > 0)
        {
            var partials = DownProjectionTiles == 1 ? null : work.Tile(compute, Workspace.ProjectionPartials, DownProjectionTiles, Width);
            batch.ProjectPackedQ4Tiled(gate, layer.Down, work.Branch, partials,
                mergeDownNormalization ? layer.FfnPostNorm : null);
        }
        else
        {
            Project(batch, gate, layer.Down, work.Branch);
        }
        if (mergeDownNormalization)
        {
            feedForward?.Add(new("Down", first, first + 1));
            feedForward?.Add(new("PostFfnNorm", first + 1, batch.DispatchCount));
        }
        else
        {
            feedForward?.Add(new("Down", first, batch.DispatchCount));
            first = batch.DispatchCount;
            Norm(batch, work.Branch, layer.FfnPostNorm, work.Branch, Width);
            feedForward?.Add(new("PostFfnNorm", first, batch.DispatchCount));
        }
        first = batch.DispatchCount;
        batch.Activate(work.Hidden, work.Hidden, TensorActivation.Residual, work.Branch);
        feedForward?.Add(new("PostFfnResidual", first, batch.DispatchCount));

        Project(batch, work.Hidden, layer.InputGate, work.PleGate);
        batch.GeluGate(work.PleGate, layerInput, work.PleGate, firstLayerInputRow, index * LayerInput);
        Project(batch, work.PleGate, layer.InputProjection, work.Branch);
        Norm(batch, work.Branch, layer.InputPostNorm, work.Branch, Width);
        batch.Activate(work.Hidden, work.Hidden, TensorActivation.Residual, work.Branch);
        batch.Scale(work.Hidden, work.Hidden, layer.Scale, roundBFloat16: false);
    }

    private bool UseFusedAttention(int rows, int keys) => rows == 1 ? keys <= FusedDecodeMaxKeys : FusedPrefillAttention;

    private bool UseSplitAttention(int rows, int keys, bool prune)
        => rows == 1 && !prune && SplitDecodeTileKeys > 0 && keys > Math.Max(32, SplitDecodeTileKeys);

    private void Project(ComputeMathBatch batch, GpuTensor input, GpuTensor weights, GpuTensor output,
        bool reusePacking = false, int firstWeightRow = 0, int weightRows = 0)
    {
        if (weightRows == 0) { weightRows = output.Columns; }
        if (CooperativePrefill && compute.CooperativeMatrixEnabled && input.Rows >= 16)
        {
            var scratch = promptScratch!;
            int paddedRows = (input.Rows + 15) / 16 * 16;
            bool padded = paddedRows != input.Rows;
            for (int first = 0; first < input.Columns; first += 4096)
            {
                int width = Math.Min(4096, input.Columns - first);
                var left = scratch.Tile(compute, Workspace.Input, paddedRows, width);
                var right = scratch.Tile(compute, Workspace.Weight, width, output.Columns);
                var product = first == 0 && !padded ? output : scratch.Tile(compute, Workspace.Product, paddedRows, output.Columns);
                if (!reusePacking || input.Columns > 4096) { batch.PackMatrixHalf(input, left, first); }
                batch.PackQ4WeightsHalf(weights, right, input.Columns, first, Q4Layout, firstWeightRow, weightRows);
                batch.ProjectCooperativeTiles(left, right, product, reuseTiles: paddedRows % 32 == 0);
                if (padded)
                {
                    GpuTensor trimmed = first == 0 ? output : scratch.Tile(compute, Workspace.Product, input.Rows, output.Columns);
                    batch.Gather(product, trimmed);
                    product = trimmed;
                }
                if (first > 0) { batch.Activate(output, output, TensorActivation.Residual, product); }
            }
        }
        else if (input.Rows > 1 ? PackedPrefill : PackedDecode)
        {
            batch.ProjectPackedQ4(input, weights, output, PackedDecodeLanes, Q4Layout, firstWeightRow, weightRows);
        }
        else
        {
            batch.QuantizedLinear(input, weights, output);
        }
    }

    private ComputeMathResult Execute(ComputeMathBatch batch, Dictionary<string, double>? shaderMs,
        ComputeBatchUpload? upload = null, List<FeedForwardDispatches>? feedForward = null,
        Dictionary<string, double>? feedForwardMs = null)
    {
        if (shaderMs is null)
        {
            return upload is null ? compute.Execute(batch) : compute.ExecuteWithUpload(batch, upload);
        }
        var profile = compute.ExecuteWithDispatchTimings(batch, upload);
        for (int index = 0; index < profile.ShaderNames.Count; index++)
        {
            string shader = profile.ShaderNames[index];
            shaderMs[shader] = shaderMs.GetValueOrDefault(shader) + profile.DispatchMilliseconds[index];
        }
        if (feedForward is not null && feedForwardMs is not null)
        {
            foreach (var range in feedForward)
            {
                double elapsed = 0;
                for (int index = range.First; index < range.End; index++)
                {
                    elapsed += profile.DispatchMilliseconds[index];
                }
                feedForwardMs[range.Name] = feedForwardMs.GetValueOrDefault(range.Name) + elapsed;
            }
        }
        return profile.Execution;
    }

    public (int Token, float Score) GreedyToken(GpuTensor hidden)
    {
        if (DeviceHead)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long start = Stopwatch.GetTimestamp();
            var batch = RecordHead(hidden);
            batch.ArgMax(logitStorage, argmaxPartials, argmaxOutput);
            Dictionary<string, double>? shaderMs = ProfileDispatches ? new(StringComparer.Ordinal) : null;
            var timing = Execute(batch, shaderMs);
            Span<float> pair = stackalloc float[2];
            long readbackStart = compute.MeasureBatchCpuTime ? Stopwatch.GetTimestamp() : 0;
            compute.ReadInto(argmaxOutput, pair);
            double? readbackMs = readbackStart == 0 ? null : Stopwatch.GetElapsedTime(readbackStart).TotalMilliseconds;
            int token = (int)pair[1];
            if (token is < 0 or >= VocabularySize || !float.IsFinite(pair[0]))
            {
                throw new InvalidDataException("E2B output head did not produce a valid token.");
            }
            HeadProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, timing.GpuMilliseconds.GetValueOrDefault(),
                0, 2, timing.Dispatches)
            {
                ShaderMs = shaderMs, BatchCpu = timing.CpuTimings is { } cpu ? [cpu] : null, ReadbackMs = readbackMs,
            };
            return (token, pair[0]);
        }
        ReadOnlySpan<float> logits = Logits(hidden);
        int best = 0;
        for (int index = 1; index < logits.Length; index++)
        {
            if (logits[index] > logits[best]) { best = index; }
        }
        return (best, logits[best]);
    }

    public ReadOnlySpan<float> Logits(GpuTensor hidden)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        long start = Stopwatch.GetTimestamp();
        var batch = RecordHead(hidden);
        Dictionary<string, double>? shaderMs = ProfileDispatches ? new(StringComparer.Ordinal) : null;
        var timing = Execute(batch, shaderMs);
        long readbackStart = compute.MeasureBatchCpuTime ? Stopwatch.GetTimestamp() : 0;
        compute.ReadInto(logitStorage, logitReadback);
        double? readbackMs = readbackStart == 0 ? null : Stopwatch.GetElapsedTime(readbackStart).TotalMilliseconds;
        if (!DeviceHead)
        {
            for (int index = 0; index < logitReadback.Length; index++)
            {
                logitReadback[index] = 30 * MathF.Tanh(logitReadback[index] / 30);
            }
        }
        HeadProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, timing.GpuMilliseconds.GetValueOrDefault(),
            0, 2, timing.Dispatches)
        {
            ShaderMs = shaderMs, BatchCpu = timing.CpuTimings is { } cpu ? [cpu] : null, ReadbackMs = readbackMs,
        };
        return logitReadback;
    }

    private ComputeMathBatch RecordHead(GpuTensor hidden)
    {
        var batch = compute.CreateBatch();
        RecordHead(batch, hidden);
        return batch;
    }

    private void RecordHead(ComputeMathBatch batch, GpuTensor hidden)
    {
        batch.Gather(hidden, headInput, hidden.Rows - 1);
        Norm(batch, headInput, outputNorm, headInput, Width);
        for (int index = 0; index < headWeights.Length; index++)
        {
            int first = index * HeadShardRows;
            if (DeviceHead)
            {
                batch.ProjectQ6K(headInput, headWeights[index], logitStorage, HeadLayout, first, HeadShardRows, softCap: 30);
            }
            else
            {
                batch.ProjectQ6K(headInput, headWeights[index], headSlice, HeadLayout);
                batch.Scatter(headSlice, logitStorage, first / LogitStorageColumns, first % LogitStorageColumns);
            }
        }
    }

    public void Reset() => Position = 0;

    private Scratch GetScratch(int rows)
    {
        if (rows == 1) { return decodeScratch ??= new Scratch(compute, rows); }
        if (promptScratch?.Rows != rows)
        {
            promptScratch?.Dispose();
            promptScratch = null;
            promptScratch = new Scratch(compute, rows);
        }
        return promptScratch;
    }

    private Layer LoadLayer(int index, float[]? fusedFactors)
    {
        string prefix = $"blk.{index}.";
        bool hasKv = index < KvLayers;
        int headWidth = index % 5 == 4 ? 512 : 256;
        int ffn = hasKv ? 6144 : 12288;
        GpuTensor Q4(string name, int inputs, int outputs) => PackedQ4(prefix + name + ".weight", inputs, outputs);
        GpuTensor V(string name, int width) => Vector(prefix + name + ".weight", width);
        GpuTensor? keyNorm = null;
        if (hasKv)
        {
            keyNorm = headWidth == 512 && FuseKeyCache && fusedFactors is not null
                ? RopeConstants(prefix + "attn_k_norm.weight", headWidth, fusedFactors)
                : V("attn_k_norm", headWidth);
        }
        GpuTensor queryNorm = headWidth == 512 && FuseQueryRope && fusedFactors is not null
            ? RopeConstants(prefix + "attn_q_norm.weight", headWidth, fusedFactors)
            : V("attn_q_norm", headWidth);
        string gateName = prefix + "ffn_gate.weight";
        string upName = prefix + "ffn_up.weight";
        GpuTensor gate = PackedQ4(gateName, Width, ffn, FuseGateUp ? upName : null);
        GpuTensor up = FuseGateUp ? gate : PackedQ4(upName, Width, ffn);
        return new(V("attn_norm", Width), Q4("attn_q", Width, Heads * headWidth), queryNorm,
            hasKv ? Q4("attn_k", Width, headWidth) : null, hasKv ? Q4("attn_v", Width, headWidth) : null,
            keyNorm,
            hasKv ? Own(compute.CreateTensor(ContextCapacity * 2, headWidth)) : null,
            Q4("attn_output", Heads * headWidth, Width), V("post_attention_norm", Width),
            V("ffn_norm", Width), gate, up, Q4("ffn_down", ffn, Width),
            V("post_ffw_norm", Width), Q4("inp_gate", Width, LayerInput), Q4("proj", LayerInput, Width),
            V("post_norm", Width), ReadVector(prefix + "layer_output_scale.weight", 1)[0]);
    }

    private GpuTensor RopeConstants(string name, int width, float[] factors)
    {
        var constants = new float[width + factors.Length];
        ReadVector(name, width).CopyTo(constants, 0);
        factors.CopyTo(constants, width);
        var tensor = Own(compute.CreateTensor(1, constants.Length));
        compute.Upload(tensor, constants, reuseStaging: true);
        return tensor;
    }

    private GpuTensor PackedQ4(string name, int inputs, int outputs, string? pairedName = null)
    {
        checkpoint.RequireTensor(name, GgufTensorType.Q4_0, inputs, outputs);
        if (pairedName is not null) { checkpoint.RequireTensor(pairedName, GgufTensorType.Q4_0, inputs, outputs); }
        var bytes = new byte[outputs * GgufCheckpoint.RowBytes(GgufTensorType.Q4_0, inputs)];
        bool interleaved = Q4Layout == PackedQ4Layout.Interleaved8;
        int storageRows = interleaved ? outputs / 2 * 9 : outputs * 5;
        var words = new uint[storageRows * (inputs / 32)];
        int matrices = pairedName is null ? 1 : 2;
        var tensor = Own(compute.CreateSparseTensor(matrices * storageRows, inputs / 32, GpuTensorFormat.UInt32));
        Upload(name, 0);
        if (pairedName is not null) { Upload(pairedName, words.Length); }
        return tensor;

        void Upload(string tensorName, int offset)
        {
            checkpoint.ReadRows(tensorName, 0, outputs, bytes);
            if (interleaved)
            {
                GgufBlocks.RepackQ4_0Interleaved8(bytes, words, inputs);
            }
            else
            {
                GgufBlocks.RepackQ4_0(bytes, words);
            }
            compute.UploadUInt32Region(tensor, MemoryMarshal.AsBytes(words.AsSpan()).ToArray(), offset);
        }
    }

    private GpuTensor Vector(string name, int width, float scale = 1)
    {
        float[] values = ReadVector(name, width);
        for (int index = 0; index < values.Length; index++) { values[index] *= scale; }
        var tensor = Own(compute.CreateTensor(1, width));
        compute.Upload(tensor, values, reuseStaging: true);
        return tensor;
    }

    private GpuTensor VectorOnes(int width)
    {
        var tensor = Own(compute.CreateTensor(1, width));
        compute.Upload(tensor, Enumerable.Repeat(1f, width).ToArray(), reuseStaging: true);
        return tensor;
    }

    private float[] ReadVector(string name, int width)
    {
        checkpoint.RequireTensor(name, GgufTensorType.Float32, width);
        var values = new float[width];
        checkpoint.ReadRows(name, 0, 1, MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private GpuTensor Own(GpuTensor tensor)
    {
        owned.Add(tensor);
        return tensor;
    }

    private void Norm(ComputeMathBatch batch, GpuTensor input, GpuTensor gamma, GpuTensor output,
        int width, float epsilon = 1e-6f)
        => batch.NormalizeHeads(input, gamma, output, width, epsilon, roundBFloat16: false, bounded: BoundedNorm,
            specialize1536: SpecializeDecodeNorm && input.Rows == 1);

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        tokenReplay?.Dispose();
        decodeScratch?.Dispose();
        promptScratch?.Dispose();
        foreach (var tensor in owned) { tensor.Dispose(); }
        owned.Clear();
    }

    private sealed record Layer(GpuTensor AttnNorm, GpuTensor Query, GpuTensor QueryNorm, GpuTensor? Key, GpuTensor? Value,
        GpuTensor? KeyNorm, GpuTensor? Cache, GpuTensor AttnOut, GpuTensor AttnPostNorm,
        GpuTensor FfnNorm, GpuTensor Gate, GpuTensor Up, GpuTensor Down, GpuTensor FfnPostNorm, GpuTensor InputGate,
        GpuTensor InputProjection, GpuTensor InputPostNorm, float Scale);

    private readonly record struct FeedForwardDispatches(string Name, int First, int End);

    private enum Workspace { Input, Weight, Product, Attention, ProjectionPartials }

    private sealed class Scratch : IDisposable
    {
        private readonly List<GpuTensor> owned = [];
        private readonly Dictionary<(Workspace Role, int Rows, int Columns), GpuTensor> tiles = [];
        public readonly int Rows;
        public readonly GpuTensor Hidden, Norm, Branch, LayerTokenInput, LayerInput;
        public readonly GpuTensor QueryLocal, QueryGlobal, KeyLocal, KeyGlobal, ValueLocal, ValueGlobal,
            AttentionLocal, AttentionGlobal, Gate, Up, GateWide, UpWide, PleGate;
        private readonly byte[] hostEmbeddings;
        public Span<float> HostEmbeddings => MemoryMarshal.Cast<byte, float>(hostEmbeddings.AsSpan());
        public Span<float> HostInput => HostEmbeddings[..(Rows * Width)];
        public Span<float> HostLayerInput => HostEmbeddings[(Rows * Width)..];
        public ComputeBatchUpload EmbeddingUpload { get; }

        public Scratch(VulkanComputeMath compute, int rows)
        {
            Rows = rows;
            hostEmbeddings = new byte[rows * (Width + AllLayerInputs) * sizeof(float)];
            try
            {
                Hidden = Allocate(Width);
                Norm = Allocate(Width);
                Branch = Allocate(Width);
                LayerTokenInput = Allocate(AllLayerInputs);
                EmbeddingUpload = new([Hidden, LayerTokenInput], hostEmbeddings);
                LayerInput = Allocate(AllLayerInputs);
                QueryLocal = Allocate(Heads * 256);
                QueryGlobal = Allocate(Heads * 512);
                KeyLocal = Allocate(256);
                KeyGlobal = Allocate(512);
                ValueLocal = Allocate(256);
                ValueGlobal = Allocate(512);
                AttentionLocal = Allocate(Heads * 256);
                AttentionGlobal = Allocate(Heads * 512);
                Gate = Allocate(6144);
                Up = Allocate(6144);
                GateWide = Allocate(12288);
                UpWide = Allocate(12288);
                PleGate = Allocate(Gemma4E2bModel.LayerInput);
            }
            catch
            {
                Dispose();
                throw;
            }
            GpuTensor Allocate(int columns)
            {
                var tensor = compute.CreateTensor(rows, columns);
                owned.Add(tensor);
                return tensor;
            }
        }

        public GpuTensor Tile(VulkanComputeMath compute, Workspace role, int rows, int columns)
        {
            var key = (role, rows, columns);
            if (!tiles.TryGetValue(key, out var tensor))
            {
                var format = role is Workspace.Product or Workspace.Attention or Workspace.ProjectionPartials
                    ? GpuTensorFormat.Float32 : GpuTensorFormat.PackedFloat16;
                tensor = compute.CreateTensor(rows, columns, format);
                owned.Add(tensor);
                tiles.Add(key, tensor);
            }
            return tensor;
        }

        public void Dispose()
        {
            foreach (var tensor in owned) { tensor.Dispose(); }
            owned.Clear();
            tiles.Clear();
        }
    }
}
