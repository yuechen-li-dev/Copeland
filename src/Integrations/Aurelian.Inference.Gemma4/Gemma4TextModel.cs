using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Gemma4;

/// <summary>Pinned Gemma 4 12B Unified text-only graph. Single sequence, bounded
/// causal cache; no vision/audio, shared-KV variants or architecture guessing.</summary>
public sealed class Gemma4TextModel : ICausalTextModel, IDisposable
{
    public const int HiddenWidth = 3840;
    public const int VocabularySize = 262144;
    public const int LayerCount = 48;
    public const string Prefix = "model.language_model.";
    public const string Embeddings = Prefix + "embed_tokens.weight";
    private readonly VulkanComputeMath compute;
    private readonly SafeTensorFile source;
    private readonly VulkanQuantizedLayers layers;
    private readonly Dictionary<string, GpuTensor> vectors = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (GpuTensor Keys, GpuTensor Values)> cache = [];
    private readonly int capacity;
    private bool disposed;
    private int? evaluatedLayerCount;
    private readonly bool useQueuedExecution;
    private readonly List<ComputeQueueMetrics> queueMeasurements = [];
    public IReadOnlyList<ComputeQueueMetrics> QueueMeasurements => queueMeasurements;
    public int Position { get; private set; }
    public int ContextCapacity => capacity;
    public Action<int>? ObserveLayer { get; set; }
    public long ResidentWeightBytes => layers.ResidentBytes;
    public long UploadedWeightBytes => layers.UploadedBytes;
    public QuantizedLayerTimings WeightTimings => layers.Timings;
    public int WeightHostScratchBytes => layers.HostScratchBytes;
    public long WeightHostCacheBytes => layers.HostCacheBytes;

    public static void ValidateConfiguration(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var text = root.GetProperty("text_config");
        bool compatible = root.GetProperty("model_type").GetString() == "gemma4_unified"
            && text.GetProperty("hidden_size").GetInt32() == HiddenWidth
            && text.GetProperty("hidden_size_per_layer_input").GetInt32() == 0
            && text.GetProperty("num_hidden_layers").GetInt32() == LayerCount
            && text.GetProperty("vocab_size").GetInt32() == VocabularySize
            && text.GetProperty("intermediate_size").GetInt32() == 15360
            && text.GetProperty("num_attention_heads").GetInt32() == 16
            && text.GetProperty("num_key_value_heads").GetInt32() == 8
            && text.GetProperty("num_global_key_value_heads").GetInt32() == 1
            && text.GetProperty("head_dim").GetInt32() == 256
            && text.GetProperty("global_head_dim").GetInt32() == 512
            && text.GetProperty("num_kv_shared_layers").GetInt32() == 0
            && !text.GetProperty("enable_moe_block").GetBoolean()
            && text.GetProperty("attention_k_eq_v").GetBoolean()
            && !text.GetProperty("attention_bias").GetBoolean()
            && text.GetProperty("tie_word_embeddings").GetBoolean()
            && !text.GetProperty("use_double_wide_mlp").GetBoolean()
            && text.GetProperty("sliding_window").GetInt32() == 1024
            && text.GetProperty("hidden_activation").GetString() == "gelu_pytorch_tanh"
            && text.GetProperty("rms_norm_eps").GetSingle() == 1e-6f
            && text.GetProperty("use_bidirectional_attention").GetString() == "vision"
            && text.GetProperty("final_logit_softcapping").GetSingle() == 30;
        string[] layerTypes = text.GetProperty("layer_types").EnumerateArray().Select(item => item.GetString()!).ToArray();
        compatible &= layerTypes.Length == LayerCount;
        for (int layer = 0; compatible && layer < LayerCount; layer++)
        {
            compatible &= layerTypes[layer] == (layer % 6 == 5 ? "full_attention" : "sliding_attention");
        }
        var rope = text.GetProperty("rope_parameters");
        compatible &= rope.GetProperty("sliding_attention").GetProperty("rope_theta").GetSingle() == 10000
            && rope.GetProperty("sliding_attention").GetProperty("rope_type").GetString() == "default"
            && rope.GetProperty("full_attention").GetProperty("rope_theta").GetSingle() == 1000000
            && rope.GetProperty("full_attention").GetProperty("rope_type").GetString() == "proportional"
            && rope.GetProperty("full_attention").GetProperty("partial_rotary_factor").GetSingle() == .25f;
        if (!compatible) { throw new InvalidDataException("AUR-GEMMA-001: Configuration is outside the pinned 12B Unified text profile."); }
    }

