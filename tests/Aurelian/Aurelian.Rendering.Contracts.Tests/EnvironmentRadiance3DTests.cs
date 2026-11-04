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
