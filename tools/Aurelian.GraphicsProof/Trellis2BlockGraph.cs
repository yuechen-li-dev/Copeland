using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>Explicit research graph for the pinned sparse-structure block. Runtime
/// execution is C#/Vulkan. No Python, Torch, CUDA, ONNX or external process calls.</summary>
internal sealed class Trellis2BlockGraph(VulkanComputeMath math, SafeTensorFile weights)
{
    public const int Channels = 1536;
    public readonly List<object> Measurements = [];

    public GpuTensor Run(GpuTensor input, GpuTensor context, float timestep)
    {
        using var conditioning = Conditioning(timestep);
        using var learnedModulation = Tensor(1, 9216, Small("blocks.0.modulation"));
        using var modulation = math.CreateTensor(1, 9216);
        Execute(batch => batch.AddBFloat16(conditioning, learnedModulation, modulation));
        using var selfNorm = Normalize(input);
        using var selfInput = math.CreateTensor(input.Rows, Channels);
        Execute(batch => batch.ModulateBFloat16(selfNorm, modulation, selfInput));
        using var selfOutput = SelfAttention(selfInput);
        using var selfResidual = GatedResidual(input, selfOutput, modulation, 2 * Channels);
        using var crossNorm = Normalize(selfResidual, "blocks.0.norm2");
        using var crossOutput = CrossAttention(crossNorm, context);
        using var crossResidual = math.CreateTensor(input.Rows, Channels);
        Execute(batch => batch.AddBFloat16(selfResidual, crossOutput, crossResidual));
        using var ffnNorm = Normalize(crossResidual);
        using var ffnInput = math.CreateTensor(input.Rows, Channels);
        Execute(batch => batch.ModulateBFloat16(ffnNorm, modulation, ffnInput, 3 * Channels));
        using var ffnHidden = Affine(ffnInput, "blocks.0.mlp.mlp.0", rounded: true);
        using var ffnActivated = math.CreateTensor(input.Rows, 8192);
        Execute(batch =>
        {
            batch.Activate(ffnHidden, ffnActivated, TensorActivation.Gelu);
            batch.RoundBFloat16(ffnActivated, ffnActivated);
        });
        using var ffnOutput = Affine(ffnActivated, "blocks.0.mlp.mlp.2", rounded: true);
        return GatedResidual(crossResidual, ffnOutput, modulation, 5 * Channels);
    }

    private GpuTensor Conditioning(float timestep)
    {
        float[] features = FrequencyFeatures(timestep);
        using var input = Tensor(1, 256, features);
        using var first = Affine(input, "t_embedder.mlp.0", rounded: false);
        using var activated = math.CreateTensor(1, Channels);
        Execute(batch => batch.Activate(first, activated, TensorActivation.Silu));
        using var second = Affine(activated, "t_embedder.mlp.2", rounded: false);
        using var sharedInput = math.CreateTensor(1, Channels);
        Execute(batch => batch.Activate(second, sharedInput, TensorActivation.Silu));
        return Affine(sharedInput, "adaLN_modulation.1", rounded: true);
    }

    private GpuTensor SelfAttention(GpuTensor input)
    {
        using var qkv = Affine(input, "blocks.0.self_attn.to_qkv", rounded: true);
        using var q = Gather(qkv, Channels, 0);
        using var k = Gather(qkv, Channels, Channels);
        using var v = Gather(qkv, Channels, Channels * 2);
        using var qGamma = Tensor(1, Channels, Small("blocks.0.self_attn.q_rms_norm.gamma"));
        using var kGamma = Tensor(1, Channels, Small("blocks.0.self_attn.k_rms_norm.gamma"));
        Execute(batch =>
        {
            batch.NormalizeHeadsBFloat16(q, qGamma, q);
            batch.NormalizeHeadsBFloat16(k, kGamma, k);
        });
        using var qr = math.CreateTensor(input.Rows, Channels);
        using var kr = math.CreateTensor(input.Rows, Channels);
        Execute(batch =>
        {
            batch.Rope3D(q, qr);
            batch.Rope3D(k, kr);
        });
        using var attention = math.CreateTensor(input.Rows, Channels);
        var result = math.AttentionTiled(qr, kr, v, attention, queryTileRows: Math.Min(64, input.Rows));
        Measurements.Add(new { Operation = "self-attention", Result = result });
        Console.WriteLine($"self-attention: {result.Submissions} submissions, {result.GpuMilliseconds:F2} ms GPU");
        return Affine(attention, "blocks.0.self_attn.to_out", rounded: true);
    }

