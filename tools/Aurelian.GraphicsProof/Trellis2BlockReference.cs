using Aurelian.Assets.Tensors;

/// <summary>Independent double-accumulating reference for the explicit upstream
/// block equations, with BF16 rounding at each upstream tensor boundary.</summary>
internal sealed class Trellis2BlockReference(SafeTensorFile weights)
{
    private const int Channels = 1536;

    public float[] Run(float[] input, int tokens, float[] context, int contextTokens, float timestep)
    {
        float[] time = Affine(Trellis2BlockGraph.FrequencyFeatures(timestep), 1, "t_embedder.mlp.0", false);
        time = Affine(Activation(time, false), 1, "t_embedder.mlp.2", false);
        float[] mod = Affine(Activation(time, false), 1, "adaLN_modulation.1", true);
        float[] learnedMod = Small("blocks.0.modulation");
        for (int index = 0; index < mod.Length; index++)
        {
            mod[index] = Round(mod[index] + learnedMod[index]);
        }
        float[] h = Modulate(Normalize(input, tokens), mod, 0);
        float[] qkv = Affine(h, tokens, "blocks.0.self_attn.to_qkv", true);
        float[] q = Gather(qkv, tokens, Channels * 3, 0);
        float[] k = Gather(qkv, tokens, Channels * 3, Channels);
        float[] v = Gather(qkv, tokens, Channels * 3, Channels * 2);
        q = Rope(HeadNormalize(q, "blocks.0.self_attn.q_rms_norm.gamma"), tokens);
        k = Rope(HeadNormalize(k, "blocks.0.self_attn.k_rms_norm.gamma"), tokens);
        h = Affine(Attention(q, k, v, tokens, tokens), tokens, "blocks.0.self_attn.to_out", true);
        float[] x = Residual(input, h, mod, 2 * Channels);
        h = Normalize(x, tokens, "blocks.0.norm2");
        q = Affine(h, tokens, "blocks.0.cross_attn.to_q", true);
        float[] kv = Affine(context, contextTokens, "blocks.0.cross_attn.to_kv", true);
        k = Gather(kv, contextTokens, Channels * 2, 0);
        v = Gather(kv, contextTokens, Channels * 2, Channels);
        q = HeadNormalize(q, "blocks.0.cross_attn.q_rms_norm.gamma");
        k = HeadNormalize(k, "blocks.0.cross_attn.k_rms_norm.gamma");
        h = Affine(Attention(q, k, v, tokens, contextTokens), tokens, "blocks.0.cross_attn.to_out", true);
        x = Residual(x, h);
        h = Modulate(Normalize(x, tokens), mod, 3 * Channels);
        h = Affine(h, tokens, "blocks.0.mlp.mlp.0", true);
        h = Activation(h, true);
        h = Affine(h, tokens, "blocks.0.mlp.mlp.2", true);
        return Residual(x, h, mod, 5 * Channels);
    }

    private float[] Affine(float[] input, int rows, string prefix, bool rounded)
    {
        var shape = weights.Tensors[prefix + ".weight"].Shape;
        int inner = shape[1];
        int columns = shape[0];
        float[] output = new float[rows * columns];
        float[] bias = Small(prefix + ".bias");
        for (int first = 0; first < columns; first += 64)
        {
            int count = Math.Min(64, columns - first);
            float[] tile = weights.ReadRows(prefix + ".weight", first, count);
            Parallel.For(0, rows, row =>
            {
                for (int column = 0; column < count; column++)
                {
                    double sum = bias[first + column];
                    for (int index = 0; index < inner; index++)
                    {
                        sum += (double)input[row * inner + index] * tile[column * inner + index];
                    }
                    float value = (float)sum;
                    output[row * columns + first + column] = rounded ? Round(value) : value;
                }
            });
        }
        return output;
    }

    private float[] Normalize(float[] input, int rows, string? prefix = null)
    {
        float[] gamma = prefix is null ? Enumerable.Repeat(1f, Channels).ToArray() : Small(prefix + ".weight");
        float[] beta = prefix is null ? new float[Channels] : Small(prefix + ".bias");
        var output = new float[input.Length];
        for (int row = 0; row < rows; row++)
        {
            double sum = 0;
            for (int column = 0; column < Channels; column++)
            {
                sum += input[row * Channels + column];
            }
            double mean = sum / Channels;
            double variance = 0;
            for (int column = 0; column < Channels; column++)
            {
                double centered = input[row * Channels + column] - mean;
                variance += centered * centered;
            }
            double inverse = 1 / Math.Sqrt(variance / Channels + 1e-6);
            for (int column = 0; column < Channels; column++)
            {
                output[row * Channels + column] = Round((float)((input[row * Channels + column] - mean) * inverse * gamma[column] + beta[column]));
            }
        }
        return output;
    }

