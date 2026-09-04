using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class Native2DSubmissionM1Tests
{
    [Fact]
    public void ValidateValues_AcceptsFiniteAxisAlignedPixelAndUvPayload()
    {
        NativeQuadSubmission submission = CreateSubmission(
            new Native2DRect(-10, 20, 30, 40),
            Native2DUvRect.Full,
            new Native2DTint(0, 0.5f, 1, 1));

        Native2DSubmissionValidator.ValidateValues(submission);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ValidateValues_RejectsNonFiniteCoordinates(float value)
    {
        NativeQuadSubmission submission = CreateSubmission(
            new Native2DRect(value, 0, 1, 1),
            Native2DUvRect.Full,
            Native2DTint.White);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Native2DSubmissionValidator.ValidateValues(submission));

        Assert.Contains("finite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateValues_RejectsNegativeDimensions()
    {
        NativeQuadSubmission submission = CreateSubmission(
            new Native2DRect(0, 0, -1, 1),
            Native2DUvRect.Full,
            Native2DTint.White);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Native2DSubmissionValidator.ValidateValues(submission));

        Assert.Contains("cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateValues_RejectsReversedUvBounds()
    {
        NativeQuadSubmission submission = CreateSubmission(
            new Native2DRect(0, 0, 1, 1),
            new Native2DUvRect(1, 0, 0, 1),
            Native2DTint.White);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Native2DSubmissionValidator.ValidateValues(submission));

        Assert.Contains("UV bounds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateValues_RejectsTintOutsideUnitRange()
    {
        NativeQuadSubmission submission = CreateSubmission(
            new Native2DRect(0, 0, 1, 1),
            Native2DUvRect.Full,
            new Native2DTint(1.01f, 1, 1, 1));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Native2DSubmissionValidator.ValidateValues(submission));

        Assert.Contains("[0, 1]", exception.Message, StringComparison.Ordinal);
    }

    private static NativeQuadSubmission CreateSubmission(
        Native2DRect destination,
        Native2DUvRect uv,
        Native2DTint tint)
        => new(destination, uv, new Native2DTextureHandle(1), tint);
}
