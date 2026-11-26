using System.Diagnostics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Gemma4;

public sealed record Gemma4StageProfile(double WallMs, double GpuMs, double InputMs, int Submissions, int Dispatches)
{
    public IReadOnlyDictionary<string, double>? ShaderMs { get; init; }
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
    private const int LogitSlice = 32768;
    private const string Tokens = "token_embd.weight";
    private const string PerLayerTokens = "per_layer_token_embd.weight";
    private readonly VulkanComputeMath compute;
    private readonly GgufCheckpoint checkpoint;
    private readonly List<GpuTensor> owned = [];
    private readonly Layer[] layers = new Layer[LayerCount];
    private readonly GpuTensor[] headWeights = new GpuTensor[VocabularySize / LogitSlice];
    private readonly GpuTensor outputNorm, layerInputProjection, layerInputNorm, onesLocal, onesGlobal, ropeFactors;
    private readonly GpuTensor headInput, headSlice, logitStorage, argmaxPartials, argmaxOutput;
    private readonly byte[] tokenBlock = new byte[GgufCheckpoint.RowBytes(GgufTensorType.Q6_K, Width)];
    private readonly byte[] layerTokenBlock = new byte[GgufCheckpoint.RowBytes(GgufTensorType.Q6_K, AllLayerInputs)];
    private readonly float[] logitReadback = new float[VocabularySize];
    private Scratch? decodeScratch, promptScratch;
    private bool disposed;

    public Gemma4E2bModel(VulkanComputeMath compute, GgufCheckpoint checkpoint, int capacity = 640)
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

            for (int index = 0; index < layers.Length; index++) { layers[index] = LoadLayer(index); }
            byte[] packedHead = new byte[LogitSlice * tokenBlock.Length];
            for (int index = 0; index < headWeights.Length; index++)
            {
                checkpoint.ReadRows(Tokens, index * LogitSlice, LogitSlice, packedHead);
                headWeights[index] = Own(compute.CreateSparseTensor(LogitSlice, tokenBlock.Length / 4, GpuTensorFormat.UInt32));
                compute.UploadUInt32Bytes(headWeights[index], packedHead);
            }
            headInput = Own(compute.CreateTensor(1, Width));
            headSlice = Own(compute.CreateTensor(1, LogitSlice));
            logitStorage = Own(compute.CreateTensor(4, 2 * LogitSlice));
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
    public int Position { get; private set; }
    public bool LayerSubmissions { get; init; }
    public bool PruneSharedRows { get; init; } = true;
    public bool PackedPrefill { get; init; } = true;
    public bool PackedDecode { get; init; } = true;
    public int PackedDecodeLanes { get; init; } = 16;
    public bool CooperativePrefill { get; init; } = true;
    public bool ReusePrefillInputPacking { get; init; } = true;
    public bool ProfileDispatches { get; init; }
    public int FusedDecodeMaxKeys { get; init; } = 128;
    public bool FusedPrefillAttention { get; init; } = true;
    public bool WidePrefillKeyTile { get; init; } = true;
    public bool BoundedNorm { get; init; } = true;
    public bool DeviceHead { get; init; } = true;
    public Gemma4StageProfile? BodyProfile { get; private set; }
    public Gemma4StageProfile? HeadProfile { get; private set; }

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
            for (int row = 0; row < rows; row++)
            {
                checkpoint.ReadRows(Tokens, tokens[row], 1, tokenBlock);
                GgufBlocks.DecodeQ6_K(tokenBlock, initial.HostInput.AsSpan(row * Width, Width));
                checkpoint.ReadRows(PerLayerTokens, tokens[row], 1, layerTokenBlock);
                GgufBlocks.DecodeQ6_K(layerTokenBlock, initial.HostLayerInput.AsSpan(row * AllLayerInputs, AllLayerInputs));
            }
            compute.Upload(initial.Hidden, initial.HostInput, reuseStaging: true);
            compute.Upload(initial.LayerTokenInput, initial.HostLayerInput, reuseStaging: true);
            double inputMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            bool fused = UseFusedAttention(rows, end);
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
            void Submit()
            {
                var timing = Execute(batch, shaderMs);
                gpuMs += timing.GpuMilliseconds.GetValueOrDefault();
                dispatches += timing.Dispatches;
                submissions++;
                batch = compute.CreateBatch();
            }

