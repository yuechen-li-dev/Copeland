namespace Aurelian.Graphics.Vulkan.ComputeMath;

public enum PackedQ4Layout
{
    RowMajor,
    /// <summary>Eight output rows with binary16 scale pairs followed by four nibble planes.</summary>
    Interleaved8,
}

public enum PackedQ6Layout
{
    RowMajor,
    RowMajorWord,
    Interleaved8,
}

public sealed partial class ComputeMathBatch
{
    private static long PackedQ4Words(int outputColumns, int inputWidth, PackedQ4Layout layout)
        => layout == PackedQ4Layout.Interleaved8
            ? (long)outputColumns * inputWidth / 256 * 36
            : (long)outputColumns * inputWidth / 32 * 5;

    private static bool PackedQ4Contains(GpuTensor weights, int inputWidth, PackedQ4Layout layout,
        int firstWeightRow, int outputColumns, int weightRows)
    {
        return Enum.IsDefined(layout) && firstWeightRow >= 0 && (long)firstWeightRow + outputColumns <= weightRows
            && (layout == PackedQ4Layout.RowMajor || (firstWeightRow % 8 == 0 && weightRows % 8 == 0))
            && weights.Elements == PackedQ4Words(weightRows, inputWidth, layout);
    }

    /// <summary>Decode a K slice of packed [output,input] Q4B32 weights into
    /// transposed binary16 storage for cooperative projection.</summary>
    public void PackQ4WeightsHalf(GpuTensor weights, GpuTensor output, int inputWidth, int firstInput = 0)
        => PackQ4WeightsHalf(weights, output, inputWidth, firstInput, PackedQ4Layout.RowMajor);

    public void PackQ4WeightsHalf(GpuTensor weights, GpuTensor output, int inputWidth, int firstInput, PackedQ4Layout layout)
        => PackQ4WeightsHalf(weights, output, inputWidth, firstInput, layout, 0, output.Columns);

    public void PackQ4WeightsHalf(GpuTensor weights, GpuTensor output, int inputWidth, int firstInput, PackedQ4Layout layout,
        int firstWeightRow, int weightRows)
    {
        Owner.Validate(weights, GpuTensorFormat.UInt32);
        Owner.Validate(output, GpuTensorFormat.PackedFloat16);
        Require(inputWidth > 0 && inputWidth % 32 == 0 && firstInput >= 0 && firstInput % 16 == 0
            && output.Rows % 16 == 0 && output.Columns % 16 == 0
            && firstInput + (long)output.Rows <= inputWidth
            && PackedQ4Contains(weights, inputWidth, layout, firstWeightRow, output.Columns, weightRows),
            "Q4 weight packing needs complete aligned weights and an in-range K tile.");
        string shader = layout == PackedQ4Layout.Interleaved8
            ? "PackQ4WeightsHalf.InterleavedHalfScale" : "PackQ4WeightsHalf.v.ts";
        Add([new(shader, weights, weights, output, new()
        {
            Columns = (uint)output.Columns, Inner = (uint)inputWidth, Offset = (uint)firstInput, Height = (uint)firstWeightRow,
        }, (uint)output.Rows / 16, (uint)output.Columns / 16)]);
    }

    /// <summary>Explicit FP16 precision conversion, zero row padding, and an
    /// optional transpose of streamed [output,input,bias] weights.</summary>
    public void PackMatrixHalf(GpuTensor input, GpuTensor output, int firstColumn = 0, bool transposeWeights = false)
    {
        Owner.Validate(input, GpuTensorFormat.Float32);
        Owner.Validate(output, GpuTensorFormat.PackedFloat16);
        Require(output.Rows % 16 == 0 && output.Columns % 16 == 0 && firstColumn >= 0,
            "FP16 packing needs aligned output dimensions and a nonnegative column offset.");
        if (transposeWeights)
        {
            Require(output.Columns == input.Rows && (long)firstColumn + output.Rows <= input.Columns - 1,
                "Transposed packing must cover a selected weight slice, excluding its appended bias.");
        }
        else
        {
            Require(output.Rows >= input.Rows && output.Rows < input.Rows + 16
                && (long)firstColumn + output.Columns <= input.Columns,
                "Activation packing permits only final row padding and an in-range column slice.");
        }
        uint groups = Groups(output.Elements / 2, 64);
        uint groupsX = Math.Min(groups, 65535);
        Add([new("PackMatrixHalf.v.ts", input, input, output, new()
        {
            Rows = (uint)output.Rows,
            Columns = (uint)output.Columns,
            Inner = (uint)input.Columns,
            Height = (uint)input.Rows,
            Offset = (uint)firstColumn,
            Mode = transposeWeights ? 1u : 0u,
            Stride = groupsX * 64,
        }, groupsX, (groups + groupsX - 1) / groupsX)]);
    }

    internal void LinearEpilogue(GpuTensor input, GpuTensor weights, GpuTensor output,
        LinearWeightTile tile, bool accumulate, bool addBias)
    {
        Check(input, weights, output);
        Require(input.Rows >= output.Rows && input.Columns == weights.Rows && tile.OutputCount <= input.Columns
            && (long)tile.FirstOutput + tile.OutputCount <= output.Columns, "Linear epilogue dimensions do not match.");
        uint groups = Groups(output.Rows * tile.OutputCount, 64);
        uint groupsX = Math.Min(groups, 65535);
        Add([new("LinearEpilogue.v.ts", input, weights, output, new()
        {
            Rows = (uint)output.Rows,
            Columns = (uint)tile.OutputCount,
            Inner = (uint)input.Columns,
            Offset = (uint)tile.FirstOutput,
            Width = (uint)output.Columns,
            Channels = (uint)weights.Columns,
            Mode = accumulate ? 1u : 0u,
            Padding = addBias ? 1u : 0u,
            Stride = groupsX * 64,
        }, groupsX, (groups + groupsX - 1) / groupsX)]);
    }
}
