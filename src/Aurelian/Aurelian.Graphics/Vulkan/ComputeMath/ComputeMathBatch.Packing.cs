namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
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
