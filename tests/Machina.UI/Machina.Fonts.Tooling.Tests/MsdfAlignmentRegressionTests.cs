using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class MsdfAlignmentRegressionTests : IClassFixture<MsdfAlignmentExportFixture>
{
    private readonly MsdfAlignmentExportFixture fixture;

    public MsdfAlignmentRegressionTests(MsdfAlignmentExportFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void MsdfAlignment_HelloMachina_ImprovesAgainstDirectOutline()
    {
        MsdfAlignmentExportPair exports = fixture.ExportPair;

        FontShapeDiff before = FindFixture(exports.Before.ShapeDiffReport, 64, "Hello Machina").DirectVsMsdf;
        FontShapeDiff after = FindFixture(exports.After.ShapeDiffReport, 64, "Hello Machina").DirectVsMsdf;

        Assert.True(after.IntersectionOverUnion > before.IntersectionOverUnion + 0.05d);
        Assert.True(after.P95EdgeDistance < before.P95EdgeDistance);
    }

    [Fact]
    public void MsdfAlignment_Settings_ImprovesAgainstDirectOutline()
    {
        MsdfAlignmentExportPair exports = fixture.ExportPair;

        double beforeAverageIou = AverageIou(exports.Before.ShapeDiffReport, "Settings");
        double afterAverageIou = AverageIou(exports.After.ShapeDiffReport, "Settings");
        double beforeAverageP95 = AverageP95(exports.Before.ShapeDiffReport, "Settings");
        double afterAverageP95 = AverageP95(exports.After.ShapeDiffReport, "Settings");

        Assert.True(afterAverageIou >= beforeAverageIou - 0.01d);
        Assert.True(afterAverageP95 < beforeAverageP95);
    }

    [Fact]
    public void MsdfAlignment_DoesNotRegressSmallUiSizes()
    {
        MsdfAlignmentExportPair exports = fixture.ExportPair;

        foreach (int size in new[] { 16, 24 })
        {
            foreach (string text in new[] { "Hello Machina", "Settings", "Machina UI" })
            {
                FontShapeDiff before = FindFixture(exports.Before.ShapeDiffReport, size, text).DirectVsMsdf;
                FontShapeDiff after = FindFixture(exports.After.ShapeDiffReport, size, text).DirectVsMsdf;

                Assert.True(
                    after.IntersectionOverUnion >= before.IntersectionOverUnion - 0.03d,
                    $"Expected no material regression for '{text}' at {size}px. Before={before.IntersectionOverUnion:0.000}, After={after.IntersectionOverUnion:0.000}.");
            }
        }
    }

    [Fact]
    public void MsdfAlignment_UsesSharedFixtureArtifacts()
    {
        MsdfAlignmentExportPair exports = fixture.ExportPair;

        Assert.True(File.Exists(exports.Before.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(exports.After.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(exports.Before.ManifestJsonPath));
        Assert.True(File.Exists(exports.After.ManifestJsonPath));
        Assert.True(File.Exists(Path.Combine(exports.After.OutputDirectory, "64", "m9d-direct-vs-msdf-hello-machina.png")));
    }

    private static FontShapeDiffFixtureReport FindFixture(FontShapeDiffReport report, int sizePx, string text)
    {
        FontShapeDiffSizeReport size = Assert.Single(report.Sizes, item => item.SizePx == sizePx);
        return Assert.Single(size.Fixtures, item => string.Equals(item.Text, text, StringComparison.Ordinal));
    }

    private static double AverageIou(FontShapeDiffReport report, string text)
    {
        return report.Sizes
            .SelectMany(static size => size.Fixtures)
            .Where(fixture => string.Equals(fixture.Text, text, StringComparison.Ordinal))
            .Average(static fixture => fixture.DirectVsMsdf.IntersectionOverUnion);
    }

    private static double AverageP95(FontShapeDiffReport report, string text)
    {
        return report.Sizes
            .SelectMany(static size => size.Fixtures)
            .Where(fixture => string.Equals(fixture.Text, text, StringComparison.Ordinal))
            .Average(static fixture => fixture.DirectVsMsdf.P95EdgeDistance);
    }
}
