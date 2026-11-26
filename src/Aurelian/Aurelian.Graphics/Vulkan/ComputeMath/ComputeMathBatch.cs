using System.Runtime.InteropServices;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public enum RowReduction { Sum, Maximum, Softmax }
public enum MatrixKernel { Portable16, Resident64 }
public enum Normalization { RmsNorm, LayerNorm }
public enum TensorActivation { Silu, Gelu, SiluGate, Residual, Bias, GeluErf, GeluGate, BiasSilu, BiasGelu, BiasResidual }
public enum RopeLayout { HalfSplit, Interleaved }

[StructLayout(LayoutKind.Sequential)]
internal struct KernelParameters
{
    public uint Rows, Columns, Inner, Offset;
    public uint Mode, Width, Height, Channels;
    public uint KernelWidth, KernelHeight, Padding, Stride;
    public float Alpha, Beta, Epsilon, Theta;
}

internal sealed record ComputeMathDispatch(string Shader, GpuTensor Input, GpuTensor Auxiliary,
    GpuTensor Output, KernelParameters Parameters, uint GroupsX, uint GroupsY = 1)
{
    public bool WritesAuxiliary => Shader == "DecodeQkv.v.ts";
    public TokenReplayUpdate? TokenUpdate { get; init; }
}

/// <summary>A bounded dispatch plan with immutable shape-checked steps.
/// Execute it on its owning VulkanComputeMath; intermediate tensors never require readback.</summary>
public sealed partial class ComputeMathBatch
{
    internal const int MatrixKChunk = 128;
    internal VulkanComputeMath Owner { get; }
    internal List<ComputeMathDispatch> Dispatches { get; } = [];
    public int DispatchCount => Dispatches.Count;

    internal ComputeMathBatch(VulkanComputeMath owner) => Owner = owner;

    public void Multiply(GpuTensor a, GpuTensor b, GpuTensor output,
        float alpha = 1, float beta = 0, bool transposeB = false)
    {
        Multiply(a, b, output, MatrixKernel.Portable16, alpha, beta, transposeB);
    }

    public void Multiply(GpuTensor a, GpuTensor b, GpuTensor output, MatrixKernel kernel,
        float alpha = 1, float beta = 0, bool transposeB = false)
    {
        Add(MatrixDispatches(a, b, output, alpha, beta, transposeB, kernel));
    }

    public void MultiplyCooperative(GpuTensor packedA, GpuTensor packedB, GpuTensor output)
    {
        MultiplyCooperativeCore(packedA, packedB, output, 0);
    }

    /// <summary>Loads packed matrix fragments directly and reuses each fragment across
    /// adjacent output tiles when requested. The reuse path requires 32-aligned M/N.</summary>
    public void ProjectCooperativeTiles(GpuTensor packedA, GpuTensor packedB, GpuTensor output, bool reuseTiles)
    {
        MultiplyCooperativeCore(packedA, packedB, output, reuseTiles ? 2 : 1);
    }

    private void MultiplyCooperativeCore(GpuTensor packedA, GpuTensor packedB, GpuTensor output, int tiles)
    {
        Owner.Validate(packedA, GpuTensorFormat.PackedFloat16);
        Owner.Validate(packedB, GpuTensorFormat.PackedFloat16);
        Owner.Validate(output, GpuTensorFormat.Float32);
        Require(Owner.CooperativeMatrixEnabled, "Cooperative FP16 matmul requires an enabled audited Vulkan tuple; use explicit FP32 Multiply otherwise.");
        Require(packedA.Columns == packedB.Rows && output.Rows == packedA.Rows && output.Columns == packedB.Columns
            && packedA.Rows % 16 == 0 && packedA.Columns % 16 == 0 && packedB.Columns % 16 == 0
            && packedA.Columns <= 4096,
            "Cooperative matmul requires 16-aligned M/N/K, K <= 4096, packed FP16 A/B and FP32 C. Tails are rejected.");
        uint tileWidth = tiles == 2 ? 32u : 16u;
        Require(packedA.Rows % tileWidth == 0 && packedB.Columns % tileWidth == 0,
            "Cooperative projection dimensions must cover whole output tiles.");
        string shader = tiles == 0 ? "CooperativeSgemm.v.ts" : "CooperativeProjection.v.ts";
        Add([new(shader, packedA, packedB, output, new()
        {
            Rows = (uint)packedA.Rows, Columns = (uint)packedB.Columns, Inner = (uint)packedA.Columns, Mode = (uint)tiles,
        }, (uint)packedA.Rows / tileWidth, (uint)packedB.Columns / tileWidth)]);
    }

