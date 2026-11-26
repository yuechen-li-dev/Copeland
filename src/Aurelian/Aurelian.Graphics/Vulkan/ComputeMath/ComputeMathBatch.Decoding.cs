namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
    /// <summary>FP32 projection from row-major input and [outputs, inputs] weights.
    /// Uses the qualified kernels for single rows and the 32-row down projection,
    /// with resident tiled multiplication for the remaining shapes.</summary>
    public void ProjectDense(GpuTensor input, GpuTensor weights, GpuTensor output,
        bool useVectorKernel = true, bool usePromptTile = true)
    {
        if (useVectorKernel && input.Rows == 1)
        {
            MultiplyVector(input, weights, output);
        }
        else if (usePromptTile && input.Rows == 32 && input.Columns == 3072 && output.Columns == 768)
        {
            PrefillDown32(input, weights, output);
        }
        else
        {
            Multiply(input, weights, output, MatrixKernel.Resident64, transposeB: true);
        }
    }

    public void PrefillDown32(GpuTensor input, GpuTensor weights, GpuTensor output)
    {
        Check(input, weights, output);
        Require(input.Rows == 32 && input.Columns == 3072 && weights.Rows == 768 && weights.Columns == 3072
            && output.Rows == 32 && output.Columns == 768, "Expected a 32-row 3072-to-768 projection.");
        Add([new("PrefillDown.v.ts", input, weights, output, new() { Columns = 768 }, 4, 192)]);
    }

    public void DecodeQkv(GpuTensor qkv, GpuTensor cache, GpuTensor query, int position, float scale)
    {
        Check(qkv, cache, query);
        int capacity = cache.Rows / 2;
        Require(qkv.Rows == 1 && query.Rows == 1 && qkv.Columns == 3L * query.Columns
            && cache.Columns == query.Columns && cache.Rows == capacity * 2
            && position >= 0 && position < capacity && float.IsFinite(scale)
            && Groups(query.Columns, 64) <= 65535,
            "Decode QKV needs one projected row and a paired key/value cache.");
        Add([new("DecodeQkv.v.ts", qkv, cache, query, new()
        {
            Width = (uint)query.Columns, Rows = (uint)capacity, Offset = (uint)position, Alpha = scale,
        }, Groups(query.Columns, 64))]);
    }

    /// <summary>Single-row projection with contiguous input weights for each output.</summary>
    public void MultiplyVector(GpuTensor input, GpuTensor weights, GpuTensor output)
    {
        Require(input.Rows == 1, "Single-vector multiply requires one input row.");
        MultiplyVectorRows(input, weights, output);
    }

    /// <summary>Apply independent GEMV reductions to the rows of an input matrix.</summary>
    public void MultiplyVectorRows(GpuTensor input, GpuTensor weights, GpuTensor output)
    {
        Owner.Validate(input, GpuTensorFormat.Float32);
        bool packed = weights.Format == GpuTensorFormat.PackedFloat16;
        Owner.Validate(weights, packed ? GpuTensorFormat.PackedFloat16 : GpuTensorFormat.Float32);
        Owner.Validate(output, GpuTensorFormat.Float32);
        Require(output.Rows == input.Rows && input.Columns <= 8192
            && weights.Columns == input.Columns && output.Columns == weights.Rows
            && !ReferenceEquals(output, input) && !ReferenceEquals(output, weights),
            "GEMV requires independent output, matched input rows and weights shaped [output, input].");
        Add([new(packed ? "DenseGemv.Packed" : "DenseGemv.v.ts", input, weights, output, new()
        {
            Columns = (uint)output.Columns,
            Inner = (uint)input.Columns,
            Width = Groups(input.Columns, 32),
        }, Groups(output.Columns, 4), (uint)output.Rows)]);
    }

    /// <summary>Noncausal FP32 attention with 64-channel heads. The first half
    /// of packedKeyValue contains key rows, the second half contains value rows.</summary>
    public void FusedAttention64(GpuTensor query, GpuTensor packedKeyValue, GpuTensor output)
    {
        Check(query, packedKeyValue, output);
        int keys = packedKeyValue.Rows / 2;
        int heads = query.Columns / 64;
        Require(SameShape(query, output) && query.Columns == packedKeyValue.Columns
            && query.Columns % 64 == 0 && keys is > 0 and <= 2048 && packedKeyValue.Rows == 2 * keys
            && Groups(query.Rows, 16) <= 65535 && heads <= 65535
            && !ReferenceEquals(output, query) && !ReferenceEquals(output, packedKeyValue),
            "Fused attention needs 64-wide heads, matching output and paired key/value rows for up to 2048 keys.");
        Add([new("FusedAttention64.v.ts", query, packedKeyValue, output, new()
        {
            Rows = (uint)query.Rows, Columns = (uint)keys, Width = (uint)query.Columns, Inner = Groups(keys, 32),
        }, Groups(query.Rows, 16), (uint)heads)]);
    }

    /// <summary>FP32 causal prefill from position zero. Query already includes the
    /// head scale. Cache halves hold K and V and may extend beyond the live rows.</summary>
    public void CausalPrefillAttention64(GpuTensor scaledQuery, GpuTensor keyValue, GpuTensor output)
    {
        Check(scaledQuery, keyValue, output);
        int rows = scaledQuery.Rows;
        int capacity = keyValue.Rows / 2;
        Require(SameShape(scaledQuery, output) && rows <= 2048 && rows <= capacity
            && keyValue.Rows == capacity * 2 && keyValue.Columns == scaledQuery.Columns
            && output.Columns % 64 == 0 && output.Columns / 64 <= 65535
            && !ReferenceEquals(scaledQuery, output) && !ReferenceEquals(keyValue, output),
            "Causal prefill requires 64-wide heads, independent output, and two cache halves covering the prompt.");
        Add([new("FusedAttention64.Causal", scaledQuery, keyValue, output, new()
        {
            Rows = (uint)rows, Columns = (uint)rows, Width = (uint)output.Columns,
            Inner = Groups(rows, 32), Offset = (uint)capacity,
        }, Groups(rows, 16), (uint)output.Columns / 64)]);
    }

    /// <summary>Uses packed binary16 Q/K/V with FP32 reduction and accumulation.</summary>
    public void FusedAttention64Packed(GpuTensor query, GpuTensor keyValue, GpuTensor output, int keys)
    {
        FusedAttention64Packed(query, keyValue, output, keys, cooperative: false);
    }

    public void FusedAttention64Packed(GpuTensor query, GpuTensor keyValue, GpuTensor output, int keys, bool cooperative)
    {
        FusedAttention64PackedCore(query, keyValue, output, keys, cooperative, 16);
    }

    public void CooperativeAttention64(GpuTensor query, GpuTensor keyValue, GpuTensor output, int keys, int keyTile)
    {
        Require(keyTile is 16 or 32 or 64, "Cooperative attention supports 16, 32 or 64 keys per tile.");
        FusedAttention64PackedCore(query, keyValue, output, keys, true, keyTile);
    }

    private void FusedAttention64PackedCore(GpuTensor query, GpuTensor keyValue, GpuTensor output, int keys,
        bool cooperative, int keyTile)
    {
        Require(!cooperative || Owner.CooperativeMatrixEnabled, "Cooperative attention requires subgroup FP16 matrix support.");
        Owner.Validate(query, GpuTensorFormat.PackedFloat16);
        Owner.Validate(keyValue, GpuTensorFormat.PackedFloat16);
        Owner.Validate(output, GpuTensorFormat.Float32);
        Require(keys is > 0 and <= 2048 && keyValue.Rows == Groups(2 * keys, 16) * 16
            && query.Rows == Groups(output.Rows, 16) * 16 && query.Columns == output.Columns
            && keyValue.Columns == output.Columns && output.Columns % 64 == 0
            && Groups(output.Rows, 16) <= 65535 && output.Columns / 64 <= 65535,
            "Packed fused attention requires aligned Q/K/V, paired key/value rows, and 64-wide heads.");
        string shader = cooperative ? "CooperativeAttention64.v.ts" : "FusedAttention64.Packed";
        if (cooperative && keyTile > 16)
        {
            shader = keyTile == 64 ? "WideCooperativeAttention64.Keys64" : "WideCooperativeAttention64.v.ts";
        }
        Add([new(shader, query, keyValue, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)keys, Width = (uint)output.Columns,
            Inner = Groups(keys, cooperative ? keyTile : 32), Mode = (uint)keyTile,
        }, Groups(output.Rows, 16), (uint)output.Columns / 64)]);
    }

    /// <summary>Cross-attention over a resident token-major FP32 cache.</summary>
    public void AttendToCache(GpuTensor query, GpuTensor keys, GpuTensor values,
        GpuTensor scores, GpuTensor probabilities, GpuTensor output, int headWidth)
    {
        Check(query, keys, values, scores, probabilities, output);
        int heads = query.Columns / Math.Max(1, headWidth);
        int headRows = query.Rows * heads;
        Require(SameShape(query, output) && SameShape(keys, values)
            && query.Columns == keys.Columns && headWidth is 64 or 128 && query.Columns % headWidth == 0
            && keys.Rows <= 2048 && scores.Rows == headRows && scores.Columns == keys.Rows
            && SameShape(scores, probabilities) && !ReferenceEquals(scores, probabilities)
            && Independent(scores) && Independent(probabilities) && Independent(output)
            && !ReferenceEquals(output, scores) && !ReferenceEquals(output, probabilities),
            "Cross-attention needs matching query rows, independent output and scratch, 64/128-wide heads and at most 2048 keys.");
        KernelParameters parameters = new()
        {
            Columns = (uint)keys.Rows,
            Inner = Groups(keys.Rows, 8),
            Width = (uint)headWidth,
            Height = (uint)heads,
            Alpha = 1f / MathF.Sqrt(headWidth),
        };
        Add([
            new("CrossAttentionScores.v.ts", query, keys, scores, parameters, Groups(keys.Rows, 4), (uint)headRows),
            new("RowReduction.v.ts", scores, scores, probabilities, new()
            {
                Rows = (uint)headRows, Columns = (uint)keys.Rows, Mode = (uint)RowReduction.Softmax,
            }, (uint)headRows),
            new("CrossAttentionValues.v.ts", probabilities, values, output, parameters, (uint)headWidth / 16, (uint)headRows),
        ]);

        bool Independent(GpuTensor tensor) => !ReferenceEquals(tensor, query)
            && !ReferenceEquals(tensor, keys) && !ReferenceEquals(tensor, values);
    }

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
        GroupedAttentionScores(query, keys, scores, heads, kvHeads, headWidth, positionOffset, keyCount, window,
            roundBFloat16: true);
    }

    public void GroupedAttentionScores(GpuTensor query, GpuTensor keys, GpuTensor scores,
        int heads, int kvHeads, int headWidth, int positionOffset, int keyCount, int window, bool roundBFloat16)
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
            Stride = (uint)window, Mode = roundBFloat16 ? 0u : 2u,
        }, (uint)keyCount, (uint)(query.Rows * heads))]);
    }

    public void GroupedAttentionValues(GpuTensor probabilities, GpuTensor values, GpuTensor output,
        int heads, int kvHeads, int headWidth)
    {
        GroupedAttentionValues(probabilities, values, output, heads, kvHeads, headWidth, roundBFloat16: true);
    }

    public void GroupedAttentionValues(GpuTensor probabilities, GpuTensor values, GpuTensor output,
        int heads, int kvHeads, int headWidth, bool roundBFloat16)
        => GroupedAttentionValues(probabilities, values, output, heads, kvHeads, headWidth, roundBFloat16, 0);

    public void GroupedAttentionValues(GpuTensor probabilities, GpuTensor values, GpuTensor output,
        int heads, int kvHeads, int headWidth, bool roundBFloat16, int firstValueRow)
    {
        Check(probabilities, values, output);
        Require(heads is > 0 and <= 64 && kvHeads > 0 && heads % kvHeads == 0
            && headWidth is > 0 and <= 512 && output.Columns == heads * headWidth
            && values.Columns == kvHeads * headWidth && firstValueRow >= 0 && values.Rows - firstValueRow >= probabilities.Columns
            && probabilities.Columns is > 0 and <= 1024 && probabilities.Rows == output.Rows * heads
            && !ReferenceEquals(output, probabilities) && !ReferenceEquals(output, values),
            "GQA values require matching probabilities, cache and independent output.");
        Add([new("GroupedAttention.v.ts", probabilities, values, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)probabilities.Columns, Width = (uint)headWidth,
            Height = (uint)heads, Channels = (uint)kvHeads, Offset = (uint)firstValueRow, Mode = roundBFloat16 ? 1u : 3u,
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
