using Aurelian.Graphics.Vulkan.ComputeMath;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class StreamingLinearPlanTests
{
    [Fact]
    public void WideFeedForwardProjectionUsesTheEntireBoundedDispatchBudget()
    {
        var plan = new StreamingLinearPlan(8192, 1536, 64);
        Assert.Equal(512, plan.DispatchesPerTile);
        Assert.Equal(2097408ul, plan.WeightBufferBytes);
        Assert.Equal(24, plan.Tiles.Count);
        Assert.Equal(new LinearWeightTile(1472, 64), plan.Tiles[^1]);
    }

    [Fact]
    public void ScheduleCoversEveryOutputExactlyOnceIncludingATail()
    {
        var plan = new StreamingLinearPlan(1536, 9216, 250);
        Assert.Equal(96, plan.DispatchesPerTile);
        Assert.Equal(1537000ul, plan.WeightBufferBytes);
        Assert.Equal(56659968ul, plan.ExpandedFullWeightBytes);
        Assert.Equal(37, plan.Tiles.Count);
        Assert.Equal(new LinearWeightTile(9000, 216), plan.Tiles[^1]);
        Assert.Equal(Enumerable.Range(0, 9216), plan.Tiles.SelectMany(tile => Enumerable.Range(tile.FirstOutput, tile.OutputCount)));
    }

    [Theory]
    [InlineData(0, 1536, 16)]
    [InlineData(8193, 1536, 16)]
    [InlineData(1536, 0, 16)]
    [InlineData(1536, 65537, 16)]
    [InlineData(1536, 1536, 0)]
    [InlineData(1536, 1536, 1537)]
    [InlineData(1536, 1536, 1024)]
    [InlineData(1, 8192, 4097)]
    public void InvalidShapesAndUnboundedTilesAreRejectedBeforeExecution(int input, int output, int tile)
    {
        var exception = Assert.Throws<ArgumentException>(() => new StreamingLinearPlan(input, output, tile));
        Assert.StartsWith("AUR-COMPUTE-MATH-001:", exception.Message);
    }
}
