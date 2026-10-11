using System.Runtime.InteropServices;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public enum RowReduction { Sum, Maximum, Softmax }
public enum Normalization { RmsNorm, LayerNorm }
public enum TensorActivation { Silu, Gelu, SiluGate, Residual, Bias }
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
    GpuTensor Output, KernelParameters Parameters, uint GroupsX, uint GroupsY = 1);

/// <summary>A bounded dispatch plan with immutable shape-checked steps.
/// Execute it on its owning VulkanComputeMath; intermediate tensors never require readback.</summary>
public sealed class ComputeMathBatch
{
    internal VulkanComputeMath Owner { get; }
    internal List<ComputeMathDispatch> Dispatches { get; } = [];
    public int DispatchCount => Dispatches.Count;

    internal ComputeMathBatch(VulkanComputeMath owner) => Owner = owner;

    public void Multiply(GpuTensor a, GpuTensor b, GpuTensor output,
        float alpha = 1, float beta = 0, bool transposeB = false)
    {
        Add(MatrixDispatches(a, b, output, alpha, beta, transposeB));
    }

    public void MultiplyCooperative(GpuTensor packedA, GpuTensor packedB, GpuTensor output)
    {
        Owner.Validate(packedA, GpuTensorFormat.PackedFloat16);
        Owner.Validate(packedB, GpuTensorFormat.PackedFloat16);
        Owner.Validate(output, GpuTensorFormat.Float32);
        Require(Owner.CooperativeMatrixEnabled, "Cooperative FP16 matmul requires an enabled audited Vulkan tuple; use explicit FP32 Multiply otherwise.");
        Require(packedA.Columns == packedB.Rows && output.Rows == packedA.Rows && output.Columns == packedB.Columns
            && packedA.Rows % 16 == 0 && packedA.Columns % 16 == 0 && packedB.Columns % 16 == 0
            && packedA.Columns <= 4096,
            "Cooperative matmul requires 16-aligned M/N/K, K <= 4096, packed FP16 A/B and FP32 C. Tails are rejected.");
        Add([new("CooperativeSgemm.v.ts", packedA, packedB, output, new()
        {
            Rows = (uint)packedA.Rows, Columns = (uint)packedB.Columns, Inner = (uint)packedA.Columns,
        }, (uint)packedA.Rows / 16, (uint)packedB.Columns / 16)]);
    }

    private ComputeMathDispatch[] MatrixDispatches(GpuTensor a, GpuTensor b, GpuTensor output,
        float alpha, float beta, bool transposeB)
    {
        Check(a, b, output);
        int columns = transposeB ? b.Rows : b.Columns;
        int inner = transposeB ? b.Columns : b.Rows;
        Require(a.Columns == inner && output.Rows == a.Rows && output.Columns == columns
            && a.Columns <= 4096 && float.IsFinite(alpha) && float.IsFinite(beta),
            "SGEMM requires A[M,K], B[K,N] (or transposed B[N,K]), C[M,N], K <= 4096 and finite scales.");
        Require(!ReferenceEquals(output, a) && !ReferenceEquals(output, b), "SGEMM output cannot alias either input.");
        var steps = new List<ComputeMathDispatch>();
        for (int offset = 0; offset < inner; offset += 16)
        {
            steps.Add(new("Sgemm.v.ts", a, b, output, new()
            {
                Rows = (uint)a.Rows, Columns = (uint)columns, Inner = (uint)inner,
                Offset = (uint)offset, Mode = transposeB ? 1u : 0u, Alpha = alpha, Beta = beta,
            }, Groups(a.Rows, 16), Groups(columns, 16)));
        }
        return steps.ToArray();
    }

