using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class DiffuseProbeGridTests
{
    [Fact]
    public void CornersMatchTheGpuDecoderOrderingAndStayInTheGridAtBoundaryReceivers()
    {
        var grid = new DiffuseProbeGrid(Vector3.Zero, new(3), 4, 4, 4);
        Assert.Equal([21, 22, 25, 26, 37, 38, 41, 42], grid.Corners(new(1.3f, 1.3f, 1.3f), Vector3.UnitY));
        foreach (var point in new[] { new Vector3(-.1f), new Vector3(3.1f), Vector3.Zero, new Vector3(3) })
        {
            Assert.All(grid.Corners(point, Vector3.UnitY), index => Assert.InRange(index, 0, 63));
        }
        Assert.Equal(Vector3.Zero, grid.Position(0));
        Assert.Equal(new Vector3(3), grid.Position(63));
    }

    [Fact]
    public void WideShallowGridsReuseTheSame512ProbeStorageLimit()
    {
        var wide = new DiffuseProbeGrid(Vector3.Zero, Vector3.One, 12, 3, 12, 128);
        wide.Validate();
        Assert.Equal(432, wide.Count);
        Assert.Equal(Vector3.One, wide.Position(431));
        Assert.All(wide.Corners(new(.999f), Vector3.UnitY), index => Assert.InRange(index, 0, 431));
        new DiffuseProbeGrid(Vector3.Zero, Vector3.One, 16, 2, 16, 128).Validate();
        Assert.Throws<ArgumentException>(() => new DiffuseProbeGrid(Vector3.Zero, Vector3.One, 16, 3, 16).Validate());
    }

    [Theory]
    [InlineData(1, 64, 8)]
    [InlineData(17, 64, 8)]
    [InlineData(4, 15, 8)]
    [InlineData(4, 129, 8)]
    [InlineData(4, 64, 1)]
    [InlineData(4, 64, 33)]
    public void InvalidStorageAndRayBudgetsAreRejectedBeforeGpuAllocation(int size, int rays, int surfaceResolution)
    {
        var grid = new DiffuseProbeGrid(Vector3.Zero, Vector3.One, size, 4, 4, rays, surfaceResolution);
        Assert.Throws<ArgumentException>(grid.Validate);
    }

    [Fact]
    public void DegenerateGeometryAndNonphysicalMaterialsAreRejected()
    {
        var valid = new DiffuseProbeTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, new(.5f), Vector3.Zero);
        valid.Validate();
        Assert.Throws<ArgumentException>((valid with { C = valid.B }).Validate);
        Assert.Throws<ArgumentException>((valid with { Albedo = new(1.01f) }).Validate);
        Assert.Throws<ArgumentException>((valid with { Emission = new(-1) }).Validate);
        Assert.Throws<ArgumentException>((valid with { A = new(float.NaN) }).Validate);
        Assert.Throws<ArgumentException>(new DiffuseProbeLight(Vector3.Zero, Vector3.One, float.PositiveInfinity).Validate);
    }
}
