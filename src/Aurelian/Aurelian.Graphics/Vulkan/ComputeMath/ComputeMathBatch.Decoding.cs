namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
    public void QuantizedLinear(GpuTensor input, GpuTensor weights, GpuTensor output,
        bool roundBFloat16 = false, bool chunkedReference = false)
    {
        Check(input, output);
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        Require(input.Columns % 32 == 0 && input.Columns <= 32768
            && output.Rows == input.Rows && output.Columns <= 65535
            && weights.Elements == (long)output.Columns * input.Columns / 32 * 5
            && !ReferenceEquals(input, output),
            "Q4B32 linear requires independent output, complete blocks and exact packed weight count.");
        if (!chunkedReference)
        {
            Add([new("QuantizedGemv.v.ts", input, weights, output, new()
            {
                Rows = (uint)input.Rows,
                Columns = (uint)output.Columns,
                Inner = (uint)input.Columns,
                Mode = roundBFloat16 ? 1u : 0u,
            }, (uint)output.Columns, (uint)input.Rows)]);
            return;
        }
        for (int offset = 0; offset < input.Columns; offset += 1024)
        {
            Add([new("QuantizedLinear.v.ts", input, weights, output, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)output.Columns,
                Inner = (uint)input.Columns, Offset = (uint)offset,
            }, (uint)output.Columns, (uint)input.Rows)]);
        }
        if (roundBFloat16)
        {
            RoundBFloat16(output, output);
        }
    }

    public void NormalizeHeads(GpuTensor input, GpuTensor gamma, GpuTensor output, int headWidth, float epsilon = 1e-6f)
    {
        Check(input, gamma, output);
        Require(SameShape(input, output) && headWidth is > 0 and <= 8192
            && input.Columns % headWidth == 0 && input.Elements / headWidth <= 65535 && gamma.Elements == headWidth
            && float.IsFinite(epsilon) && epsilon > 0 && !ReferenceEquals(output, gamma),
            "Head RMSNorm requires matching shapes, independent gamma and a bounded head width.");
        Add([new("DecoderNormalize.v.ts", input, gamma, output, new()
        {
            Rows = (uint)(input.Elements / headWidth), Columns = (uint)headWidth, Epsilon = epsilon,
        }, (uint)(input.Elements / headWidth))]);
    }

    public void RotateHeads(GpuTensor input, GpuTensor output, int headWidth, int positionOffset,
        float theta, float rotaryProportion = 1)
    {
        Check(input, output);
        Require(SameShape(input, output) && headWidth is > 0 and <= 512 && headWidth % 2 == 0
            && input.Columns % headWidth == 0 && positionOffset is >= 0 and <= 262144
            && float.IsFinite(theta) && theta > 1 && rotaryProportion is > 0 and <= 1,
            "Head RoPE requires bounded even heads, position and rotary proportion.");
        Add([new("DecoderRope.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Width = (uint)headWidth,
            Offset = (uint)positionOffset, Theta = theta, Height = (uint)(headWidth / 2 * rotaryProportion),
        }, Groups(input.Elements / 2, 64))]);
    }

    public void GroupedAttentionScores(GpuTensor query, GpuTensor keys, GpuTensor scores,
        int heads, int kvHeads, int headWidth, int positionOffset, int keyCount, int window = 1024)
    {
        Check(query, keys, scores);
        Require(heads is > 0 and <= 64 && kvHeads > 0 && heads % kvHeads == 0
            && headWidth is > 0 and <= 512 && query.Columns == heads * headWidth
            && keys.Columns == kvHeads * headWidth && keyCount is > 0 and <= 1024
            && keys.Rows >= keyCount && scores.Rows == query.Rows * heads && scores.Columns == keyCount
            && positionOffset is >= 0 and <= 262144 && positionOffset + query.Rows == keyCount && window is > 0 and <= 1024
            && !ReferenceEquals(scores, query) && !ReferenceEquals(scores, keys),
            "GQA scores require bounded causal Q/K and an explicit cache prefix/window.");
        Add([new("GroupedAttention.v.ts", query, keys, scores, new()
        {
            Rows = (uint)query.Rows, Columns = (uint)keyCount, Width = (uint)headWidth,
            Height = (uint)heads, Channels = (uint)kvHeads, Offset = (uint)positionOffset,
            Stride = (uint)window, Mode = 0,
        }, (uint)keyCount, (uint)(query.Rows * heads))]);
    }

    public void GroupedAttentionValues(GpuTensor probabilities, GpuTensor values, GpuTensor output,
        int heads, int kvHeads, int headWidth)
    {
        Check(probabilities, values, output);
        Require(heads is > 0 and <= 64 && kvHeads > 0 && heads % kvHeads == 0
            && headWidth is > 0 and <= 512 && output.Columns == heads * headWidth
            && values.Columns == kvHeads * headWidth && values.Rows >= probabilities.Columns
            && probabilities.Columns is > 0 and <= 1024 && probabilities.Rows == output.Rows * heads
            && !ReferenceEquals(output, probabilities) && !ReferenceEquals(output, values),
            "GQA values require matching probabilities, cache and independent output.");
        Add([new("GroupedAttention.v.ts", probabilities, values, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)probabilities.Columns, Width = (uint)headWidth,
            Height = (uint)heads, Channels = (uint)kvHeads, Mode = 1,
        }, (uint)headWidth, (uint)(output.Rows * heads))]);
    }

    public void Scale(GpuTensor input, GpuTensor output, float factor)
    {
        Check(input, output);
        Require(SameShape(input, output) && float.IsFinite(factor), "Scale requires finite factor and matching shapes.");
        Add([new("DecoderScale.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Alpha = factor,
        }, Groups(input.Elements, 64))]);
    }
}
