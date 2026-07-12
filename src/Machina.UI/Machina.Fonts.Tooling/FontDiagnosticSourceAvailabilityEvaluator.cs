namespace Machina.Fonts.Tooling;

public static class FontDiagnosticSourceAvailabilityEvaluator
{
    public static FontDiagnosticSourceAvailability Create(
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        bool placementReportAvailable,
        bool shapeDiffReportAvailable)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(errors);

        return new FontDiagnosticSourceAvailability(
            BrowserReferenceAvailable: false,
            DirectOutlineAvailable: true,
            MsdfAvailable: true,
            BrowserMaskAvailable: false,
            DirectMaskAvailable: true,
            MsdfMaskAvailable: true,
            PlacementReportAvailable: placementReportAvailable,
            ShapeDiffReportAvailable: shapeDiffReportAvailable,
            warnings.ToArray(),
            errors.ToArray());
    }
}