    private ComputeMathDispatch[] MatrixDispatches(GpuTensor a, GpuTensor b, GpuTensor output,
        float alpha, float beta, bool transposeB, MatrixKernel kernel = MatrixKernel.Portable16)
    {
        Check(a, b, output);
        int columns = transposeB ? b.Rows : b.Columns;
        int inner = transposeB ? b.Columns : b.Rows;
        Require(Enum.IsDefined(kernel) && a.Columns == inner && output.Rows == a.Rows && output.Columns == columns
            && a.Columns <= 8192 && float.IsFinite(alpha) && float.IsFinite(beta),
            "SGEMM requires A[M,K], B[K,N] (or transposed B[N,K]), C[M,N], K <= 8192 and finite scales.");
        Require(!ReferenceEquals(output, a) && !ReferenceEquals(output, b), "SGEMM output cannot alias either input.");
        var steps = new List<ComputeMathDispatch>();
        int tileRows = kernel == MatrixKernel.Resident64 ? 64 : 16;
        int tileColumns = kernel == MatrixKernel.Portable16 ? 16 : 64;
        string shader = kernel switch
        {
            MatrixKernel.Resident64 => "ResidentSgemm.v.ts",
            _ => "Sgemm.v.ts",
        };
        int chunk = kernel == MatrixKernel.Portable16 ? MatrixKChunk : inner;
        for (int offset = 0; offset < inner; offset += chunk)
        {
            steps.Add(new(shader, a, b, output, new()
            {
                Rows = (uint)a.Rows, Columns = (uint)columns, Inner = (uint)inner,
                Offset = (uint)offset, Mode = transposeB ? 1u : 0u, Alpha = alpha, Beta = beta,
                KernelWidth = Groups(Math.Min(inner - offset, chunk), 16),
            }, Groups(a.Rows, tileRows), Groups(columns, tileColumns)));
        }
        return steps.ToArray();
    }

