using System.Text;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public static class FontDiagnosticManifestBuilder
{
    public static FontDiagnosticExportManifest CreateManifest(
        FontDiagnosticExportOptions options,
        string outputDirectory,
        FontDiagnosticSourceAvailability sourceAvailability,
        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> presetReports,
        IReadOnlyList<string> artifacts,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(sourceAvailability);
        ArgumentNullException.ThrowIfNull(presetReports);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(errors);

        List<string> manifestWarnings = warnings
            .Concat(sourceAvailability.Warnings)
            .Concat(presetReports.SelectMany(static report => report.Warnings))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToList();

        List<string> manifestErrors = errors
            .Concat(sourceAvailability.Errors)
            .Concat(presetReports.SelectMany(static report => report.Errors))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToList();

        return new FontDiagnosticExportManifest(
            Format: 1,
            Kind: "machina-font-diagnostic-export",
            Milestone: "M9d",
            OutputDirectory: outputDirectory,
            Presets: options.PresetNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TextBackend: new FontDiagnosticTextBackendPolicy(
                MachinaTextRenderStrategyCatalog.GetStableName(options.StaticTextRenderStrategy),
                MachinaTextRenderStrategyCatalog.GetStableName(MachinaTextRenderStrategyCatalog.ScalableExperimental)),
            Options: new FontDiagnosticExportManifestOptions(
                options.CleanOutputDirectory,
                options.AllowPartial,
                options.ScaleExperimentalFieldWithEmSize,
                options.GridOptions.GridStep,
                options.GridOptions.ShowGrid,
                options.GridOptions.ShowUnitLabels,
                options.GridOptions.ShowAxes,
                options.GridOptions.AxisStep,
                options.BoundsOptions.ShowBounds,
                options.BoundsOptions.ShowWireframes),
            Sources: sourceAvailability with
            {
                Warnings = manifestWarnings.ToArray(),
                Errors = manifestErrors.ToArray(),
            },
            PresetReports: presetReports,
            Artifacts: artifacts
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            Warnings: manifestWarnings.ToArray(),
            Errors: manifestErrors.ToArray(),
            Complete: manifestErrors.Count == 0 && presetReports.All(static report => report.Complete),
            GeneratedAtUtc: options.IncludeTimestamp
                ? DateTimeOffset.UtcNow.ToString("O")
                : null);
    }

    public static string BuildTextReport(FontDiagnosticExportManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        StringBuilder builder = new();
        builder.AppendLine("Machina Font Toolkit M9d export manifest");
        builder.AppendLine($"format: {manifest.Format}");
        builder.AppendLine($"kind: {manifest.Kind}");
        builder.AppendLine($"milestone: {manifest.Milestone}");
        builder.AppendLine($"outputDirectory: {manifest.OutputDirectory}");
        builder.AppendLine($"presets: {string.Join(", ", manifest.Presets)}");
        builder.AppendLine("textBackend:");
        builder.AppendLine($"  staticDefault: {manifest.TextBackend.StaticDefault}");
        builder.AppendLine($"  scalableExperimental: {manifest.TextBackend.ScalableExperimental}");
        builder.AppendLine($"complete: {manifest.Complete.ToString().ToLowerInvariant()}");
        builder.AppendLine($"generatedAtUtc: {manifest.GeneratedAtUtc ?? "<omitted>"}");
        builder.AppendLine("options:");
        builder.AppendLine($"  cleanOutputDirectory: {manifest.Options.CleanOutputDirectory.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  allowPartial: {manifest.Options.AllowPartial.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  scaleExperimentalFieldWithEmSize: {manifest.Options.ScaleExperimentalFieldWithEmSize.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  gridStep: {manifest.Options.GridStep}");
        builder.AppendLine($"  showGrid: {manifest.Options.ShowGrid.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showUnitLabels: {manifest.Options.ShowUnitLabels.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showAxes: {manifest.Options.ShowAxes.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  axisStep: {manifest.Options.AxisStep}");
        builder.AppendLine($"  showBounds: {manifest.Options.ShowBounds.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  showWireframes: {manifest.Options.ShowWireframes.ToString().ToLowerInvariant()}");
        builder.AppendLine("sources:");
        builder.AppendLine($"  browserReferenceAvailable: {manifest.Sources.BrowserReferenceAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directOutlineAvailable: {manifest.Sources.DirectOutlineAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  msdfAvailable: {manifest.Sources.MsdfAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  browserMaskAvailable: {manifest.Sources.BrowserMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directMaskAvailable: {manifest.Sources.DirectMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  msdfMaskAvailable: {manifest.Sources.MsdfMaskAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  placementReportAvailable: {manifest.Sources.PlacementReportAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  shapeDiffReportAvailable: {manifest.Sources.ShapeDiffReportAvailable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"warnings: {FormatJoinedValues(manifest.Warnings)}");
        builder.AppendLine($"errors: {FormatJoinedValues(manifest.Errors)}");
        builder.AppendLine("presetReports:");

        foreach (FontDiagnosticPresetAvailabilityReport presetReport in manifest.PresetReports)
        {
            builder.AppendLine($"  - {presetReport.PresetName}: complete={presetReport.Complete.ToString().ToLowerInvariant()} required={FormatJoinedValues(presetReport.RequiredSources)} available={FormatJoinedValues(presetReport.AvailableSources)} missing={FormatJoinedValues(presetReport.MissingRequiredSources)} degraded={FormatJoinedValues(presetReport.DegradedSources)} warnings={FormatJoinedValues(presetReport.Warnings)} errors={FormatJoinedValues(presetReport.Errors)}");
        }

        builder.AppendLine("artifacts:");
        foreach (string artifact in manifest.Artifacts)
        {
            builder.AppendLine($"  - {artifact}");
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> EnumerateGeneratedArtifacts(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        return Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputDirectory, path))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatJoinedValues(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "none"
            : string.Join(", ", values);
    }
}