    public static IEnumerable<ModelTensorRequirement> Requirements()
    {
        yield return new(Embeddings, [VocabularySize, HiddenWidth], "BF16");
        yield return new(Prefix + "norm.weight", [HiddenWidth], "BF16");
        for (int layer = 0; layer < LayerCount; layer++)
        {
            string prefix = Prefix + $"layers.{layer}.";
            bool global = layer % 6 == 5;
            int headWidth = global ? 512 : 256;
            int kvWidth = global ? 512 : 2048;
            foreach (string name in new[] { "input_layernorm", "post_attention_layernorm", "pre_feedforward_layernorm", "post_feedforward_layernorm" })
            {
                yield return new(prefix + name + ".weight", [HiddenWidth], "BF16");
            }
            yield return new(prefix + "layer_scalar", [1], "BF16");
            yield return new(prefix + "self_attn.q_norm.weight", [headWidth], "BF16");
            yield return new(prefix + "self_attn.k_norm.weight", [headWidth], "BF16");
            yield return new(prefix + "self_attn.q_proj.weight", [16 * headWidth, HiddenWidth], "BF16");
            yield return new(prefix + "self_attn.k_proj.weight", [kvWidth, HiddenWidth], "BF16");
            if (!global) { yield return new(prefix + "self_attn.v_proj.weight", [kvWidth, HiddenWidth], "BF16"); }
            yield return new(prefix + "self_attn.o_proj.weight", [HiddenWidth, 16 * headWidth], "BF16");
            yield return new(prefix + "mlp.gate_proj.weight", [15360, HiddenWidth], "BF16");
            yield return new(prefix + "mlp.up_proj.weight", [15360, HiddenWidth], "BF16");
            yield return new(prefix + "mlp.down_proj.weight", [HiddenWidth, 15360], "BF16");
        }
    }