    public void ReduceRows(GpuTensor input, GpuTensor output, RowReduction operation = RowReduction.Softmax)
    {
        Check(input, output);
        Require(Enum.IsDefined(operation) && input.Columns <= 8192 && output.Rows == input.Rows
            && output.Columns == (operation == RowReduction.Softmax ? input.Columns : 1),
            "Row reductions require width <= 8192; sum/max return [rows,1], softmax preserves shape.");
        Require(!ReferenceEquals(input, output), "Row reduction output must have its own allocation.");
        Add([new("RowReduction.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation,
        }, (uint)input.Rows)]);
    }

    public void Normalize(GpuTensor input, GpuTensor gammaBeta, GpuTensor output,
        Normalization operation = Normalization.RmsNorm, float epsilon = 1e-5f)
    {
        Normalize(input, gammaBeta, output, operation, epsilon, specializeWidth: Owner.UseWidthSpecializedLayerNorm);
    }

    public void Normalize(GpuTensor input, GpuTensor gammaBeta, GpuTensor output,
        Normalization operation, float epsilon, bool specializeWidth)
    {
        string shader = "Normalization.v.ts";
        if (specializeWidth && operation == Normalization.LayerNorm)
        {
            shader = input.Columns switch
            {
                384 => "LayerNorm384",
                768 => "LayerNorm768.v.ts",
                _ => shader,
            };
        }
        NormalizeCore(input, gammaBeta, output, operation, epsilon, shader);
    }

    public void LayerNorm768(GpuTensor input, GpuTensor gammaBeta, GpuTensor output, float epsilon = 1e-5f)
    {
        Require(input.Columns == 768, "LayerNorm768 requires exactly 768 columns.");
        Normalize(input, gammaBeta, output, Normalization.LayerNorm, epsilon, specializeWidth: true);
    }

    private void NormalizeCore(GpuTensor input, GpuTensor gammaBeta, GpuTensor output,
        Normalization operation, float epsilon, string shader)
    {
        Check(input, gammaBeta, output);
        Require(Enum.IsDefined(operation) && input.Columns <= 8192 && SameShape(input, output)
            && gammaBeta.Elements == input.Columns * (operation == Normalization.LayerNorm ? 2 : 1)
            && float.IsFinite(epsilon) && epsilon > 0,
            "Normalization needs width <= 8192, matching output, positive epsilon and gamma (plus beta for LayerNorm).");
        Require(!ReferenceEquals(output, gammaBeta), "Normalization output cannot overwrite its weights.");
        uint groupsX = (uint)Math.Min(input.Rows, 4096);
        Add([new(shader, input, gammaBeta, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation,
            Epsilon = epsilon, Height = groupsX,
        }, groupsX, ((uint)input.Rows + groupsX - 1) / groupsX)]);
    }

    public void Activate(GpuTensor input, GpuTensor output, TensorActivation operation, GpuTensor? auxiliary = null)
    {
        if (operation is TensorActivation.BiasGelu or TensorActivation.BiasResidual)
        {
            Require(auxiliary is not null, "A fused projection epilogue requires explicit bias weights.");
        }
        auxiliary ??= input;
        Check(input, auxiliary, output);
        Require(Enum.IsDefined(operation) && SameShape(input, output), "Activation requires matching FP32 shapes and a defined operation.");
        if (operation is TensorActivation.SiluGate or TensorActivation.Residual or TensorActivation.GeluGate)
        {
            Require(SameShape(input, auxiliary), "Gated/residual activation requires a same-shaped auxiliary tensor.");
        }
        if (operation is TensorActivation.Bias or TensorActivation.BiasSilu or TensorActivation.BiasGelu or TensorActivation.BiasResidual)
        {
            Require(auxiliary.Elements == input.Columns && !ReferenceEquals(auxiliary, output), "Bias requires one independent row of channel weights.");
        }
        if (operation == TensorActivation.BiasSilu)
        {
            uint groups = Groups(input.Elements, 1024);
            uint groupsX = Math.Min(groups, 65535);
            Add([new("BiasSilu.v.ts", input, auxiliary, output, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)input.Columns, Height = groupsX,
            }, groupsX, (groups + groupsX - 1) / groupsX)]);
            return;
        }
        string shader = operation is TensorActivation.BiasGelu or TensorActivation.BiasResidual
            ? "ProjectionEpilogue.v.ts" : "Activation.v.ts";
        Add([new(shader, input, auxiliary, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation,
            Stride = Math.Min(Groups(input.Elements, 64), 65535) * 64,
        }, Math.Min(Groups(input.Elements, 64), 65535), (Groups(input.Elements, 64) + 65534) / 65535)]);
    }

    public void RotatePositions(GpuTensor input, GpuTensor output, int positionOffset = 0,
        float theta = 10000, RopeLayout layout = RopeLayout.HalfSplit)
    {
        Check(input, output);
        Require(SameShape(input, output) && input.Columns % 2 == 0 && positionOffset is >= 0 and <= 1000000
            && float.IsFinite(theta) && theta > 1 && Enum.IsDefined(layout),
            "RoPE needs even matching widths, an offset of 0–1000000, theta > 1 and a defined layout.");
        Add([new("Rope.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Offset = (uint)positionOffset,
            Theta = theta, Mode = (uint)layout,
        }, Groups(input.Elements / 2, 64))]);
    }

    public void Fft(GpuTensor input, GpuTensor output, GpuTensor scratch, bool inverse = false)
    {
        Owner.Validate(input, GpuTensorFormat.Complex32);
        Owner.Validate(output, GpuTensorFormat.Complex32);
        Owner.Validate(scratch, GpuTensorFormat.Complex32);
        Require(SameShape(input, output) && SameShape(input, scratch)
            && (input.Columns & (input.Columns - 1)) == 0 && input.Columns <= 65536,
            "FFT requires matching complex32 batches of power-of-two lengths <= 65536.");
        Require(!ReferenceEquals(input, output) && !ReferenceEquals(input, scratch) && !ReferenceEquals(output, scratch),
            "FFT input, output and scratch must be three distinct tensors.");
        int bits = System.Numerics.BitOperations.Log2((uint)input.Columns);
        var current = bits % 2 == 0 ? output : scratch;
        var steps = new List<ComputeMathDispatch>
        {
            new("FftBitReverse.v.ts", input, input, current, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)input.Columns, Offset = (uint)bits,
            }, Groups(input.Elements, 64)),
        };
        for (int half = 1; half < input.Columns; half *= 2)
        {
            var next = ReferenceEquals(current, output) ? scratch : output;
            steps.Add(new("FftButterfly.v.ts", current, current, next, new()
            {
                Rows = (uint)input.Rows, Columns = (uint)input.Columns, Offset = (uint)half,
                Mode = inverse ? 1u : 0u,
                Alpha = inverse && half * 2 == input.Columns ? 1f / input.Columns : 1,
            }, Groups(input.Elements, 64)));
            current = next;
        }
        Add(steps);
    }

    public void Convolve(GpuTensor image, GpuTensor weights, GpuTensor columns, GpuTensor output,
        int width, int height, int channels, int kernelWidth, int kernelHeight, int padding = 0, int stride = 1)
    {
        ConvolveImage(image, weights, columns, output, width, height, channels, kernelWidth, kernelHeight,
            padding, padding, stride);
    }

    public void Convolve1D(GpuTensor input, GpuTensor weights, GpuTensor columns, GpuTensor output,
        int kernelWidth, int padding = 0, int stride = 1)
    {
        ConvolveImage(input, weights, columns, output, input.Rows, 1, input.Columns, kernelWidth, 1,
            padding, 0, stride);
    }

    private void ConvolveImage(GpuTensor image, GpuTensor weights, GpuTensor columns, GpuTensor output,
        int width, int height, int channels, int kernelWidth, int kernelHeight, int paddingX, int paddingY, int stride)
    {
        Check(image, weights, columns, output);
        Require(!ReferenceEquals(columns, weights), "Im2col scratch cannot overwrite weights.");
        var expansion = Im2ColDispatch(image, columns, width, height, channels, kernelWidth, kernelHeight, paddingX, paddingY, stride);
        var matrix = MatrixDispatches(columns, weights, output, 1, 0, false);
        Add([expansion, .. matrix]);
    }

    public void Im2Col1D(GpuTensor image, GpuTensor columns, int kernelWidth, int padding, int stride)
    {
        Check(image, columns);
        Add([Im2ColDispatch(image, columns, image.Rows, 1, image.Columns, kernelWidth, 1, padding, 0, stride)]);
    }

    private static ComputeMathDispatch Im2ColDispatch(GpuTensor image, GpuTensor columns, int width, int height,
        int channels, int kernelWidth, int kernelHeight, int paddingX, int paddingY, int stride)
    {
        Require(width is > 0 and <= 4096 && height is > 0 and <= 4096 && channels is > 0 and <= 1024
            && kernelWidth is > 0 and <= 15 && kernelHeight is > 0 and <= 15
            && paddingX is >= 0 and <= 32 && paddingY is >= 0 and <= 32 && stride is > 0 and <= 16,
            "Convolution dimensions, kernel, padding or stride are outside the bounded NHWC profile.");
        int outputWidth = (width + 2 * paddingX - kernelWidth) / stride + 1;
        int outputHeight = (height + 2 * paddingY - kernelHeight) / stride + 1;
        int inner = kernelWidth * kernelHeight * channels;
        Require(width + 2 * paddingX >= kernelWidth && height + 2 * paddingY >= kernelHeight
            && image.Elements == width * height * channels
            && columns.Rows == outputWidth * outputHeight && columns.Columns == inner,
            "Convolution needs NHWC input and correctly sized [outputPixels,kernelElements] im2col scratch.");
        Require(!ReferenceEquals(columns, image), "Im2col scratch cannot overwrite its input.");
        return new("Im2Col.v.ts", image, image, columns, new()
        {
            Rows = (uint)columns.Rows, Columns = (uint)outputWidth, Inner = (uint)inner,
            Width = (uint)width, Height = (uint)height, Channels = (uint)channels,
            KernelWidth = (uint)kernelWidth, KernelHeight = (uint)kernelHeight,
            Padding = (uint)paddingX, Offset = (uint)paddingY, Stride = (uint)stride,
        }, Groups(columns.Elements, 64));
    }

    public void Attention(GpuTensor query, GpuTensor key, GpuTensor value, GpuTensor scores,
        GpuTensor probabilities, GpuTensor output, bool causal = true, int positionOffset = 0)
    {
        Check(query, key, value, scores, probabilities, output);
        Require(query.Columns == key.Columns && key.Rows == value.Rows && key.Rows <= 1024
            && SameShape(scores, probabilities) && positionOffset is >= 0 and <= 1000000,
            "Attention needs compatible Q/K/V, score/probability scratch, <= 1024 keys and a bounded causal offset.");
        Require(!ReferenceEquals(scores, probabilities) && !ReferenceEquals(probabilities, value)
            && !ReferenceEquals(probabilities, output) && !ReferenceEquals(scores, value),
            "Attention intermediates cannot alias still-live inputs or each other.");
        var scoresPlan = MatrixDispatches(query, key, scores, 1f / MathF.Sqrt(query.Columns), 0, true);
        var resultPlan = MatrixDispatches(probabilities, value, output, 1, 0, false);
        var steps = new List<ComputeMathDispatch>(scoresPlan);
        if (causal)
        {
            steps.Add(new("AttentionMask.v.ts", scores, scores, scores, new()
            {
                Rows = (uint)scores.Rows, Columns = (uint)scores.Columns, Offset = (uint)positionOffset,
            }, Groups(scores.Elements, 64)));
        }
        steps.Add(new("RowReduction.v.ts", scores, scores, probabilities, new()
        {
            Rows = (uint)scores.Rows, Columns = (uint)scores.Columns, Mode = (uint)RowReduction.Softmax,
        }, (uint)scores.Rows));
        steps.AddRange(resultPlan);
        Add(steps);
    }

    private void Add(IEnumerable<ComputeMathDispatch> source)
    {
        var steps = source.ToArray();
        int limit = TokenReplayCapacity == 0 ? Owner.MaximumDispatches : MaximumTokenReplayDispatches;
        Require(Dispatches.Count + steps.Length <= limit, "Batch exceeds its declared dispatch budget.");
        Require(steps.All(step => step.GroupsX is > 0 and <= 65535 && step.GroupsY is > 0 and <= 65535),
            "Dispatch dimensions exceed the portable Vulkan workgroup range; tile the operation.");
        Dispatches.AddRange(steps);
    }

    private void Check(params GpuTensor[] tensors)
    {
        foreach (var tensor in tensors)
        {
            Owner.Validate(tensor, GpuTensorFormat.Float32);
        }
    }

    private static bool SameShape(GpuTensor a, GpuTensor b) => a.Rows == b.Rows && a.Columns == b.Columns;
    private static uint Groups(int count, int width) => (uint)((count + width - 1) / width);
    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException("AUR-COMPUTE-MATH-001: " + message);
        }
    }
}
