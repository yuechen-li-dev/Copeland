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

    /// <summary>Gather selected eight-way children from channel-major parent
    /// rows. Mapping stores parent row and XYZ child slot (X is the low bit).
    /// Repeat expands each source channel for the residual skip branch.</summary>
    public void SparseChannelToSpatial(GpuTensor input, GpuTensor mapping, GpuTensor output,
        int firstChild = 0, int? childCount = null, int firstParent = 0)
    {
        Check(input, output);
        Owner.Validate(mapping, GpuTensorFormat.UInt32);
        int count = childCount ?? output.Rows;
        Require(input.Columns % 8 == 0 && output.Columns % (input.Columns / 8) == 0
            && mapping.Rows == output.Rows && mapping.Columns == 2
            && count > 0 && firstChild >= 0 && (long)firstChild + count <= output.Rows
            && firstParent >= 0 && !ReferenceEquals(input, output),
            "Sparse channel-to-spatial needs typed parent/slot mapping, eight channel groups and an independent valid child range.");
        SparseMap("SparseChannelToSpatial.v.ts", input, mapping, output, new()
        {
            Rows = (uint)count, Columns = (uint)output.Columns, Inner = (uint)input.Columns,
            Offset = (uint)firstParent, Padding = (uint)firstChild, Height = (uint)input.Rows,
            Width = (uint)(output.Columns / (input.Columns / 8)),
        });
    }
}