    private static float[] Modulate(float[] input, float[] mod, int offset)
    {
        var output = new float[input.Length];
        for (int index = 0; index < input.Length; index++)
        {
            int column = index % Channels;
            output[index] = Round(Round(input[index] * Round(1 + mod[offset + Channels + column])) + mod[offset + column]);
        }
        return output;
    }

    private float[] HeadNormalize(float[] input, string name)
    {
        float[] gamma = Small(name);
        var output = new float[input.Length];
        for (int first = 0; first < input.Length; first += 128)
        {
            double squares = 0;
            for (int lane = 0; lane < 128; lane++)
            {
                squares += (double)input[first + lane] * input[first + lane];
            }
            double denominator = Math.Max(Math.Sqrt(squares), 1e-12);
            for (int lane = 0; lane < 128; lane++)
            {
                output[first + lane] = Round((float)(input[first + lane] / denominator * gamma[(first + lane) % Channels] * Math.Sqrt(128)));
            }
        }
        return output;
    }

    private static float[] Rope(float[] input, int rows)
    {
        var output = new float[input.Length];
        for (int row = 0; row < rows; row++)
        {
            int[] position = [row / 256, row / 16 % 16, row % 16];
            for (int head = 0; head < 12; head++)
            {
                for (int pair = 0; pair < 64; pair++)
                {
                    double phase = pair < 63 ? position[pair / 21] / Math.Pow(10000, (double)(pair % 21) / 21) : 0;
                    int index = row * Channels + head * 128 + pair * 2;
                    double a = input[index];
                    double b = input[index + 1];
                    output[index] = Round((float)(a * Math.Cos(phase) - b * Math.Sin(phase)));
                    output[index + 1] = Round((float)(a * Math.Sin(phase) + b * Math.Cos(phase)));
                }
            }
        }
        return output;
    }

    private static float[] Attention(float[] q, float[] k, float[] v, int queries, int keys)
    {
        var output = new float[queries * Channels];
        Parallel.For(0, queries, row =>
        {
            double[] scores = new double[keys];
            for (int head = 0; head < 12; head++)
            {
                double maximum = double.NegativeInfinity;
                for (int key = 0; key < keys; key++)
                {
                    double sum = 0;
                    for (int lane = 0; lane < 128; lane++)
                    {
                        sum += (double)q[row * Channels + head * 128 + lane] * k[key * Channels + head * 128 + lane];
                    }
                    scores[key] = sum / Math.Sqrt(128);
                    maximum = Math.Max(maximum, scores[key]);
                }
                double denominator = 0;
                for (int key = 0; key < keys; key++)
                {
                    scores[key] = Math.Exp(scores[key] - maximum);
                    denominator += scores[key];
                }
                for (int lane = 0; lane < 128; lane++)
                {
                    double sum = 0;
                    for (int key = 0; key < keys; key++)
                    {
                        sum += scores[key] * v[key * Channels + head * 128 + lane];
                    }
                    output[row * Channels + head * 128 + lane] = Round((float)(sum / denominator));
                }
            }
        });
        return output;
    }

    private static float[] Gather(float[] input, int rows, int stride, int firstColumn)
    {
        var output = new float[rows * Channels];
        for (int row = 0; row < rows; row++)
        {
            Array.Copy(input, row * stride + firstColumn, output, row * Channels, Channels);
        }
        return output;
    }

    private static float[] Residual(float[] input, float[] branch, float[]? mod = null, int gate = 0)
    {
        var output = new float[input.Length];
        for (int index = 0; index < input.Length; index++)
        {
            float value = mod is null ? branch[index] : Round(branch[index] * mod[gate + index % Channels]);
            output[index] = Round(input[index] + value);
        }
        return output;
    }

    private static float[] Activation(float[] input, bool gelu)
    {
        var result = new float[input.Length];
        for (int index = 0; index < input.Length; index++)
        {
            double value = input[index];
            double activated;
            if (gelu)
            {
                double scaled = Math.Sqrt(2 / Math.PI) * (value + 0.044715 * value * value * value);
                activated = value * 0.5 * (1 + Math.Tanh(scaled));
            }
            else
            {
                activated = value / (1 + Math.Exp(-value));
            }
            result[index] = gelu ? Round((float)activated) : (float)activated;
        }
        return result;
    }

    private float[] Small(string name)
    {
        var info = weights.Tensors[name];
        return info.Shape.Count == 1 ? weights.ReadVector(name) : weights.ReadRows(name, 0, info.Shape[0]);
    }

    public static float Round(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        if ((bits & 0x7f800000u) == 0x7f800000u)
        {
            return float.IsNaN(value) ? BitConverter.UInt32BitsToSingle((bits | 0x400000u) & 0xffff0000u) : value;
        }
        uint rounded = unchecked(bits + 0x7fffu + ((bits >> 16) & 1));
        return BitConverter.UInt32BitsToSingle(rounded & 0xffff0000u);
    }
}
