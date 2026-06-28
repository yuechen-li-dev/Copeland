using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DistanceFieldSamplingTests
{
    [Fact]
    public void DistanceFieldSampling_BilinearOrNearestPolicyIsDocumented()
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
    public void DistanceFieldSampling_MsdfMedianThreshold_IsStable()
    {
        float[] data = [0.9f, 0.2f, 0.6f];

        float sample = DistanceFieldSampling.SampleDistance(data, 1, 1, 3, DistanceFieldKind.Msdf, 0d, 0d);
        double coverage = DistanceFieldSampling.ComputeCoverage(sample, 4d, 0.6d, 1d, 1d);

        Assert.Equal(0.6f, sample, 5);
        Assert.Equal(0.5d, coverage, 6);
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
    public void DistanceFieldSampling_SmoothAlpha_UsesConfiguredSmoothing()
    {
        double narrow = DistanceFieldSampling.ComputeCoverage(0.45f, 4d, 0.5d, 1d, 0.5d);
        double defaultWidth = DistanceFieldSampling.ComputeCoverage(0.45f, 4d, 0.5d, 1d, 1d);
        double wide = DistanceFieldSampling.ComputeCoverage(0.45f, 4d, 0.5d, 1d, 1.5d);

        Assert.True(narrow < defaultWidth);
        Assert.True(defaultWidth < wide);
    }

    [Fact]
    public void DistanceFieldSampling_SampleCoordinatesUsePixelCenters()
    {
        DistanceFieldPageReference page = RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Sdf,
            2,
            1,
            static (x, _) => [x == 0 ? 0f : 1f]);
        GlyphAtlasEntry entry = RenderingTestHelpers.CreateEntry(0, 0, 2, 1, page.Width, page.Height);

        RgbaImage image = CpuDistanceFieldGlyphRenderer.RenderGlyph(
            page,
            entry,
            new DistanceFieldRenderOptions(
                2,
                1,
                Rgba32.White,
                Rgba32.Black,
                PxRange: 1d,
                Threshold: 0.5d,
                SmoothingMultiplier: 1d));

        Rgba32 left = image.GetPixel(0, 0);
        Rgba32 right = image.GetPixel(1, 0);

        Assert.InRange(left.R, 1, 254);
        Assert.InRange(right.R, 1, 254);
        Assert.True(left.R < right.R);
    }
}
