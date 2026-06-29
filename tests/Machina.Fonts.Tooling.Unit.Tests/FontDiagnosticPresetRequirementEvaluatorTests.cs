using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class FontDiagnosticPresetRequirementEvaluatorTests
{
    [Fact]
    public void Export_StrictPresetFailsWhenBrowserMissing()
    {
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);

        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> reports =
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(["browser-vs-direct"], availability, allowPartial: false);

        FontDiagnosticPresetAvailabilityReport report = Assert.Single(reports);
        Assert.False(report.Complete);
        Assert.Contains("browser-reference", report.MissingRequiredSources);
        Assert.Contains(report.Errors, error => error.Contains("requires sources that are unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Export_AllowPartialWritesWarningWhenBrowserMissing()
    {
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);

        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> reports =
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(["browser-vs-direct"], availability, allowPartial: true);

        FontDiagnosticPresetAvailabilityReport report = Assert.Single(reports);
        Assert.False(report.Complete);
        Assert.Contains("browser-reference", report.DegradedSources);
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, warning => warning.Contains("degraded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Export_DirectVsMsdfSucceedsWithoutBrowser()
    {
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);

        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> reports =
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(["direct-vs-msdf"], availability, allowPartial: false);

        FontDiagnosticPresetAvailabilityReport report = Assert.Single(reports);
        Assert.True(report.Complete);
        Assert.Empty(report.Errors);
        Assert.Equal(["direct-outline", "msdf"], report.RequiredSources);
    }
}
