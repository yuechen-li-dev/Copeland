using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class FontDiagnosticSourceAvailabilityEvaluatorTests
{
    [Fact]
    public void SourceAvailability_CanEvaluateWithoutRendering()
    {
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);

        Assert.False(availability.BrowserReferenceAvailable);
        Assert.False(availability.BrowserMaskAvailable);
        Assert.True(availability.DirectOutlineAvailable);
        Assert.True(availability.MsdfAvailable);
        Assert.True(availability.DirectMaskAvailable);
        Assert.True(availability.MsdfMaskAvailable);
        Assert.False(availability.PlacementReportAvailable);
        Assert.False(availability.ShapeDiffReportAvailable);
    }

    [Fact]
    public void SourceAvailability_CanMarkGeneratedReportsAvailable()
    {
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: ["warning"],
            errors: ["error"],
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);

        Assert.True(availability.PlacementReportAvailable);
        Assert.True(availability.ShapeDiffReportAvailable);
        Assert.Equal(["warning"], availability.Warnings);
        Assert.Equal(["error"], availability.Errors);
    }
}
