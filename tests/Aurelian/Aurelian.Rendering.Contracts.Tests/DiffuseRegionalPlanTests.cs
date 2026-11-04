using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class DiffuseRegionalPlanTests
{
    [Fact]
    public void UnevenPartitionsCoverEveryProbeExactlyOnceAndSnapshotCommands()
    {
        var grid = new DiffuseProbeGrid(Vector3.Zero, Vector3.One, 5, 3, 7, 128);
        var partition = new DiffuseRegionPartition3D(2, 2, 3);
        var counts = partition.ProbeCounts(grid);
        Assert.Equal(105, counts.Sum());
        Assert.All(counts, count => Assert.True(count > 0));
        Assert.Equal(0, partition.RegionForProbe(grid, 0));
        Assert.Equal(11, partition.RegionForProbe(grid, 104));
        var commands = counts.Select(_ => new DiffuseRegionCommand3D(DiffuseRegionStrategy3D.Runtime, 128, 0)).ToArray();
        var plan = new DiffuseRegionalPlan3D(partition, commands);
        commands[0] = new(DiffuseRegionStrategy3D.Compiled, 0, 9);
        Assert.Equal(DiffuseRegionStrategy3D.Runtime, plan.Regions[0].Strategy);
        plan.Validate(grid);
        Assert.Throws<ArgumentException>(() => new DiffuseRegionPartition3D(6).Validate(grid));
        Assert.Throws<ArgumentException>(() => new DiffuseRegionPartition3D(5, 3, 7).Validate(grid));
    }

    [Theory]
    [InlineData(DiffuseRegionStrategy3D.Compiled, 1, 0)]
    [InlineData(DiffuseRegionStrategy3D.Runtime, 16, 0)]
    [InlineData(DiffuseRegionStrategy3D.Residual, 129, 0)]
    [InlineData(DiffuseRegionStrategy3D.Residual, 16, -1)]
    [InlineData((DiffuseRegionStrategy3D)100, 0, 0)]
    public void InvalidWorkCommandsAreRejected(DiffuseRegionStrategy3D strategy, int budget, int epoch)
    {
        var grid = new DiffuseProbeGrid(Vector3.Zero, Vector3.One, 4, 4, 4, 128);
        var plan = new DiffuseRegionalPlan3D(new(), Enumerable.Repeat(new DiffuseRegionCommand3D(strategy, budget, epoch), 4));
        Assert.Throws<ArgumentException>(() => plan.Validate(grid));
        Assert.Throws<ArgumentException>(() => new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RegionalPlan = plan }.Validate());
    }
}