            batch.Scale(initial.Hidden, initial.Hidden, MathF.Sqrt(Width), roundBFloat16: false);
            batch.ProjectDense(initial.Hidden, layerInputProjection, initial.LayerInput);
            Norm(batch, initial.LayerInput, layerInputNorm, initial.LayerInput, LayerInput, Width * 1e-6f);
            batch.Scale(initial.LayerTokenInput, initial.LayerTokenInput, MathF.Sqrt(LayerInput / 2f), roundBFloat16: false);
            batch.Activate(initial.LayerInput, initial.LayerInput, TensorActivation.Residual, initial.LayerTokenInput);
            for (int index = 0; index < LayerCount; index++)
            {
                if (batch.DispatchCount > compute.MaximumDispatches - 64) { Submit(); }
                if (prune && index == KvLayers) { batch.Gather(initial.Hidden, final.Hidden, rows - 1); }
                bool pruned = prune && index >= KvLayers;
                var scratch = pruned ? final : initial;
                RecordLayer(batch, index, scratch, initial.LayerInput, end - scratch.Rows,
                    pruned ? prunedScores : scores, pruned ? prunedProbabilities : probabilities, pruned ? rows - 1 : 0);
                if (LayerSubmissions) { Submit(); }
            }
            batch.Gather(final.Hidden, result);
            Submit();
            Position = end;
            BodyProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, gpuMs, inputMs, submissions, dispatches)
            {
                ShaderMs = shaderMs,
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

    private void RecordLayer(ComputeMathBatch batch, int index, Scratch work, GpuTensor layerInput,
        int position, GpuTensor? scores, GpuTensor? probabilities, int firstLayerInputRow)
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
        Norm(batch, query, layer.QueryNorm, query, headWidth);
        batch.RotateHeads(query, query, headWidth, position, theta, roundBFloat16: false, frequencyFactors: factors);
        if (index < KvLayers)
        {
            GpuTensor key = global ? work.KeyGlobal : work.KeyLocal;
            GpuTensor value = global ? work.ValueGlobal : work.ValueLocal;
            // Q, K and V read the same normalized activation and use one K tile.
            Project(batch, work.Norm, layer.Key!, key, reusePacking: ReusePrefillInputPacking);
            Project(batch, work.Norm, layer.Value!, value, reusePacking: ReusePrefillInputPacking);
            Norm(batch, key, layer.KeyNorm!, key, headWidth);
            Norm(batch, value, global ? onesGlobal : onesLocal, value, headWidth);
            batch.RotateHeads(key, key, headWidth, position, theta, roundBFloat16: false, frequencyFactors: factors);
            batch.Scatter(key, layer.Cache!, position);
            batch.Scatter(value, layer.Cache!, position + ContextCapacity);
        }
        else
        {
            cache = layers[global ? KvLayers - 1 : KvLayers - 2];
        }
        int window = global ? 1024 : 512;
        if (UseFusedAttention(work.Rows, position + work.Rows))
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
        Project(batch, work.Norm, layer.Gate, gate);
        // Gate and up also read the same packed input, before GELU mutates gate.
        Project(batch, work.Norm, layer.Up, up, reusePacking: ReusePrefillInputPacking);
        batch.GeluGate(gate, up, gate);
        Project(batch, gate, layer.Down, work.Branch);
        Norm(batch, work.Branch, layer.FfnPostNorm, work.Branch, Width);
        batch.Activate(work.Hidden, work.Hidden, TensorActivation.Residual, work.Branch);

        Project(batch, work.Hidden, layer.InputGate, work.PleGate);
        batch.GeluGate(work.PleGate, layerInput, work.PleGate, firstLayerInputRow, index * LayerInput);
        Project(batch, work.PleGate, layer.InputProjection, work.Branch);
        Norm(batch, work.Branch, layer.InputPostNorm, work.Branch, Width);
        batch.Activate(work.Hidden, work.Hidden, TensorActivation.Residual, work.Branch);
        batch.Scale(work.Hidden, work.Hidden, layer.Scale, roundBFloat16: false);
    }

    private bool UseFusedAttention(int rows, int keys) => rows == 1 ? keys <= FusedDecodeMaxKeys : FusedPrefillAttention;

    private void Project(ComputeMathBatch batch, GpuTensor input, GpuTensor weights, GpuTensor output,
        bool reusePacking = false)
    {
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
                batch.PackQ4WeightsHalf(weights, right, input.Columns, first);
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
            batch.ProjectPackedQ4(input, weights, output, PackedDecodeLanes);
        }
        else
        {
            batch.QuantizedLinear(input, weights, output);
        }
    }

    private ComputeMathResult Execute(ComputeMathBatch batch, Dictionary<string, double>? shaderMs)
    {
        if (shaderMs is null) { return compute.Execute(batch); }
        var profile = compute.ExecuteWithDispatchTimings(batch);
        for (int index = 0; index < profile.ShaderNames.Count; index++)
        {
            string shader = profile.ShaderNames[index];
            shaderMs[shader] = shaderMs.GetValueOrDefault(shader) + profile.DispatchMilliseconds[index];
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
            compute.ReadInto(argmaxOutput, pair);
            int token = (int)pair[1];
            if (token is < 0 or >= VocabularySize || !float.IsFinite(pair[0]))
            {
                throw new InvalidDataException("E2B output head did not produce a valid token.");
            }
            HeadProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, timing.GpuMilliseconds.GetValueOrDefault(),
                0, 2, timing.Dispatches) { ShaderMs = shaderMs };
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
        compute.ReadInto(logitStorage, logitReadback);
        if (!DeviceHead)
        {
            for (int index = 0; index < logitReadback.Length; index++)
            {
                logitReadback[index] = 30 * MathF.Tanh(logitReadback[index] / 30);
            }
        }
        HeadProfile = new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, timing.GpuMilliseconds.GetValueOrDefault(),
            0, 2, timing.Dispatches) { ShaderMs = shaderMs };
        return logitReadback;
    }

    private ComputeMathBatch RecordHead(GpuTensor hidden)
    {
        var batch = compute.CreateBatch();
        batch.Gather(hidden, headInput, hidden.Rows - 1);
        Norm(batch, headInput, outputNorm, headInput, Width);
        for (int index = 0; index < headWeights.Length; index++)
        {
            if (DeviceHead)
            {
                batch.ProjectQ6K(headInput, headWeights[index], logitStorage, index * LogitSlice, LogitSlice, softCap: 30);
            }
            else
            {
                batch.ProjectQ6K(headInput, headWeights[index], headSlice);
                batch.Scatter(headSlice, logitStorage, index / 2, index % 2 * LogitSlice);
            }
        }
        return batch;
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

    private Layer LoadLayer(int index)
    {
        string prefix = $"blk.{index}.";
        bool hasKv = index < KvLayers;
        int headWidth = index % 5 == 4 ? 512 : 256;
        int ffn = hasKv ? 6144 : 12288;
        GpuTensor Q4(string name, int inputs, int outputs) => PackedQ4(prefix + name + ".weight", inputs, outputs);
        GpuTensor V(string name, int width) => Vector(prefix + name + ".weight", width);
        return new(V("attn_norm", Width), Q4("attn_q", Width, Heads * headWidth), V("attn_q_norm", headWidth),
            hasKv ? Q4("attn_k", Width, headWidth) : null, hasKv ? Q4("attn_v", Width, headWidth) : null,
            hasKv ? V("attn_k_norm", headWidth) : null,
            hasKv ? Own(compute.CreateTensor(ContextCapacity * 2, headWidth)) : null,
            Q4("attn_output", Heads * headWidth, Width), V("post_attention_norm", Width),
            V("ffn_norm", Width), Q4("ffn_gate", Width, ffn), Q4("ffn_up", Width, ffn), Q4("ffn_down", ffn, Width),
            V("post_ffw_norm", Width), Q4("inp_gate", Width, LayerInput), Q4("proj", LayerInput, Width),
            V("post_norm", Width), ReadVector(prefix + "layer_output_scale.weight", 1)[0]);
    }

    private GpuTensor PackedQ4(string name, int inputs, int outputs)
    {
        checkpoint.RequireTensor(name, GgufTensorType.Q4_0, inputs, outputs);
        var bytes = new byte[outputs * GgufCheckpoint.RowBytes(GgufTensorType.Q4_0, inputs)];
        checkpoint.ReadRows(name, 0, outputs, bytes);
        var words = new uint[outputs * inputs / 32 * 5];
        GgufBlocks.RepackQ4_0(bytes, words);
        var tensor = Own(compute.CreateSparseTensor(outputs * 5, inputs / 32, GpuTensorFormat.UInt32));
        compute.UploadUInt32Bytes(tensor, MemoryMarshal.AsBytes(words.AsSpan()).ToArray());
        return tensor;
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
        => batch.NormalizeHeads(input, gamma, output, width, epsilon, roundBFloat16: false, bounded: BoundedNorm);

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        decodeScratch?.Dispose();
        promptScratch?.Dispose();
        foreach (var tensor in owned) { tensor.Dispose(); }
        owned.Clear();
    }

    private sealed record Layer(GpuTensor AttnNorm, GpuTensor Query, GpuTensor QueryNorm, GpuTensor? Key, GpuTensor? Value,
        GpuTensor? KeyNorm, GpuTensor? Cache, GpuTensor AttnOut, GpuTensor AttnPostNorm,
        GpuTensor FfnNorm, GpuTensor Gate, GpuTensor Up, GpuTensor Down, GpuTensor FfnPostNorm, GpuTensor InputGate,
        GpuTensor InputProjection, GpuTensor InputPostNorm, float Scale);

    private enum Workspace { Input, Weight, Product }

    private sealed class Scratch : IDisposable
    {
        private readonly List<GpuTensor> owned = [];
        private readonly Dictionary<(Workspace Role, int Rows, int Columns), GpuTensor> tiles = [];
        public readonly int Rows;
        public readonly GpuTensor Hidden, Norm, Branch, LayerTokenInput, LayerInput;
        public readonly GpuTensor QueryLocal, QueryGlobal, KeyLocal, KeyGlobal, ValueLocal, ValueGlobal,
            AttentionLocal, AttentionGlobal, Gate, Up, GateWide, UpWide, PleGate;
        public readonly float[] HostInput, HostLayerInput;

        public Scratch(VulkanComputeMath compute, int rows)
        {
            Rows = rows;
            HostInput = new float[rows * Width];
            HostLayerInput = new float[rows * AllLayerInputs];
            try
            {
                Hidden = Allocate(Width);
                Norm = Allocate(Width);
                Branch = Allocate(Width);
                LayerTokenInput = Allocate(AllLayerInputs);
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
                var format = role == Workspace.Product ? GpuTensorFormat.Float32 : GpuTensorFormat.PackedFloat16;
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
