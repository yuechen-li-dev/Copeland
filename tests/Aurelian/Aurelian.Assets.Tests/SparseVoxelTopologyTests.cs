using Aurelian.Assets.Tensors;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class SparseVoxelTopologyTests
{
    [Fact]
    public void RowOrderAndNeighborReciprocitySurviveIrregularCoordinates()
    {
        VoxelCoordinate[] coordinates = [new(31, 4, 9), new(0, 0, 0), new(30, 4, 9)];
        var topology = SparseVoxelTopology.Create(512, coordinates);
        Assert.Equal(coordinates, topology.Coordinates);
        Assert.Equal(2, topology.Neighbors27[4]);
        Assert.Equal(0, topology.Neighbors27[2 * 27 + 22]);
        Assert.Equal(1, topology.Neighbors27[27 + 13]);
        Assert.Equal(-1, topology.Neighbors27[27]);
    }

    [Fact]
    public void SubdivisionUsesSparseXLeastSignificantBitOrderAndRetainsParentMappings()
    {
        var topology = SparseVoxelTopology.Create(32, [new(4, 8, 12), new(1, 2, 3)]);
        float[] logits = [1, 1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, -1, -1, -1, -1];
        var subdivision = topology.Subdivide(logits);
        Assert.Equal(new[] { new VoxelCoordinate(8, 16, 24), new(9, 16, 24), new(8, 16, 25), new(9, 17, 25), new(3, 4, 6) }, subdivision.Topology.Coordinates);
        Assert.Equal(new[] { 0, 0, 0, 0, 1 }, subdivision.ParentRows);
        Assert.Equal(new[] { 0, 1, 4, 7, 1 }, subdivision.ChildSlots);
        Assert.Equal(64, subdivision.Topology.Resolution);
        Assert.Equal(1, subdivision.Topology.Neighbors27[22]);
    }

    [Fact]
    public void DuplicateOutOfRangeAndNonfiniteInputsHaveNamedStops()
    {
        AssertStop(() => SparseVoxelTopology.Create(32, [new(0, 0, 0), new(0, 0, 0)]));
        AssertStop(() => SparseVoxelTopology.Create(32, [new(32, 0, 0)]));
        AssertStop(() => SparseVoxelTopology.Create(0, []));
        var topology = SparseVoxelTopology.Create(32, [new(0, 0, 0)]);
        AssertStop(() => topology.Subdivide([1, 2]));
        AssertStop(() => topology.Subdivide([1, 1, 1, float.NaN, 1, 1, 1, 1]));
    }

    private static void AssertStop(Action action)
    {
        var exception = Assert.Throws<InvalidDataException>(action);
        Assert.StartsWith("AUR-SPARSE-TOPOLOGY-001:", exception.Message);
    }
}
