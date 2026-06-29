using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public enum FontDiagnosticSourceKind
{
    BrowserReference,
    DirectOutline,
    Msdf,
    BrowserMask,
    DirectMask,
    MsdfMask,
    PlacementReport,
    ShapeDiffReport,
}

public sealed record FontDiagnosticSourceAvailability(
    bool BrowserReferenceAvailable,
    bool DirectOutlineAvailable,
    bool MsdfAvailable,
    bool BrowserMaskAvailable,
    bool DirectMaskAvailable,
    bool MsdfMaskAvailable,
    bool PlacementReportAvailable,
    bool ShapeDiffReportAvailable,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsAvailable(FontDiagnosticSourceKind sourceKind)
    {
        return sourceKind switch
        {
            FontDiagnosticSourceKind.BrowserReference => BrowserReferenceAvailable,
            FontDiagnosticSourceKind.DirectOutline => DirectOutlineAvailable,
            FontDiagnosticSourceKind.Msdf => MsdfAvailable,
            FontDiagnosticSourceKind.BrowserMask => BrowserMaskAvailable,
            FontDiagnosticSourceKind.DirectMask => DirectMaskAvailable,
            FontDiagnosticSourceKind.MsdfMask => MsdfMaskAvailable,
            FontDiagnosticSourceKind.PlacementReport => PlacementReportAvailable,
            FontDiagnosticSourceKind.ShapeDiffReport => ShapeDiffReportAvailable,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
        };
    }

    public IReadOnlyList<string> GetAvailableSourceNames()
    {
        return FontDiagnosticSourceCatalog.GetAll()
            .Where(IsAvailable)
            .Select(FontDiagnosticSourceCatalog.GetName)
            .ToArray();
    }
}

public sealed record FontDiagnosticPresetRequirements(
    IReadOnlyList<FontDiagnosticSourceKind> RequiredSources);

public sealed record FontDiagnosticPresetAvailabilityReport(
    string PresetName,
    IReadOnlyList<string> RequiredSources,
    IReadOnlyList<string> AvailableSources,
    IReadOnlyList<string> MissingRequiredSources,
    IReadOnlyList<string> DegradedSources,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Complete);

public sealed record FontDiagnosticExportManifestOptions(
    bool CleanOutputDirectory,
    bool AllowPartial,
    bool ScaleExperimentalFieldWithEmSize,
    int GridStep,
    bool ShowGrid,
    bool ShowUnitLabels,
    bool ShowAxes,
    int AxisStep,
    bool ShowBounds,
    bool ShowWireframes);

public sealed record FontDiagnosticTextBackendPolicy(
    string StaticDefault,
    string ScalableExperimental);

public sealed record FontDiagnosticExportManifest(
    int Format,
    string Kind,
    string Milestone,
    string OutputDirectory,
    IReadOnlyList<string> Presets,
    FontDiagnosticTextBackendPolicy TextBackend,
    FontDiagnosticExportManifestOptions Options,
    FontDiagnosticSourceAvailability Sources,
    IReadOnlyList<FontDiagnosticPresetAvailabilityReport> PresetReports,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Complete,
    string? GeneratedAtUtc);

internal static class FontDiagnosticSourceCatalog
{
    public static IReadOnlyList<FontDiagnosticSourceKind> GetAll()
    {
        return
        [
            FontDiagnosticSourceKind.BrowserReference,
            FontDiagnosticSourceKind.DirectOutline,
            FontDiagnosticSourceKind.Msdf,
            FontDiagnosticSourceKind.BrowserMask,
            FontDiagnosticSourceKind.DirectMask,
            FontDiagnosticSourceKind.MsdfMask,
            FontDiagnosticSourceKind.PlacementReport,
            FontDiagnosticSourceKind.ShapeDiffReport,
        ];
    }

    public static string GetName(FontDiagnosticSourceKind sourceKind)
    {
        return sourceKind switch
        {
            FontDiagnosticSourceKind.BrowserReference => "browser-reference",
            FontDiagnosticSourceKind.DirectOutline => "direct-outline",
            FontDiagnosticSourceKind.Msdf => "msdf",
            FontDiagnosticSourceKind.BrowserMask => "browser-mask",
            FontDiagnosticSourceKind.DirectMask => "direct-mask",
            FontDiagnosticSourceKind.MsdfMask => "msdf-mask",
            FontDiagnosticSourceKind.PlacementReport => "placement-report",
            FontDiagnosticSourceKind.ShapeDiffReport => "shape-diff-report",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
        };
    }
}
