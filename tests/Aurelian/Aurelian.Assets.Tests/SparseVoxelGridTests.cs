using Aurelian.Assets.Tensors;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class SparseVoxelGridTests
{
    [Fact]
    public void PoolingPreservesPositiveChildrenAndNeighborhoodsHaveReciprocalIndices()
    {
        float[] logits = Enumerable.Repeat(-2f, 64).ToArray();
        logits[0] = 1;
        logits[3] = 2;
        logits[62] = 3;
        var grid = SparseVoxelGrid.FromLogits(logits, 4, 2);
        Assert.Equal(new[] { new VoxelCoordinate(0, 0, 0), new(0, 0, 1), new(1, 1, 1) }, grid.Coordinates);
        Assert.Equal(new[] { 0, 1, -1, -1, -1, -1, -1, 2 }, grid.ActiveIndex);
        for (int row = 0; row < grid.Coordinates.Length; row++)
        {
            Assert.Equal(row, grid.Neighbors27[row * 27 + 13]);
            for (int slot = 0; slot < 27; slot++)
            {
                int neighbor = grid.Neighbors27[row * 27 + slot];
                if (neighbor >= 0)
                {
                    Assert.Equal(row, grid.Neighbors27[neighbor * 27 + 26 - slot]);
                }
            }
        }
        Assert.Equal(-1, grid.Neighbors27[0]);
    }

    [Fact]
    public void EmptyAndFullGridsRemainValidAndZeroIsOutside()
    {
        var empty = SparseVoxelGrid.FromLogits(new float[8], 2);
        Assert.Empty(empty.Coordinates);
        Assert.Empty(empty.Neighbors27);
        Assert.All(empty.ActiveIndex, index => Assert.Equal(-1, index));
        var full = SparseVoxelGrid.FromLogits(Enumerable.Repeat(1f, 8).ToArray(), 2);
        Assert.Equal(8, full.Coordinates.Length);
        Assert.Equal(216, full.Neighbors27.Length);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(65, 1)]
    [InlineData(4, 3)]
    [InlineData(4, 0)]
    public void InvalidDimensionsHaveNamedStops(int resolution, int pool)
    {
        var exception = Assert.Throws<InvalidDataException>(() => SparseVoxelGrid.FromLogits(new float[64], resolution, pool));
        Assert.StartsWith("AUR-OCCUPANCY-001:", exception.Message);
    }

    [Fact]
    public void NonfiniteLogitsAreRejectedBeforeProducingCoordinates()
    {
        Assert.Throws<InvalidDataException>(() => SparseVoxelGrid.FromLogits(new[] { float.PositiveInfinity }, 1));
    }
}