    public void ReduceRows(GpuTensor input, GpuTensor output, RowReduction operation = RowReduction.Softmax)
    {
        Check(input, output);
        Require(Enum.IsDefined(operation) && input.Columns <= 1024 && output.Rows == input.Rows
            && output.Columns == (operation == RowReduction.Softmax ? input.Columns : 1),
            "Row reductions require width <= 1024; sum/max return [rows,1], softmax preserves shape.");
        Require(!ReferenceEquals(input, output), "Row reduction output must have its own allocation.");
        Add([new("RowReduction.v.ts", input, input, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation,
        }, (uint)input.Rows)]);
    }

    public void Normalize(GpuTensor input, GpuTensor gammaBeta, GpuTensor output,
        Normalization operation = Normalization.RmsNorm, float epsilon = 1e-5f)
    {
        Check(input, gammaBeta, output);
        Require(Enum.IsDefined(operation) && input.Columns <= 1024 && SameShape(input, output)
            && gammaBeta.Elements == input.Columns * (operation == Normalization.LayerNorm ? 2 : 1)
            && float.IsFinite(epsilon) && epsilon > 0,
            "Normalization needs width <= 1024, matching output, positive epsilon and gamma (plus beta for LayerNorm).");
        Require(!ReferenceEquals(output, gammaBeta), "Normalization output cannot overwrite its weights.");
        Add([new("Normalization.v.ts", input, gammaBeta, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation, Epsilon = epsilon,
        }, (uint)input.Rows)]);
    }

    public void Activate(GpuTensor input, GpuTensor output, TensorActivation operation, GpuTensor? auxiliary = null)
    {
        auxiliary ??= input;
        Check(input, auxiliary, output);
        Require(Enum.IsDefined(operation) && SameShape(input, output), "Activation requires matching FP32 shapes and a defined operation.");
        if (operation is TensorActivation.SiluGate or TensorActivation.Residual)
        {
            Require(SameShape(input, auxiliary), "Gated/residual activation requires a same-shaped auxiliary tensor.");
        }
        if (operation == TensorActivation.Bias)
        {
            Require(auxiliary.Elements == input.Columns && !ReferenceEquals(auxiliary, output), "Bias requires one independent row of channel weights.");
        }
        Add([new("Activation.v.ts", input, auxiliary, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Mode = (uint)operation,
        }, Groups(input.Elements, 64))]);
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
        Check(image, weights, columns, output);
        Require(width is > 0 and <= 4096 && height is > 0 and <= 4096 && channels is > 0 and <= 1024
            && kernelWidth is > 0 and <= 15 && kernelHeight is > 0 and <= 15
            && padding is >= 0 and <= 32 && stride is > 0 and <= 16,
            "Convolution dimensions, kernel, symmetric padding or stride are outside the bounded NHWC profile.");
        int outputWidth = (width + 2 * padding - kernelWidth) / stride + 1;
        int outputHeight = (height + 2 * padding - kernelHeight) / stride + 1;
        int inner = kernelWidth * kernelHeight * channels;
        Require(width + 2 * padding >= kernelWidth && height + 2 * padding >= kernelHeight
            && image.Elements == width * height * channels
            && columns.Rows == outputWidth * outputHeight && columns.Columns == inner,
            "Convolution needs NHWC input and correctly sized [outputPixels,kernelElements] im2col scratch.");
        Require(!ReferenceEquals(columns, image) && !ReferenceEquals(columns, weights), "Im2col scratch cannot overwrite inputs.");
        var matrix = MatrixDispatches(columns, weights, output, 1, 0, false);
        Add([new("Im2Col.v.ts", image, image, columns, new()
        {
            Rows = (uint)columns.Rows, Columns = (uint)outputWidth, Inner = (uint)inner,
            Width = (uint)width, Height = (uint)height, Channels = (uint)channels,
            KernelWidth = (uint)kernelWidth, KernelHeight = (uint)kernelHeight,
            Padding = (uint)padding, Stride = (uint)stride,
        }, Groups(columns.Elements, 64)), .. matrix]);
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
        Require(Dispatches.Count + steps.Length <= Owner.MaximumDispatches, "Batch exceeds its declared dispatch budget.");
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
