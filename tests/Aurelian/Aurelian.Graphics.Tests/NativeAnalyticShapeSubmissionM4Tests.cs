using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class NativeAnalyticShapeSubmissionM4Tests
{
    [Fact]
    public void ValidClosedShapesUseTexturelessStraightAlphaPipeline()
    {
        Native2DSubmissionValidator.ValidateValues(Create(NativeAnalyticShapeKind.RoundedRect, 8));
        Native2DSubmissionValidator.ValidateValues(Create(NativeAnalyticShapeKind.Circle, 16, 32, 32));
        Native2DSubmissionValidator.ValidateValues(Create(NativeAnalyticShapeKind.Pill, 16, 240, 32));

        Assert.True(Native2DPipelineOptions.AnalyticShape2D.StraightAlphaBlend);
        Assert.False(Native2DPipelineOptions.AnalyticShape2D.LinearFiltering);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(17)]
    public void InvalidRadiusIsRejected(float radius)
    {
        Assert.ThrowsAny<ArgumentException>(() => Native2DSubmissionValidator.ValidateValues(Create(
            NativeAnalyticShapeKind.RoundedRect,
            radius,
            32,
            32)));
    }

    [Fact]
    public void ZeroSizeAndNonSquareCircleAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Native2DSubmissionValidator.ValidateValues(Create(
            NativeAnalyticShapeKind.RoundedRect,
            0,
            0,
            32)));
        Assert.Throws<ArgumentException>(() => Native2DSubmissionValidator.ValidateValues(Create(
            NativeAnalyticShapeKind.Circle,
            8,
            32,
            16)));
    }

    [Fact]
    public void SoftShockwaveIsTexturelessStraightAlphaAndRejectsNonFiniteParameters()
    {
        var valid = new NativeSoftShockwaveSubmission(
            new Native2DRect(4, 4, 64, 64),
            Native2DUvRect.Full,
            new Native2DTint(1, 0.8f, 0.2f, 1),
            Age: 0.1f,
            Lifetime: 0.4f,
            Radius: 0.45f,
            Thickness: 0.08f,
            Intensity: 1,
            Seed: 17);
        Native2DSubmissionValidator.ValidateValues(valid);
        Assert.True(Native2DPipelineOptions.SoftShockwave.StraightAlphaBlend);
        Assert.False(Native2DPipelineOptions.SoftShockwave.LinearFiltering);
        Assert.Throws<ArgumentException>(() => Native2DSubmissionValidator.ValidateValues(valid with { Age = float.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Native2DSubmissionValidator.ValidateValues(valid with { Lifetime = 0 }));
    }

    private static NativeAnalyticShapeSubmission Create(
        NativeAnalyticShapeKind kind,
        float radius,
        float width = 32,
        float height = 32)
    {
        return new NativeAnalyticShapeSubmission(
            new Native2DRect(0, 0, width, height),
            new Native2DSize(width, height),
            Native2DUvRect.Full,
            kind,
            Native2DTint.White,
            radius,
            Native2DTint.White,
            0);
    }
}
