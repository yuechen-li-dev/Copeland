using System.Numerics;
using Aurelian.Assets.Tensors;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class FlexibleDualGridMeshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EdgeNeighborhoodsUseLearnedDiagonalWeightsAndGridScale(int axis)
    {
        VoxelCoordinate[][] coordinates =
        [
            [new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0)],
            [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)],
            [new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0)],
        ];
        var topology = SparseVoxelTopology.Create(2, coordinates[axis]);
        float[] features = new float[28];
        features[3 + axis] = 1;
        features[6] = 3;
        features[20] = 3;
        var mesh = FlexibleDualGridMesh.Decode(topology, features);
        Assert.Equal(1, mesh.Quads);
        Assert.Equal(0, mesh.MissingNeighborQuads);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, mesh.TriangleIndices);
        Assert.Equal(new Vector3(-.25f), mesh.Positions[0]);
        features[6] = 0;
        features[20] = 0;
        features[13] = 3;
        features[27] = 3;
        Assert.Equal(new[] { 0, 1, 3, 3, 1, 2 }, FlexibleDualGridMesh.Decode(topology, features).TriangleIndices);
    }

    [Fact]
    public void MissingNeighborsAreDiagnosedWithoutInventingTriangles()
    {
        var topology = SparseVoxelTopology.Create(2, [new(0, 0, 0)]);
        var mesh = FlexibleDualGridMesh.Decode(topology, [0, 0, 0, 1, 1, 1, 0]);
        Assert.Empty(mesh.TriangleIndices);
        Assert.Equal(3, mesh.MissingNeighborQuads);
        Assert.Throws<InvalidDataException>(() => FlexibleDualGridMesh.Decode(topology, [0]));
        Assert.Throws<InvalidDataException>(() => FlexibleDualGridMesh.Decode(topology, [0, 0, 0, 0, 0, 0, float.NaN]));
    }
}
