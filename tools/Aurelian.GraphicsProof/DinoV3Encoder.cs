using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>The pinned DINOv3 ViT-L/16 inference graph used by TRELLIS.2.
/// Host code streams checkpoint rows; all learned arithmetic stays on Vulkan.
/// TRELLIS uses non-affine final LayerNorm, not the model's learned norm.</summary>
internal sealed class DinoV3Encoder(VulkanComputeMath math, SafeTensorFile weights)
{
    public const int Channels = 1024;
    public const int PrefixTokens = 5;
    public List<object> Measurements { get; } = [];

    public GpuTensor Encode(float[] patches, int grid)
    {
        if (grid != 32 || patches.Length != grid * grid * 768)
        {
            throw new ArgumentException("DINO encoder proof requires the real 512x512 TRELLIS input.");
        }
        using var patchInput = Tensor(grid * grid, 768, patches);
        using var patchEmbedding = Affine(patchInput, "embeddings.patch_embeddings", slices: true);
        using var prefix = Tensor(PrefixTokens, Channels,
            [.. weights.ReadSlices("embeddings.cls_token", 0, 1), .. weights.ReadSlices("embeddings.register_tokens", 0, 1)]);
        GpuTensor hidden = math.CreateTensor(grid * grid + PrefixTokens, Channels);
        Execute(batch =>
        {
            batch.Scatter(prefix, hidden);
            batch.Scatter(patchEmbedding, hidden, firstRow: PrefixTokens);
        });
        try
        {
            for (int layer = 0; layer < 24; layer++)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                GpuTensor next = Layer(hidden, grid, layer);
                hidden.Dispose();
                hidden = next;
                Console.WriteLine($"DINO layer {layer + 1}/24: {timer.Elapsed.TotalSeconds:F2} s");
            }
            return Normalize(hidden, null);
        }
        finally
        {
            hidden.Dispose();
        }
    }

    private GpuTensor Layer(GpuTensor input, int grid, int layer)
    {
        string prefix = $"layer.{layer}";
        using var normalized = Normalize(input, prefix + ".norm1");
        using var q = Affine(normalized, prefix + ".attention.q_proj");
        using var k = Affine(normalized, prefix + ".attention.k_proj");
        using var v = Affine(normalized, prefix + ".attention.v_proj");
        using var qr = math.CreateTensor(input.Rows, Channels);
        using var kr = math.CreateTensor(input.Rows, Channels);
        Execute(batch =>
        {
            batch.Rope2D(q, qr, grid);
            batch.Rope2D(k, kr, grid);
        });
        using var attended = math.CreateTensor(input.Rows, Channels);
        var attention = math.AttentionTiled(qr, kr, v, attended, headWidth: 64,
            queryTileRows: 64, roundOutput: false);
        Measurements.Add(new { Layer = layer, Operation = "attention", Result = attention });
        using var projected = Affine(attended, prefix + ".attention.o_proj");
        using var residual = Residual(input, projected, prefix + ".layer_scale1.lambda1");
        using var ffnInput = Normalize(residual, prefix + ".norm2");
        using var expanded = Affine(ffnInput, prefix + ".mlp.up_proj");
        Execute(batch => batch.Activate(expanded, expanded, TensorActivation.GeluErf));
        using var contracted = Affine(expanded, prefix + ".mlp.down_proj");
        return Residual(residual, contracted, prefix + ".layer_scale2.lambda1");
    }

    private GpuTensor Affine(GpuTensor input, string prefix, bool slices = false)
    {
        string name = prefix + ".weight";
        var info = weights.Tensors[name];
        int columns = info.Shape[0];
        int inner = info.Shape.Skip(1).Aggregate(1, (a, b) => checked(a * b));
        if (inner != input.Columns || (!slices && info.Shape.Count != 2))
        {
            throw new InvalidDataException("DINO checkpoint affine shape mismatch: " + name);
        }
        float[] bias = weights.Tensors.ContainsKey(prefix + ".bias")
            ? weights.ReadVector(prefix + ".bias") : new float[columns];
        var output = math.CreateTensor(input.Rows, columns);
        // Selected reads are <=1M values and only one weight tile is resident.
        int tileRows = input.Columns == 4096 ? 128 : 256;
        var plan = new StreamingLinearPlan(input.Columns, columns, tileRows);
        try
        {
            var result = math.LinearStreamed(input, output, plan, tile =>
            {
                float[] selected = slices
                    ? weights.ReadSlices(name, tile.FirstOutput, tile.OutputCount)
                    : weights.ReadRows(name, tile.FirstOutput, tile.OutputCount);
                var packed = new float[plan.TileRows * (input.Columns + 1)];
                for (int row = 0; row < tile.OutputCount; row++)
                {
                    Array.Copy(selected, row * input.Columns, packed, row * (input.Columns + 1), input.Columns);
                    packed[row * (input.Columns + 1) + input.Columns] = bias[tile.FirstOutput + row];
                }
                return packed;
            });
            Measurements.Add(new { Operation = prefix, Result = result });
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private GpuTensor Normalize(GpuTensor input, string? prefix)
    {
        float[] gamma = prefix is null ? Enumerable.Repeat(1f, Channels).ToArray() : weights.ReadVector(prefix + ".weight");
        float[] beta = prefix is null ? new float[Channels] : weights.ReadVector(prefix + ".bias");
        using var parameters = Tensor(1, Channels * 2, [.. gamma, .. beta]);
        var output = math.CreateTensor(input.Rows, Channels);
        Execute(batch => batch.Normalize(input, parameters, output, Normalization.LayerNorm, 1e-5f));
        return output;
    }

    private GpuTensor Residual(GpuTensor input, GpuTensor branch, string scaleName)
    {
        using var scale = Tensor(1, Channels, weights.ReadVector(scaleName));
        using var packed = math.CreateTensor(input.Rows, Channels + (Channels + input.Rows - 1) / input.Rows);
        var output = math.CreateTensor(input.Rows, Channels);
        Execute(batch =>
        {
            batch.AppendGate(branch, scale, packed, 0);
            batch.ScaledResidual(input, packed, output);
        });
        return output;
    }

    private GpuTensor Tensor(int rows, int columns, float[] values)
    {
        var tensor = math.CreateTensor(rows, columns);
        math.Upload(tensor, values);
        return tensor;
    }

    private void Execute(Action<ComputeMathBatch> write)
    {
        var batch = math.CreateBatch();
        write(batch);
        math.Execute(batch);
    }
}
