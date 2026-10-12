using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class MeshClusterReducerTests
{
    [Fact]
    public void ReductionRetainsSourceMappingAndMeasuresDeviation()
    {
        var positions = new Vector3[] { new(.001f, .001f, 0), new(.002f, .001f, 0),
            new(.1f, 0, 0), new(0, .1f, 0), new(1, 1, 1) }.ToImmutableArray();
        var triangles = new[] { 0, 2, 3, 1, 2, 3, 0, 1, 2 }.ToImmutableArray();
        var reduced = MeshClusterReducer.Reduce(positions, triangles, 64);
        Assert.Equal(new[] { 0, 0, 1, 2, -1 }, reduced.SourceToReduced);
        Assert.Equal(new[] { 0, 1, 2 }, reduced.TriangleIndices);
        Assert.InRange(reduced.MaximumPositionDeviation, .00049f, .00051f);
        Assert.Equal(positions, new Vector3[] { new(.001f, .001f, 0), new(.002f, .001f, 0),
            new(.1f, 0, 0), new(0, .1f, 0), new(1, 1, 1) });
    }

    [Fact]
    public void InvalidMeshAndNonfinitePositionsHaveNamedStops()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, new Vector3(float.NaN, 0, 0) }.ToImmutableArray();
        var error = Assert.Throws<InvalidDataException>(() => MeshClusterReducer.Reduce(positions, new[] { 0, 1, 2 }.ToImmutableArray(), 64));
        Assert.StartsWith("AUR-MESH-LOD-001:", error.Message);
        Assert.Throws<InvalidDataException>(() => MeshClusterReducer.Reduce(positions, new[] { 0, 1, 3 }.ToImmutableArray(), 64));
    }
}
