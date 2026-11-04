using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class EnvironmentRadiance3DTests
{
    [Fact]
    public void SourceIdentityAndConstantClassificationOwnAnImmutableSnapshot()
    {
        float[] pixels = [.3f, .5f, .8f, 1, .3f, .5f, .8f, 0];
        var source = new EnvironmentRadiance3D(2, 1, pixels);
        Assert.True(source.IsConstant);
        Assert.Equal(source.ContentKey, new EnvironmentRadiance3D(2, 1, pixels).ContentKey);
        pixels[0] = 3;
        Assert.Equal(.3f, source.Rgba[0]);
        var edited = new EnvironmentRadiance3D(2, 1, pixels);
        Assert.False(edited.IsConstant);
        Assert.NotEqual(source.ContentKey, edited.ContentKey);
    }

    [Fact]
    public void ImportanceDistributionUsesSolidAngleAndRetainsBoundedMonotonicMass()
    {
        var constant = new EnvironmentRadiance3D(16, 8, Enumerable.Repeat(1f, 16 * 8 * 4));
        Assert.Equal(16, constant.ImportanceWidth);
        Assert.Equal(8, constant.ImportanceHeight);
        Assert.Equal((float)((1 - Math.Cos(Math.PI / 8)) / 2), constant.ImportanceCdf[15], 6);
        Assert.Equal(1, constant.ImportanceCdf[^1]);
        var black = new EnvironmentRadiance3D(65, 33, new float[65 * 33 * 4]);
        Assert.Equal(2048, black.ImportanceCdf.Length);
        Assert.All(black.ImportanceCdf, value => Assert.True(float.IsFinite(value)));
        Assert.True(black.ImportanceCdf.Zip(black.ImportanceCdf.Skip(1)).All(pair => pair.First <= pair.Second));
        Assert.Equal(1, black.ImportanceCdf[^1]);
        float[] bright = new float[16 * 8 * 4];
        bright[(3 * 16 + 7) * 4] = 10;
        var isolated = new EnvironmentRadiance3D(16, 8, bright);
        Assert.Equal(0, isolated.ImportanceCdf[3 * 16 + 6]);
        Assert.Equal(1, isolated.ImportanceCdf[3 * 16 + 7]);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(60001f)]
    public void InvalidHdrSourcesAreRejectedBeforeGpuAdmission(float value)
    {
        Assert.Throws<ArgumentException>(() => new EnvironmentRadiance3D(1, 1, [value, 0, 0, 1]));
    }
}