    public Gemma4TextModel(VulkanComputeMath compute, SafeTensorFile source, QuantizedCheckpoint weights,
        int capacity = 256, long residentWeightBudget = 1536L * 1024 * 1024,
        bool useChunkedReference = false, long hostWeightBudget = 0,
        bool useQueuedExecution = false, bool useSeparateUploads = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 1024);
        CheckpointContract.Validate(source, Requirements().ToArray());
        foreach (var required in Requirements().Where(item => item.Shape.Count == 2))
        {
            if (!weights.Matrices.TryGetValue(required.Name, out var matrix)
                || matrix.Rows != required.Shape[0] || matrix.Columns != required.Shape[1])
            {
                throw new InvalidDataException("AUR-GEMMA-002: Quantized matrix differs from profile: " + required.Name);
            }
        }
        this.compute = compute;
        this.source = source;
        this.capacity = capacity;
        this.useQueuedExecution = useQueuedExecution;
        if (useQueuedExecution && compute.StreamController is null)
        {
            throw new ArgumentException("Queued evaluation requires a compute stream controller.", nameof(compute));
        }
        layers = new(compute, weights, residentWeightBudget, hostWeightBudget)
        {
            UseChunkedReference = useChunkedReference,
            UseSeparateUploads = useSeparateUploads,
        };
    }

    public GpuTensor Evaluate(IReadOnlyList<int> tokens, int numberOfLayers = LayerCount)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (tokens.Count == 0 || Position + tokens.Count > capacity || tokens.Any(id => id is < 0 or >= VocabularySize)
            || numberOfLayers is < 1 or > LayerCount || evaluatedLayerCount is not null && evaluatedLayerCount != numberOfLayers)
        {
            throw new InvalidDataException("AUR-GEMMA-003: Token range or cache capacity exceeded.");
        }
        evaluatedLayerCount = numberOfLayers;
        var hidden = compute.CreateTensor(tokens.Count, HiddenWidth);
        try
        {
            // The shared score tensor has at most 8,192 rows. Author larger
            // prefills as cache-preserving chunks, retaining every output row.
            if (tokens.Count > 512)
            {
                for (int first = 0; first < tokens.Count; first += 512)
                {
                    using var chunk = Evaluate(tokens.Skip(first).Take(512).ToArray(), numberOfLayers);
                    var scatter = compute.CreateBatch();
                    scatter.Scatter(chunk, hidden, first);
                    compute.Execute(scatter);
                }
                return hidden;
            }
            var embedding = new float[tokens.Count * HiddenWidth];
            for (int row = 0; row < tokens.Count; row++)
            {
                source.ReadRows(Embeddings, tokens[row], 1).CopyTo(embedding, row * HiddenWidth);
            }
            compute.Upload(hidden, embedding);
            using var queue = useQueuedExecution
                ? compute.BeginQueuedScope(compute.StreamController!, new(numberOfLayers * 32 + 1, 16, 64 * 1024 * 1024))
                : null;
            try
            {
                var start = compute.CreateBatch();
                start.Scale(hidden, hidden, Round(MathF.Sqrt(HiddenWidth)));
                compute.Execute(start);
                for (int layer = 0; layer < numberOfLayers; layer++)
                {
                    ApplyLayer(hidden, layer);
                    ObserveLayer?.Invoke(layer);
                }
                Position += tokens.Count;
                return hidden;
            }
            finally
            {
                if (queue is not null)
                {
                    queue.Flush();
                    if (queueMeasurements.Count == 128)
                    {
                        queueMeasurements.RemoveAt(0);
                    }
                    queueMeasurements.Add(queue.Metrics);
                }
            }
        }
        catch
        {
            hidden.Dispose();
            // Partial cache writes cannot be used for a different prefix.
            Reset();
            throw;
        }
    }

    GpuTensor ICausalTextModel.Evaluate(IReadOnlyList<int> tokens) => Evaluate(tokens);

    private void ApplyLayer(GpuTensor hidden, int layer)
    {
        string prefix = Prefix + $"layers.{layer}.";
        bool global = layer % 6 == 5;
        int width = global ? 512 : 256;
        int kvHeads = global ? 1 : 8;
        int count = Position + hidden.Rows;
        using var normalized = compute.CreateTensor(hidden.Rows, HiddenWidth);
        Norm(hidden, normalized, prefix + "input_layernorm.weight", HiddenWidth);
        using var query = layers.Linear(normalized, prefix + "self_attn.q_proj.weight");
        using var key = layers.Linear(normalized, prefix + "self_attn.k_proj.weight");
        using var value = global ? compute.CreateTensor(hidden.Rows, width) : layers.Linear(normalized, prefix + "self_attn.v_proj.weight");
        if (global)
        {
            var copy = compute.CreateBatch();
            copy.Gather(key, value);
            compute.Execute(copy);
        }
        Norm(query, query, prefix + "self_attn.q_norm.weight", width);
        Norm(key, key, prefix + "self_attn.k_norm.weight", width);
        var rotations = compute.CreateBatch();
        rotations.NormalizeHeads(value, Ones(width), value, width);
        rotations.RotateHeads(query, query, width, Position, global ? 1000000 : 10000, global ? .25f : 1);
        rotations.RotateHeads(key, key, width, Position, global ? 1000000 : 10000, global ? .25f : 1);
        if (!cache.TryGetValue(layer, out var state))
        {
            var keys = compute.CreateTensor(capacity, kvHeads * width);
            try { state = (keys, compute.CreateTensor(capacity, kvHeads * width)); }
            catch { keys.Dispose(); throw; }
            cache.Add(layer, state);
        }
        rotations.Scatter(key, state.Keys, Position);
        rotations.Scatter(value, state.Values, Position);
        using var scores = compute.CreateTensor(hidden.Rows * 16, count);
        using var probabilities = compute.CreateTensor(hidden.Rows * 16, count);
        using var attended = compute.CreateTensor(hidden.Rows, 16 * width);
        rotations.GroupedAttentionScores(query, state.Keys, scores, 16, kvHeads, width, Position, count);
        rotations.ReduceRows(scores, probabilities);
        rotations.RoundBFloat16(probabilities, probabilities);
        rotations.GroupedAttentionValues(probabilities, state.Values, attended, 16, kvHeads, width);
        compute.Execute(rotations);
        using var attentionOutput = layers.Linear(attended, prefix + "self_attn.o_proj.weight");
        Norm(attentionOutput, attentionOutput, prefix + "post_attention_layernorm.weight", HiddenWidth);
        var residual = compute.CreateBatch();
        residual.AddBFloat16(hidden, attentionOutput, hidden);
        compute.Execute(residual);
        Norm(hidden, normalized, prefix + "pre_feedforward_layernorm.weight", HiddenWidth);
        using var gate = layers.Linear(normalized, prefix + "mlp.gate_proj.weight");
        using var up = layers.Linear(normalized, prefix + "mlp.up_proj.weight");
        var activation = compute.CreateBatch();
        activation.Activate(gate, gate, TensorActivation.GeluGate, up);
        compute.Execute(activation);
        using var down = layers.Linear(gate, prefix + "mlp.down_proj.weight");
        Norm(down, down, prefix + "post_feedforward_layernorm.weight", HiddenWidth);
        var finish = compute.CreateBatch();
        finish.AddBFloat16(hidden, down, hidden);
        finish.Scale(hidden, hidden, source.ReadVector(prefix + "layer_scalar")[0]);
        compute.Execute(finish);
    }

    private GpuTensor Ones(int width)
    {
        string name = "ones." + width;
        if (!vectors.TryGetValue(name, out var tensor))
        {
            tensor = compute.CreateTensor(1, width);
            try { compute.Upload(tensor, Enumerable.Repeat(1f, width).ToArray()); vectors.Add(name, tensor); }
            catch { tensor.Dispose(); throw; }
        }
        return tensor;
    }

    private void Norm(GpuTensor input, GpuTensor output, string name, int width)
    {
        if (!vectors.TryGetValue(name, out var gamma))
        {
            gamma = compute.CreateTensor(1, width);
            try { compute.Upload(gamma, source.ReadVector(name)); vectors.Add(name, gamma); }
            catch { gamma.Dispose(); throw; }
        }
        var batch = compute.CreateBatch();
        batch.NormalizeHeads(input, gamma, output, width);
        compute.Execute(batch);
    }

    public (int Token, float Score) GreedyToken(GpuTensor hidden)
    {
        using var last = compute.CreateTensor(1, HiddenWidth);
        var gather = compute.CreateBatch();
        gather.Gather(hidden, last, hidden.Rows - 1);
        compute.Execute(gather);
        Norm(last, last, Prefix + "norm.weight", HiddenWidth);
        int winner = -1;
        float maximum = float.NegativeInfinity;
        for (int first = 0; first < VocabularySize; first += 8192)
        {
            using var logits = layers.Linear(last, Embeddings, first, Math.Min(8192, VocabularySize - first));
            float[] values = compute.Read(logits);
            for (int index = 0; index < values.Length; index++)
            {
                float score = Round(Round(MathF.Tanh(Round(values[index] / 30))) * 30);
                if (!float.IsFinite(score)) { throw new InvalidDataException("AUR-GEMMA-004: Nonfinite output logit."); }
                if (score > maximum) { maximum = score; winner = first + index; }
            }
        }
        return (winner, maximum);
    }

    private static float Round(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return BitConverter.UInt32BitsToSingle((bits + 32767 + ((bits >> 16) & 1)) & 0xFFFF0000);
    }

    public void Reset()
    {
        foreach (var state in cache.Values) { state.Keys.Dispose(); state.Values.Dispose(); }
        cache.Clear();
        Position = 0;
        evaluatedLayerCount = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Reset();
        foreach (var tensor in vectors.Values) { tensor.Dispose(); }
        vectors.Clear();
        layers.Dispose();
    }
}
