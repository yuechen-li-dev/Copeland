namespace Machina.Fonts.Tooling;

public static class FontDiagnosticPresetRequirementEvaluator
{
    public static IReadOnlyList<FontDiagnosticPresetAvailabilityReport> EvaluatePresetAvailability(
        IReadOnlyList<string> presetNames,
        FontDiagnosticSourceAvailability sourceAvailability,
        bool allowPartial)
    {
        ArgumentNullException.ThrowIfNull(presetNames);
        ArgumentNullException.ThrowIfNull(sourceAvailability);

        List<FontDiagnosticPresetAvailabilityReport> reports = [];

        foreach (string presetName in presetNames
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            DiagnosticPresetDefinition preset = LayerPresets.GetPreset(presetName);
            List<string> requiredSources = preset.Requirements.RequiredSources
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();
            List<string> availableSources = preset.Requirements.RequiredSources
                .Where(sourceAvailability.IsAvailable)
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();
            List<string> missingRequiredSources = preset.Requirements.RequiredSources
                .Where(sourceKind => !sourceAvailability.IsAvailable(sourceKind))
                .Select(FontDiagnosticSourceCatalog.GetName)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList();

            List<string> warnings = [];
            List<string> errors = [];
            List<string> degradedSources = [];

            if (missingRequiredSources.Count > 0)
            {
                if (allowPartial)
                {
                    degradedSources.AddRange(missingRequiredSources);
                    warnings.Add($"Preset '{preset.Name}' degraded because required sources are missing: {string.Join(", ", missingRequiredSources)}.");
                }
                else
                {
                    errors.Add($"Preset '{preset.Name}' requires sources that are unavailable: {string.Join(", ", missingRequiredSources)}.");
                }
            }

            reports.Add(new FontDiagnosticPresetAvailabilityReport(
                preset.Name,
                requiredSources,
                availableSources,
                missingRequiredSources,
                degradedSources
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                warnings
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                errors
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                Complete: missingRequiredSources.Count == 0));
        }

        return reports;
    }
}
