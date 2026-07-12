using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class FrameResolverTests
{
    [Fact]
    public void ResolveAbsoluteFrame_ResolvesExpectedRect()
    {
        var parent = new Rect(10, 20, 300, 200);
        var frame = new AbsoluteFrame(5, 6, 100, 50);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 15, 26, 100, 50);
    }

    [Fact]
    public void ResolveAnchorFrame_LeftWidth_TopHeight_ResolvesExpectedRect()
    {
        var parent = new Rect(10, 20, 300, 200);
        var frame = new AnchorFrame(Left: 5, Width: 100, Top: 6, Height: 50);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 15, 26, 100, 50);
    }

    [Fact]
    public void ResolveAnchorFrame_RightWidth_BottomHeight_ResolvesExpectedRect()
    {
        var parent = new Rect(10, 20, 300, 200);
        var frame = new AnchorFrame(Right: 10, Width: 100, Bottom: 20, Height: 50);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 200, 150, 100, 50);
    }

    [Fact]
    public void ResolveAnchorFrame_LeftRight_TopBottom_ResolvesExpectedRect()
    {
        var parent = new Rect(10, 20, 300, 200);
        var frame = new AnchorFrame(Left: 10, Right: 20, Top: 5, Bottom: 15);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 20, 25, 270, 180);
    }

    [Fact]
    public void ResolveAnchorFrame_UsesUiLengthAgainstAxisSize()
    {
        var parent = new Rect(0, 0, 400, 200);
        var frame = new AnchorFrame(Left: UiLength.Ui(0.25), Width: 100, Top: UiLength.Ui(0.5), Height: 20);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 100, 100, 100, 20);
    }

    [Fact]
    public void ResolveAnchorFrame_AllowsNegativePositionalAnchors()
    {
        var parent = new Rect(10, 20, 300, 200);
        var frame = new AnchorFrame(Left: -10, Width: 100, Top: -5, Height: 50);

        var actual = FrameResolver.ResolveFrame(parent, frame);

        AssertRect(actual, 0, 15, 100, 50);
    }

    [Fact]
    public void ResolveFrame_RejectsExplicitNegativeSize()
    {
        var absoluteError = AssertLayoutError("NegativeFrameSize", () =>
            FrameResolver.ResolveFrame(new Rect(0, 0, 100, 100), new AbsoluteFrame(0, 0, -1, 10)));

        Assert.Equal("NegativeFrameSize", absoluteError.Code);

        var anchorError = AssertLayoutError("NegativeFrameSize", () =>
            FrameResolver.ResolveFrame(new Rect(0, 0, 100, 100), new AnchorFrame(Left: 0, Width: -1, Top: 0, Height: 10)));

        Assert.Equal("NegativeFrameSize", anchorError.Code);
    }

    [Fact]
    public void ResolveFrame_RejectsDerivedNegativeSize()
    {
        var error = AssertLayoutError("NegativeResolvedSize", () =>
            FrameResolver.ResolveFrame(new Rect(0, 0, 100, 50), new AnchorFrame(Left: 70, Right: 50, Top: 0, Height: 10)));

        Assert.Equal("NegativeResolvedSize", error.Code);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ResolveFrame_RejectsInvalidHorizontalConstraintCount(bool left, bool right, bool width)
    {
        var frame = new AnchorFrame(
            Left: left ? 1 : null,
            Right: right ? 2 : null,
            Width: width ? 3 : null,
            Top: 0,
            Height: 1);

        var error = AssertLayoutError("InvalidAnchorHorizontal", () => FrameResolver.ResolveFrame(new Rect(0, 0, 100, 100), frame));
        Assert.Equal("InvalidAnchorHorizontal", error.Code);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ResolveFrame_RejectsInvalidVerticalConstraintCount(bool top, bool bottom, bool height)
    {
        var frame = new AnchorFrame(
            Left: 0,
            Width: 1,
            Top: top ? 1 : null,
            Bottom: bottom ? 2 : null,
            Height: height ? 3 : null);

        var error = AssertLayoutError("InvalidAnchorVertical", () => FrameResolver.ResolveFrame(new Rect(0, 0, 100, 100), frame));
        Assert.Equal("InvalidAnchorVertical", error.Code);
    }

    [Fact]
    public void ResolveFrame_RejectsRootFrameWithoutRootContext()
    {
        var error = AssertLayoutError("RootFrameWithoutRoot", () =>
            FrameResolver.ResolveFrame(new Rect(0, 0, 100, 100), new RootFrame()));

        Assert.Equal("RootFrameWithoutRoot", error.Code);
    }

    [Fact]
    public void ResolveFrame_RejectsInvalidParentRect()
    {
        var errorNegativeWidth = AssertLayoutError("InvalidParentRect", () =>
            FrameResolver.ResolveFrame(new Rect(0, 0, -1, 100), new AbsoluteFrame(0, 0, 1, 1)));
        Assert.Equal("InvalidParentRect", errorNegativeWidth.Code);

        var errorNaN = AssertLayoutError("InvalidParentRect", () =>
            FrameResolver.ResolveFrame(new Rect(double.NaN, 0, 100, 100), new AbsoluteFrame(0, 0, 1, 1)));
        Assert.Equal("InvalidParentRect", errorNaN.Code);
    }

    private static void AssertRect(Rect actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    private static LayoutError AssertLayoutError(string expectedCode, Action action)
    {
        var error = Assert.Throws<LayoutError>(action);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }
}
