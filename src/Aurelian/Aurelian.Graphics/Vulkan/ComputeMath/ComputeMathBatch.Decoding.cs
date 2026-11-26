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

    /// <summary>Reuse a packed projection tile across prompt rows, or select the
    /// number of lanes assigned to each output row for one token.</summary>
    public void ProjectPackedQ4(GpuTensor input, GpuTensor weights, GpuTensor output, int decodeLanes = 32)
        => ProjectPackedQ4(input, weights, output, decodeLanes, PackedQ4Layout.RowMajor);

    public void ProjectPackedQ4(GpuTensor input, GpuTensor weights, GpuTensor output, int decodeLanes, PackedQ4Layout layout)
        => ProjectPackedQ4(input, weights, output, decodeLanes, layout, 0, output.Columns);

    public void ProjectPackedQ4(GpuTensor input, GpuTensor weights, GpuTensor output, int decodeLanes, PackedQ4Layout layout,
        int firstWeightRow, int weightRows)
    {
        Check(input, output);
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        Require(input.Columns % 32 == 0 && input.Columns <= 32768 && output.Rows == input.Rows
            && PackedQ4Contains(weights, input.Columns, layout, firstWeightRow, output.Columns, weightRows)
            && !ReferenceEquals(input, output), "Packed Q4 projection requires aligned complete matrices.");
        Require(layout == PackedQ4Layout.RowMajor || (output.Columns % 8 == 0 && (input.Rows > 1 || decodeLanes == 16)),
            "Interleaved Q4 requires eight aligned outputs and sixteen lanes per output in decode.");
        if (input.Rows == 1)
        {
            int inputTile = layout == PackedQ4Layout.Interleaved8 ? 256 : decodeLanes * 8;
            Require(layout != PackedQ4Layout.Interleaved8 || input.Columns % inputTile == 0,
                "The compact Q4 decode kernel requires complete 256-column input tiles.");
            string shader = (decodeLanes, layout) switch
            {
                (32, _) => "QuantizedGemv4.v.ts",
                (16, PackedQ4Layout.Interleaved8) => "QuantizedGemv.InterleavedHalfScale",
                (16, _) => "QuantizedGemv.Lanes16",
                (8, _) => "QuantizedGemv.Lanes8",
                _ => throw new ArgumentOutOfRangeException(nameof(decodeLanes)),
            };
            Add([new(shader, input, weights, output, new()
            {
                Columns = (uint)output.Columns, Inner = (uint)input.Columns, Width = Groups(input.Columns, inputTile),
                Height = (uint)firstWeightRow,
            }, Groups(output.Columns, 128 / decodeLanes))]);
            return;
        }
        string matrixShader = layout == PackedQ4Layout.Interleaved8
            ? "ResidentSgemm.InterleavedHalfScale" : "ResidentSgemm.v.ts";
        for (int first = 0; first < input.Columns; first += 8192)
        {
            Add([new(matrixShader, input, weights, output, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)output.Columns, Inner = (uint)input.Columns,
                Offset = (uint)first, Mode = 2, Alpha = 1, KernelWidth = Groups(Math.Min(input.Columns - first, 8192), 16),
                Height = (uint)firstWeightRow,
            }, Groups(input.Rows, 64), Groups(output.Columns, 64))]);
        }
    }

    /// <summary>Project a single FP32 input to GELU(gate) times up using two consecutive interleaved Q4 matrices.</summary>
    public void ProjectPackedQ4GateUp(GpuTensor input, GpuTensor weights, GpuTensor output)
    {
        Check(input, output);
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        Require(input.Rows == 1 && output.Rows == 1 && input.Columns <= 32768 && input.Columns % 256 == 0
            && output.Columns % 8 == 0 && !ReferenceEquals(input, output)
            && weights.Elements == PackedQ4Words(2 * output.Columns, input.Columns, PackedQ4Layout.Interleaved8),
            "Fused Q4 gate/up needs a single input and two complete interleaved matrices.");
        Add([new("QuantizedQ4GateUp.v.ts", input, weights, output, new()
        {
            Columns = (uint)output.Columns, Inner = (uint)input.Columns, Width = (uint)input.Columns / 256,
            Offset = (uint)weights.Elements / 2,
        }, (uint)output.Columns / 8)]);
    }

    /// <summary>Project a single FP32 input with 512-column K tiles, optionally splitting the input width across workgroups.</summary>
    public void ProjectPackedQ4Tiled(GpuTensor input, GpuTensor weights, GpuTensor output, GpuTensor? partials = null,
        GpuTensor? normalizationWeights = null, float epsilon = 1e-6f)
    {
        Check(input, output);
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        if (partials is not null)
        {
            Check(partials);
            Require(partials.Rows is 2 or 4 && partials.Columns == output.Columns && !ReferenceEquals(partials, input),
                "Tiled Q4 partials need two or four rows of independent scratch.");
        }
        if (normalizationWeights is not null)
        {
            Check(normalizationWeights);
            Require(partials is not null && output.Columns == 1536 && normalizationWeights.Elements == output.Columns
                && !ReferenceEquals(normalizationWeights, output) && float.IsFinite(epsilon) && epsilon > 0,
                "Fused projection reduction and RMSNorm require split tiles and an independent 1536-wide gain.");
        }
        int tiles = partials?.Rows ?? 1;
        Require(input.Rows == 1 && output.Rows == 1 && input.Columns <= 32768 && input.Columns % (512 * tiles) == 0
            && output.Columns % 8 == 0 && !ReferenceEquals(input, output)
            && weights.Elements == PackedQ4Words(output.Columns, input.Columns, PackedQ4Layout.Interleaved8),
            "Tiled Q4 projection needs a single input and complete interleaved weight and K tiles.");
        var project = new ComputeMathDispatch("QuantizedQ4Tiled.v.ts", input, weights, partials ?? output, new()
        {
            Columns = (uint)output.Columns, Inner = (uint)input.Columns, Width = (uint)(input.Columns / (512 * tiles)),
        }, (uint)output.Columns / 8, (uint)tiles);
        if (partials is null)
        {
            Add([project]);
            return;
        }
        ComputeMathDispatch finish;
        if (normalizationWeights is not null)
        {
            finish = new("DecoderNormalize1536.SplitProjection", partials, normalizationWeights, output,
                new() { Inner = (uint)tiles, Epsilon = epsilon }, 1);
        }
        else
        {
            finish = new("MergeProjectionPartials.v.ts", partials, partials, output,
                new() { Rows = (uint)tiles, Columns = (uint)output.Columns }, Groups(output.Columns, 128));
        }
        Add([project, finish]);
    }

    public void NormalizeHeads(GpuTensor input, GpuTensor gamma, GpuTensor output, int headWidth,
        float epsilon = 1e-6f, bool roundBFloat16 = true, bool bounded = false)
        => NormalizeHeads(input, gamma, output, headWidth, epsilon, roundBFloat16, bounded, specialize1536: false);

    public void NormalizeHeads(GpuTensor input, GpuTensor gamma, GpuTensor output, int headWidth,
        float epsilon, bool roundBFloat16, bool bounded, bool specialize1536)
    {
        Check(input, gamma, output);
        Require(SameShape(input, output) && headWidth is > 0 and <= 8192
            && input.Columns % headWidth == 0 && input.Elements / headWidth <= 65535 && gamma.Elements == headWidth
            && float.IsFinite(epsilon) && epsilon > 0 && !ReferenceEquals(output, gamma),
            "Head RMSNorm requires matching shapes, independent gamma and a bounded head width.");
        string shader = bounded ? "DecoderNormalizeBounded.v.ts" : "DecoderNormalize.v.ts";
        if (specialize1536 && headWidth == 1536 && !roundBFloat16)
        {
            shader = "DecoderNormalize1536.v.ts";
        }
        Add([new(shader, input, gamma, output, new()
        {
            Rows = (uint)(input.Elements / headWidth), Columns = (uint)headWidth, Epsilon = epsilon, Mode = roundBFloat16 ? 0u : 1u,
            Width = Groups(headWidth, 64),
        }, (uint)(input.Elements / headWidth))]);
    }

    public void RotateHeads(GpuTensor input, GpuTensor output, int headWidth, int positionOffset,
        float theta, float rotaryProportion = 1, bool roundBFloat16 = true, GpuTensor? frequencyFactors = null)
    {
        Check(input, output);
        Require(SameShape(input, output) && headWidth is > 0 and <= 512 && headWidth % 2 == 0
            && input.Columns % headWidth == 0 && positionOffset is >= 0 and <= 262144
            && float.IsFinite(theta) && theta > 1 && rotaryProportion is > 0 and <= 1,
            "Head RoPE requires bounded even heads, position and rotary proportion.");
        uint mode = roundBFloat16 ? 0u : 1u;
        if (frequencyFactors is not null)
        {
            Check(frequencyFactors);
            Require(!roundBFloat16 && frequencyFactors.Elements == headWidth / 2 && !ReferenceEquals(frequencyFactors, output),
                "Explicit RoPE frequency factors are one independent FP32 value per pair.");
            mode = 2;
        }
        Add([new("DecoderRope.v.ts", input, frequencyFactors ?? input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Width = (uint)headWidth,
            Offset = (uint)positionOffset, Theta = theta, Height = (uint)(headWidth / 2 * rotaryProportion), Mode = mode,
        }, Groups(input.Elements / 2, 64))]);
    }

    /// <summary>Apply FP32 RMSNorm and half-split RoPE to every query head in place.</summary>
    public void NormalizeRotateQueries(GpuTensor query, GpuTensor gammaAndFactors, int headWidth,
        int positionOffset, float theta, float epsilon = 1e-6f)
    {
        Check(query, gammaAndFactors);
        bool hasFactors = gammaAndFactors.Elements == headWidth + headWidth / 2;
        Require(headWidth is 256 or 512 && query.Columns % headWidth == 0 && positionOffset is >= 0 and <= 262144
            && (hasFactors || gammaAndFactors.Elements == headWidth) && !ReferenceEquals(query, gammaAndFactors)
            && float.IsFinite(theta) && theta > 1 && float.IsFinite(epsilon) && epsilon > 0,
            "Fused query RoPE needs complete heads and an independent gamma and optional factor vector.");
        Add([new("NormalizeRotateCacheKey.Query", query, gammaAndFactors, query, new()
        {
            Width = (uint)headWidth, Inner = (uint)headWidth / 64, Offset = (uint)positionOffset,
            Height = (uint)query.Columns / (uint)headWidth, Mode = hasFactors ? 1u : 0u, Theta = theta, Epsilon = epsilon,
        }, (uint)(query.Elements / headWidth))]);
        TrackTokenOffset(positionOffset, 262145, query.Rows);
    }

    public void GroupedAttentionScores(GpuTensor query, GpuTensor keys, GpuTensor scores,
        int heads, int kvHeads, int headWidth, int positionOffset, int keyCount, int window = 1024)
    {
        GroupedAttentionScores(query, keys, scores, heads, kvHeads, headWidth, positionOffset, keyCount, window,
            roundBFloat16: true);
    }

    /// <summary>Apply FP32 RMSNorm and half-split RoPE, then write directly to the cache's key half.
    /// Optional frequency factors follow the gamma vector in the constants tensor.</summary>
    public void NormalizeRotateCacheKey(GpuTensor keys, GpuTensor gammaAndFactors, GpuTensor cache,
        int position, float theta, float epsilon = 1e-6f)
    {
        Check(keys, gammaAndFactors, cache);
        int width = keys.Columns;
        int capacity = cache.Rows / 2;
        bool hasFactors = gammaAndFactors.Elements == width + width / 2;
        Require(width is 256 or 512 && cache.Columns == width && cache.Rows == capacity * 2
            && position >= 0 && keys.Rows <= capacity - position && (hasFactors || gammaAndFactors.Elements == width)
            && float.IsFinite(theta) && theta > 1 && float.IsFinite(epsilon) && epsilon > 0
            && !ReferenceEquals(cache, keys) && !ReferenceEquals(cache, gammaAndFactors),
            "Fused key cache writes need 256 or 512 key channels, valid RoPE constants and an independent cache range.");
        Add([new("NormalizeRotateCacheKey.v.ts", keys, gammaAndFactors, cache, new()
        {
            Width = (uint)width, Inner = (uint)width / 64, Offset = (uint)position,
            Mode = hasFactors ? 1u : 0u, Theta = theta, Epsilon = epsilon,
        }, (uint)keys.Rows)]);
        TrackTokenOffset(position, capacity, keys.Rows);
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

    /// <summary>Fused FP32 causal attention. K and V occupy the
    /// two equal row ranges in the cache tensor.</summary>
    public void FusedGroupedAttention(GpuTensor query, GpuTensor keyValue, GpuTensor output,
        int heads, int headWidth, int position, int window, bool wideKeyTile = false)
    {
        Check(query, keyValue, output);
        int capacity = keyValue.Rows / 2;
        Require(SameShape(query, output) && heads is > 0 and <= 64
            && headWidth is 256 or 512 && query.Columns == heads * headWidth
            && keyValue.Columns == headWidth && keyValue.Rows == capacity * 2
            && position >= 0 && query.Rows <= capacity - position && capacity <= 1024 && window is > 0 and <= 1024
            && !ReferenceEquals(query, output) && !ReferenceEquals(keyValue, output),
            "Fused causal attention requires up to 1024 KV rows and an independent output.");
        int count = Math.Min(position + query.Rows, window);
        Add([new(wideKeyTile ? "FusedGroupedAttention.Keys8" : "FusedGroupedAttention.v.ts", query, keyValue, output, new()
        {
            Width = (uint)headWidth, Offset = (uint)position, Height = (uint)heads, Mode = (uint)window,
            Rows = (uint)capacity, Inner = Groups(count, wideKeyTile ? 8 : 4),
        }, (uint)heads, (uint)query.Rows)]);
    }

    /// <summary>FP32 single-token attention with independent key tiles and a stable softmax merge.</summary>
    public void SplitDecodeAttention(GpuTensor query, GpuTensor keyValue, GpuTensor partials, GpuTensor output,
        int heads, int headWidth, int position, int window, int tileKeys)
    {
        Check(query, keyValue, partials, output);
        int capacity = keyValue.Rows / 2;
        Require(query.Rows == 1 && SameShape(query, output) && heads is > 0 and <= 64
            && headWidth is 256 or 512 && query.Columns == heads * headWidth
            && keyValue.Columns == headWidth && keyValue.Rows == capacity * 2 && capacity <= 1024
            && position >= 0 && position < capacity && window is > 0 and <= 1024 && tileKeys is 16 or 32 or 64 or 128
            && !ReferenceEquals(output, query) && !ReferenceEquals(output, keyValue)
            && !ReferenceEquals(partials, query) && !ReferenceEquals(partials, keyValue) && !ReferenceEquals(partials, output),
            "Split attention needs one query row, a bounded KV cache and independent partials and output.");
        int keys = Math.Min(position + 1, window);
        uint tiles = Groups(keys, tileKeys);
        Require(partials.Rows >= heads * tiles && partials.Columns == headWidth + 2,
            "Split attention needs one local maximum, denominator and value vector per tile and head.");
        Add([
            new("FusedGroupedAttention.SplitDecode", query, keyValue, partials, new()
            {
                Width = (uint)headWidth, Offset = (uint)(position + 1 - keys), Columns = (uint)keys,
                Rows = (uint)capacity, Stride = (uint)tileKeys, Inner = (uint)tileKeys / 8, Channels = tiles,
            }, (uint)heads, tiles),
            new("MergeAttentionPartials.v.ts", partials, partials, output, new()
            {
                Width = (uint)headWidth, Inner = tiles,
            }, (uint)heads),
        ]);
        TrackTokenAttention(position, capacity, heads, window, tileKeys, partials.Rows);
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

    /// <summary>Apply FP32 GELU to a gate and multiply by a region of another tensor.</summary>
    public void GeluGate(GpuTensor gate, GpuTensor values, GpuTensor output, int firstRow = 0, int firstColumn = 0)
    {
        Check(gate, values, output);
        Require(SameShape(gate, output) && firstRow >= 0 && firstColumn >= 0
            && firstRow + (long)gate.Rows <= values.Rows && firstColumn + (long)gate.Columns <= values.Columns
            && !ReferenceEquals(values, output), "GELU gate requires matching output and an independent input region.");
        uint groups = Groups(gate.Elements, 64);
        uint groupsX = Math.Min(groups, 65535);
        Add([new("DecoderPointwiseFp32.v.ts", gate, values, output, new()
        {
            Rows = (uint)gate.Rows, Columns = (uint)gate.Columns, Inner = (uint)values.Columns,
            Offset = (uint)firstRow, Width = (uint)firstColumn, Mode = 1, Stride = groupsX * 64,
        }, groupsX, (groups + groupsX - 1) / groupsX)]);
    }

    /// <summary>Project one row to a vocabulary slice using native GGUF Q6_K weights.</summary>
    public void ProjectQ6K(GpuTensor input, GpuTensor weights, GpuTensor output,
        int firstOutput = 0, int outputCount = 0, float softCap = 0)
        => ProjectQ6K(input, weights, output, PackedQ6Layout.RowMajor, firstOutput, outputCount, softCap);

    public void ProjectQ6K(GpuTensor input, GpuTensor weights, GpuTensor output,
        PackedQ6Layout layout, int firstOutput = 0, int outputCount = 0, float softCap = 0)
    {
        Check(input, output);
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        int count = outputCount == 0 ? output.Elements : outputCount;
        Require(input.Rows == 1 && input.Columns is > 0 and <= 8192 && input.Columns % 512 == 0
            && firstOutput >= 0 && count > 0 && (long)firstOutput + count <= output.Elements
            && float.IsFinite(softCap) && softCap >= 0
            && weights.Elements * 4L == count * (input.Columns / 256L) * 210
            && (layout != PackedQ6Layout.Interleaved8 || count % 8 == 0)
            && !ReferenceEquals(input, output), "Q6_K projection needs one input and aligned packed weight rows.");
        string shader = layout switch
        {
            PackedQ6Layout.RowMajor => "QuantizedQ6K.v.ts",
            PackedQ6Layout.RowMajorWord => "QuantizedQ6KWord.v.ts",
            PackedQ6Layout.Interleaved8 => "QuantizedQ6KWord.Interleaved8",
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
        int rowsPerGroup = layout == PackedQ6Layout.RowMajor ? 4 : 8;
        Add([new(shader, input, weights, output, new()
        {
            Columns = (uint)count, Width = (uint)input.Columns / 256, Offset = (uint)firstOutput, Alpha = softCap,
        }, Groups(count, rowsPerGroup))]);
    }

    /// <summary>Reduce a flat scores tensor to one FP32 score and exact FP32 index.</summary>
    public void ArgMax(GpuTensor input, GpuTensor partials, GpuTensor output)
    {
        Check(input, partials, output);
        uint groups = Groups(input.Elements, 1024);
        Require(groups <= 1024 && partials.Rows == groups && partials.Columns == 2
            && output.Rows == 1 && output.Columns == 2
            && !ReferenceEquals(input, partials) && !ReferenceEquals(partials, output),
            "ArgMax needs up to 1024 score tiles and a matching independent pair per tile.");
        Add([
            new("ArgMax.v.ts", input, input, partials, new() { Columns = (uint)input.Elements }, groups),
            new("ArgMax.v.ts", partials, partials, output, new() { Columns = groups, Mode = 1 }, 1),
        ]);
    }

    /// <summary>Map the bit pattern of each BF16 value in FP32 storage through a 65,536-entry table.</summary>
    public void LookupBFloat16(GpuTensor input, GpuTensor table, GpuTensor output)
    {
        Check(input, table, output);
        Require(SameShape(input, output) && table.Elements == 65536
            && !ReferenceEquals(table, output) && !ReferenceEquals(table, input),
            "BF16 lookup requires matching input/output shapes and a separate 65,536-entry table.");
        uint groups = Groups(input.Elements, 64);
        uint groupsX = Math.Min(groups, 65535);
        Add([new("BFloat16Lookup.v.ts", input, table, output, new()
        {
            Columns = (uint)input.Elements, Stride = groupsX * 64,
        }, groupsX, (groups + groupsX - 1) / groupsX)]);
    }

    public void Scale(GpuTensor input, GpuTensor output, float factor, bool roundBFloat16 = true)
    {
        Check(input, output);
        Require(SameShape(input, output) && float.IsFinite(factor), "Scale requires finite factor and matching shapes.");
        uint groups = Groups(input.Elements, 64);
        if (roundBFloat16)
        {
            Add([new("DecoderScale.v.ts", input, input, output, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)input.Columns, Alpha = factor,
            }, groups)]);
            return;
        }
        uint groupsX = Math.Min(groups, 65535);
        Add([new("DecoderPointwiseFp32.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Alpha = factor,
            Stride = groupsX * 64,
        }, groupsX, (groups + groupsX - 1) / groupsX)]);
    }
}
