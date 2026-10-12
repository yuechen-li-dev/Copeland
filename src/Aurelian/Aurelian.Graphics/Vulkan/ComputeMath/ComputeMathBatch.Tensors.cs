namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
    public void RoundBFloat16(GpuTensor input, GpuTensor output)
    {
        Check(input, output);
        Require(SameShape(input, output), "BF16 rounding preserves the FP32 storage shape.");
        Map(input, input, output, 0, new() { Rows = (uint)input.Rows, Columns = (uint)input.Columns });
    }

    public void Gather(GpuTensor input, GpuTensor output, int firstRow = 0, int firstColumn = 0)
    {
        Check(input, output);
        Require(firstRow >= 0 && firstColumn >= 0 && (long)firstRow + output.Rows <= input.Rows
            && (long)firstColumn + output.Columns <= input.Columns && !ReferenceEquals(input, output), "Gather region must fit its input and use separate storage.");
        Map(input, input, output, 1, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)output.Columns, Inner = (uint)input.Columns,
            Offset = (uint)firstRow, Height = (uint)firstColumn,
        });
    }

    public void ScatterRounded(GpuTensor input, GpuTensor output, int firstRow = 0, int firstColumn = 0)
    {
        ScatterRegion(input, output, firstRow, firstColumn, rounded: true);
    }

    public void Scatter(GpuTensor input, GpuTensor output, int firstRow = 0, int firstColumn = 0)
    {
        ScatterRegion(input, output, firstRow, firstColumn, rounded: false);
    }

    private void ScatterRegion(GpuTensor input, GpuTensor output, int firstRow, int firstColumn, bool rounded)
    {
        Check(input, output);
        Require(firstRow >= 0 && firstColumn >= 0 && (long)firstRow + input.Rows <= output.Rows
            && (long)firstColumn + input.Columns <= output.Columns && !ReferenceEquals(input, output), "Scatter region must fit its output and use separate storage.");
        Map(input, input, output, rounded ? 2u : 8u, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Inner = (uint)output.Columns,
            Offset = (uint)firstRow, Height = (uint)firstColumn,
        });
    }

    public void ModulateBFloat16(GpuTensor input, GpuTensor modulation, GpuTensor output, int firstOffset = 0)
    {
        Check(input, modulation, output);
        Require(SameShape(input, output) && firstOffset >= 0 && (long)firstOffset + input.Columns * 2 <= modulation.Elements
            && !ReferenceEquals(modulation, output), "Modulation requires shift/scale vectors and matching activation shapes.");
        Map(input, modulation, output, 3, new() { Rows = (uint)input.Rows, Columns = (uint)input.Columns, Offset = (uint)firstOffset });
    }

    /// <summary>Concatenate the branch payload and a gate vector in a flat scratch
    /// allocation. Any padding at the end is zero. This is not a matrix reshape.</summary>
    public void AppendGate(GpuTensor branch, GpuTensor modulation, GpuTensor packedScratch, int gateOffset)
    {
        Check(branch, modulation, packedScratch);
        Require(gateOffset >= 0 && (long)gateOffset + branch.Columns <= modulation.Elements
            && packedScratch.Elements >= branch.Elements + branch.Columns
            && !ReferenceEquals(branch, packedScratch) && !ReferenceEquals(modulation, packedScratch), "Gate packing needs independent scratch for the branch plus one gate row.");
        Map(branch, modulation, packedScratch, 7, new()
        {
            Rows = (uint)packedScratch.Rows, Columns = (uint)packedScratch.Columns, Inner = (uint)branch.Elements,
            Offset = (uint)gateOffset, Height = (uint)branch.Columns,
        });
    }

    public void GatedResidualBFloat16(GpuTensor input, GpuTensor packedBranchAndGate, GpuTensor output)
    {
        Check(input, packedBranchAndGate, output);
        Require(SameShape(input, output) && packedBranchAndGate.Elements >= input.Elements + input.Columns
            && !ReferenceEquals(packedBranchAndGate, output), "Gated residual needs the matching branch payload followed by one gate row.");
        Map(input, packedBranchAndGate, output, 4, new() { Rows = (uint)input.Rows, Columns = (uint)input.Columns });
    }

    public void ScaledResidual(GpuTensor input, GpuTensor packedBranchAndScale, GpuTensor output)
    {
        Check(input, packedBranchAndScale, output);
        Require(SameShape(input, output) && packedBranchAndScale.Elements >= input.Elements + input.Columns
            && !ReferenceEquals(packedBranchAndScale, output), "Scaled residual needs the branch payload followed by one channel-scale row.");
        Map(input, packedBranchAndScale, output, 9, new() { Rows = (uint)input.Rows, Columns = (uint)input.Columns });
    }

    /// <summary>Square patch-grid RoPE. Prefix tokens are unchanged; each head
    /// rotates its half-split pairs with Y/X frequencies in FP32.</summary>
    public void Rope2D(GpuTensor input, GpuTensor output, int grid, int prefixTokens = 5,
        int headWidth = 64, float theta = 100)
    {
        Check(input, output);
        Require(SameShape(input, output) && headWidth is 64 or 128 && input.Columns % headWidth == 0
            && grid is > 0 and <= 63 && prefixTokens >= 0 && input.Rows == prefixTokens + grid * grid
            && float.IsFinite(theta) && theta > 1 && !ReferenceEquals(input, output),
            "2D RoPE needs independent 64/128-wide heads, a square patch grid, explicit prefix tokens and theta > 1.");
        Map(input, input, output, 10, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Width = (uint)headWidth,
            Height = (uint)grid, Offset = (uint)prefixTokens, Theta = theta,
        });
    }

    public void AddBFloat16(GpuTensor a, GpuTensor b, GpuTensor output)
    {
        Check(a, b, output);
        Require(SameShape(a, b) && SameShape(a, output), "Rounded addition needs identical activation shapes.");
        Map(a, b, output, 5, new() { Rows = (uint)a.Rows, Columns = (uint)a.Columns });
    }

    public void Rope3D(GpuTensor input, GpuTensor output, int resolution = 16, int firstToken = 0)
    {
        Check(input, output);
        Require(SameShape(input, output) && input.Columns % 128 == 0 && resolution is >= 1 and <= 16
            && firstToken >= 0 && (long)firstToken + input.Rows <= resolution * resolution * resolution
            && !ReferenceEquals(input, output), "3D RoPE needs independent 128-wide heads and coordinates inside the explicit lattice.");
        Map(input, input, output, 6, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Width = 128,
            Height = (uint)resolution, Offset = (uint)firstToken,
        });
    }

    public void NormalizeHeadsBFloat16(GpuTensor input, GpuTensor gamma, GpuTensor output)
    {
        Check(input, gamma, output);
        Require(SameShape(input, output) && input.Columns % 128 == 0 && gamma.Elements == input.Columns
            && !ReferenceEquals(gamma, output),
            "Head normalization needs 128-wide heads and one gamma per channel.");
        uint heads = (uint)input.Elements / 128;
        uint groupsX = heads <= 65535 ? heads : 4096;
        Add([new("HeadNormalize.v.ts", input, gamma, output, new()
        {
            Rows = (uint)input.Rows, Columns = (uint)input.Columns, Height = groupsX,
        }, groupsX, (heads + groupsX - 1) / groupsX)]);
    }

    private void Map(GpuTensor input, GpuTensor auxiliary, GpuTensor output, uint operation, KernelParameters parameters)
    {
        parameters.Mode = operation;
        uint groups = Groups((int)(parameters.Rows * parameters.Columns), 64);
        uint groupsX = Math.Min(groups, 65535);
        parameters.Stride = groupsX * 64;
        Add([new("TensorMap.v.ts", input, auxiliary, output, parameters, groupsX, (groups + 65534) / 65535)]);
    }
}
