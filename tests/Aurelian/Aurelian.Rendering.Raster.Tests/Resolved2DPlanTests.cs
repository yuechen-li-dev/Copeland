using Aurelian.Rendering.Contracts.Resolved2D;
using Xunit;

namespace Aurelian.Rendering.Raster.Tests;

public sealed class Resolved2DPlanTests
{
    [Fact]
    public void Plan_CopiesOperationsIntoAnImmutableOrderedList()
    {
        var source = new List<Resolved2DOperation>
        {
            new FillRectangleOperation("first", new Resolved2DRectangle(0, 0, 1, 1), Resolved2DRgbaColor.White),
            new FillRectangleOperation("second", new Resolved2DRectangle(1, 0, 1, 1), Resolved2DRgbaColor.Black)
        };

        var plan = new Resolved2DPlan(new Resolved2DViewport(2, 1), source);
        source.Clear();

        Assert.Collection(
            plan.Operations,
            operation => Assert.Equal("first", operation.OperationId),
            operation => Assert.Equal("second", operation.OperationId));
        Assert.False(plan.Operations is List<Resolved2DOperation>);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Viewport_RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resolved2DViewport(width, height));
    }

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, double.NegativeInfinity, 1)]
    public void Rectangle_RejectsNonFiniteGeometry(double x, double y, double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resolved2DRectangle(x, y, width, height));
    }

    [Fact]
    public void Plan_RejectsClipStackUnderflow()
    {
        Assert.Throws<InvalidOperationException>(() => new Resolved2DPlan(
            new Resolved2DViewport(1, 1),
            [new PopClipOperation("pop")]));
    }

    [Fact]
    public void Plan_RejectsUnbalancedClipStack()
    {
        Assert.Throws<InvalidOperationException>(() => new Resolved2DPlan(
            new Resolved2DViewport(1, 1),
            [new PushRectangularClipOperation("push", new Resolved2DRectangle(0, 0, 1, 1))]));
    }

    [Fact]
    public void PositionedText_AllowsEmptyTextAsADeterministicNoOp()
    {
        var operation = new PositionedTextOperation(
            "empty",
            new Resolved2DRectangle(0, 0, 1, 1),
            string.Empty,
            Resolved2DRgbaColor.White);

        Assert.Equal(string.Empty, operation.Text);
    }
}
