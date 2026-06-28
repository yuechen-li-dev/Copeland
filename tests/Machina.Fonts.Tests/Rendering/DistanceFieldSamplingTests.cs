using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DistanceFieldSamplingTests
{
    [Fact]
    public void SampleSdf_ReturnsExpectedValue()
    {
        float[] data =
        [
            0.1f, 0.2f,
            0.3f, 0.4f,
        ];

        float sample = DistanceFieldSampling.SampleDistance(data, 2, 2, 1, DistanceFieldKind.Sdf, 0.5d, 0.5d);

        Assert.Equal(0.25f, sample, 5);
    }

    [Fact]
    public void SampleMsdf_UsesMedianRgb()
    {
        float[] data = [0.9f, 0.2f, 0.6f];

        float sample = DistanceFieldSampling.SampleDistance(data, 1, 1, 3, DistanceFieldKind.Msdf, 0d, 0d);

        Assert.Equal(0.6f, sample, 5);
    }

    [Fact]
    public void SampleMsdf_IsDeterministic()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            2,
            2,
            static (x, y) =>
            {
                float value = 0.2f + (x * 0.1f) + (y * 0.2f);
                return [value + 0.3f, value, value + 0.1f];
            });

        float first = DistanceFieldSampling.SampleDistance(page, 0.35d, 0.65d);
        float second = DistanceFieldSampling.SampleDistance(page, 0.35d, 0.65d);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Sample_RejectsInvalidChannelCount()
    {
        float[] data = new float[8];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DistanceFieldSampling.SampleDistance(data, 2, 2, 2, DistanceFieldKind.Msdf, 0.5d, 0.5d));
    }

    [Fact]
    public void SmoothAlpha_ThresholdBehaviorIsStable()
    {
        double below = DistanceFieldSampling.ComputeCoverage(0.3f, 4d, 0.5d, 1d);
        double atThreshold = DistanceFieldSampling.ComputeCoverage(0.5f, 4d, 0.5d, 1d);
        double above = DistanceFieldSampling.ComputeCoverage(0.7f, 4d, 0.5d, 1d);

        Assert.True(below < 0.5d);
        Assert.Equal(0.5d, atThreshold, 12);
        Assert.True(above > 0.5d);
        Assert.True(below < above);
    }
}