    private GpuTensor CrossAttention(GpuTensor input, GpuTensor context)
    {
        using var q = Affine(input, "blocks.0.cross_attn.to_q", rounded: true);
        using var kv = Affine(context, "blocks.0.cross_attn.to_kv", rounded: true);
        using var k = Gather(kv, Channels, 0);
        using var v = Gather(kv, Channels, Channels);
        using var qGamma = Tensor(1, Channels, Small("blocks.0.cross_attn.q_rms_norm.gamma"));
        using var kGamma = Tensor(1, Channels, Small("blocks.0.cross_attn.k_rms_norm.gamma"));
        Execute(batch =>
        {
            batch.NormalizeHeadsBFloat16(q, qGamma, q);
            batch.NormalizeHeadsBFloat16(k, kGamma, k);
        });
        using var attention = math.CreateTensor(input.Rows, Channels);
        var result = math.AttentionTiled(q, k, v, attention, queryTileRows: Math.Min(64, input.Rows));
        Measurements.Add(new { Operation = "cross-attention", Result = result });
        return Affine(attention, "blocks.0.cross_attn.to_out", rounded: true);
    }

    private GpuTensor Affine(GpuTensor input, string prefix, bool rounded)
    {
        var shape = weights.Tensors[prefix + ".weight"].Shape;
        if (shape.Count != 2 || shape[1] != input.Columns)
        {
            throw new InvalidDataException("Checkpoint affine shape mismatch: " + prefix);
        }
        var output = math.CreateTensor(input.Rows, shape[0]);
        float[] bias = Small(prefix + ".bias");
        int tileRows = Math.Min(shape[0], input.Columns > 4096 ? 64 : 256);
        var plan = new StreamingLinearPlan(input.Columns, shape[0], tileRows);
        var result = math.LinearStreamed(input, output, plan, tile =>
        {
            var selected = weights.ReadRows(prefix + ".weight", tile.FirstOutput, tile.OutputCount);
            var values = new float[plan.TileRows * (input.Columns + 1)];
            for (int row = 0; row < tile.OutputCount; row++)
            {
                Array.Copy(selected, row * input.Columns, values, row * (input.Columns + 1), input.Columns);
                values[row * (input.Columns + 1) + input.Columns] = bias[tile.FirstOutput + row];
            }
            return values;
        });
        if (rounded)
        {
            Execute(batch => batch.RoundBFloat16(output, output));
        }
        Measurements.Add(new { Operation = prefix, Result = result });
        Console.WriteLine($"{prefix}: {result.Dispatches} dispatches, {result.GpuMilliseconds:F2} ms GPU");
        return output;
    }

    private GpuTensor Normalize(GpuTensor input, string? affinePrefix = null)
    {
        float[] gamma = affinePrefix is null ? Enumerable.Repeat(1f, Channels).ToArray() : Small(affinePrefix + ".weight");
        float[] bias = affinePrefix is null ? new float[Channels] : Small(affinePrefix + ".bias");
        using var parameters = Tensor(1, Channels * 2, [.. gamma, .. bias]);
        var output = math.CreateTensor(input.Rows, Channels);
        Execute(batch =>
        {
            batch.Normalize(input, parameters, output, Normalization.LayerNorm, epsilon: 1e-6f);
            batch.RoundBFloat16(output, output);
        });
        return output;
    }

    private GpuTensor GatedResidual(GpuTensor input, GpuTensor branch, GpuTensor modulation, int gateOffset)
    {
        // The scratch is an explicitly packed linear payload, not interleaved matrix rows.
        using var packed = math.CreateTensor(input.Rows, Channels + (Channels + input.Rows - 1) / input.Rows);
        var output = math.CreateTensor(input.Rows, Channels);
        Execute(batch =>
        {
            batch.AppendGate(branch, modulation, packed, gateOffset);
            batch.GatedResidualBFloat16(input, packed, output);
        });
        return output;
    }

    private GpuTensor Gather(GpuTensor input, int columns, int offset)
    {
        var output = math.CreateTensor(input.Rows, columns);
        Execute(batch => batch.Gather(input, output, firstColumn: offset));
        return output;
    }

    private void Execute(Action<ComputeMathBatch> write)
    {
        var batch = math.CreateBatch();
        write(batch);
        math.Execute(batch);
    }

    private GpuTensor Tensor(int rows, int columns, float[] values)
    {
        var tensor = math.CreateTensor(rows, columns);
        math.Upload(tensor, values);
        return tensor;
    }

    private float[] Small(string name)
    {
        var info = weights.Tensors[name];
        return info.Shape.Count == 1 ? weights.ReadVector(name) : weights.ReadRows(name, 0, info.Shape[0]);
    }

    public static float[] FrequencyFeatures(float timestep)
    {
        var result = new float[256];
        for (int index = 0; index < 128; index++)
        {
            float frequency = MathF.Exp(-MathF.Log(10000) * index / 128);
            result[index] = MathF.Cos(timestep * frequency);
            result[index + 128] = MathF.Sin(timestep * frequency);
        }
        return result;
    }
}
