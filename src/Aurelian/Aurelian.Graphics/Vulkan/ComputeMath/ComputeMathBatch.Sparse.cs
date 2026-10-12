namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
    public void RopeCoordinates3D(GpuTensor input, GpuTensor coordinates, GpuTensor output)
    {
        Check(input, coordinates, output);
        Require(SameShape(input, output) && input.Columns % 128 == 0
            && coordinates.Rows == input.Rows && coordinates.Columns == 3
            && !ReferenceEquals(input, output) && !ReferenceEquals(coordinates, output),
            "Coordinate RoPE requires independent 128-wide heads and explicit XYZ coordinates per token.");
        SparseMap("CoordinateRope.v.ts", input, coordinates, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns,
        });
    }

    /// <summary>Gather submanifold neighborhoods into kernel-major channel rows.
    /// uint.MaxValue represents missing cells; out-of-range entries are zeroed.
    /// CPU topology construction defines the 27 XYZ offsets and row identity.</summary>
    public void SparseColumns(GpuTensor input, GpuTensor neighbors, GpuTensor output, int firstRow = 0)
    {
        Check(input, output);
        Owner.Validate(neighbors, GpuTensorFormat.UInt32);
        Require(neighbors.Rows == input.Rows && neighbors.Columns == 27
            && input.Columns <= 1024 && output.Columns == input.Columns * 27
            && firstRow >= 0 && (long)firstRow + output.Rows <= input.Rows
            && !ReferenceEquals(input, output),
            "Sparse im2col requires matching typed 27-neighbor rows and a bounded independent output range.");
        SparseMap("SparseColumns.v.ts", input, neighbors, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)output.Columns,
            Inner = (uint)input.Columns, Offset = (uint)firstRow, Height = (uint)input.Rows,
        });
    }

    private void SparseMap(string shader, GpuTensor input, GpuTensor auxiliary, GpuTensor output, KernelParameters parameters)
    {
        uint groups = Groups((int)(parameters.Rows * parameters.Columns), 64);
        uint groupsX = Math.Min(groups, 65535);
        parameters.Stride = groupsX * 64;
        Add([new(shader, input, auxiliary, output, parameters, groupsX, (groups + 65534) / 65535)]);
    }
}
