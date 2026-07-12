using System.Text.Json;
using System.Text.Json.Serialization;
using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class FontDiagnosticManifestBuilderTests
{
    [Fact]
    public void ManifestBuilder_CanBuildWithoutRendering()
    {
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions("artifacts\\m11b");
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: ["source warning"],
            errors: [],
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);
        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> presetReports =
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(options.PresetNames, availability, allowPartial: false);

        FontDiagnosticExportManifest manifest = FontDiagnosticManifestBuilder.CreateManifest(
            options,
            "C:\\temp\\m11b",
            availability,
            presetReports,
            ["b/artifact.png", "a/artifact.txt", "a/artifact.txt"],
            warnings: ["outer warning"],
            errors: []);

        Assert.Equal("machina-font-diagnostic-export", manifest.Kind);
        Assert.Equal(["cad-debug", "direct-vs-msdf"], manifest.Presets);
        Assert.Equal(["a/artifact.txt", "b/artifact.png"], manifest.Artifacts);
        Assert.Contains("outer warning", manifest.Warnings);
        Assert.Contains("source warning", manifest.Warnings);
        Assert.True(manifest.Complete);
    }

    [Fact]
    public void Manifest_DoesNotIncludeTimestampByDefault()
    {
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions("artifacts\\m11b");
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);

        FontDiagnosticExportManifest manifest = FontDiagnosticManifestBuilder.CreateManifest(
            options,
            "C:\\temp\\m11b",
            availability,
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(options.PresetNames, availability, allowPartial: false),
            artifacts: [],
            warnings: [],
            errors: []);

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });

        Assert.Null(manifest.GeneratedAtUtc);
        Assert.DoesNotContain("GeneratedAtUtc", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_UsesStableOrdering()
    {
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions("artifacts\\m11b") with
        {
            PresetNames = ["direct-vs-msdf", "cad-debug", "direct-vs-msdf"],
        };
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);

        FontDiagnosticExportManifest manifest = FontDiagnosticManifestBuilder.CreateManifest(
            options,
            "C:\\temp\\m11b",
            availability,
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(options.PresetNames, availability, allowPartial: false),
            ["z/file.txt", "a/file.txt", "m/file.txt"],
            warnings: [],
            errors: []);

        Assert.Equal(["cad-debug", "direct-vs-msdf"], manifest.Presets);
        Assert.Equal(["a/file.txt", "m/file.txt", "z/file.txt"], manifest.Artifacts);
    }

    [Fact]
    public void Manifest_RecordsWarningsAndErrors()
    {
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions("artifacts\\m11b") with
        {
            PresetNames = ["browser-vs-direct"],
        };
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: ["source error"],
            placementReportAvailable: false,
            shapeDiffReportAvailable: false);
        IReadOnlyList<FontDiagnosticPresetAvailabilityReport> presetReports =
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(options.PresetNames, availability, allowPartial: false);

        FontDiagnosticExportManifest manifest = FontDiagnosticManifestBuilder.CreateManifest(
            options,
            "C:\\temp\\m11b",
            availability,
            presetReports,
            artifacts: [],
            warnings: ["outer warning"],
            errors: ["outer error"]);

        Assert.Contains("outer warning", manifest.Warnings);
        Assert.Contains("outer error", manifest.Errors);
        Assert.Contains("source error", manifest.Errors);
        Assert.Contains(manifest.Errors, error => error.Contains("requires sources that are unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.False(manifest.Complete);
    }

    [Fact]
    public void ManifestTextReport_ReflectsBuiltManifest()
    {
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions("artifacts\\m11b");
        FontDiagnosticSourceAvailability availability = FontDiagnosticSourceAvailabilityEvaluator.Create(
            warnings: [],
            errors: [],
            placementReportAvailable: true,
            shapeDiffReportAvailable: true);

        FontDiagnosticExportManifest manifest = FontDiagnosticManifestBuilder.CreateManifest(
            options,
            "C:\\temp\\m11b",
            availability,
            FontDiagnosticPresetRequirementEvaluator.EvaluatePresetAvailability(options.PresetNames, availability, allowPartial: false),
            ["32\\m9d-direct-vs-msdf-hello-machina.png"],
            warnings: [],
            errors: []);

        string report = FontDiagnosticManifestBuilder.BuildTextReport(manifest);

        Assert.Contains("Machina Font Toolkit M9d export manifest", report, StringComparison.Ordinal);
        Assert.Contains("staticDefault: DirectOutlineStatic", report, StringComparison.Ordinal);
        Assert.Contains("scalableExperimental: MsdfScalableExperimental", report, StringComparison.Ordinal);
        Assert.Contains("32\\m9d-direct-vs-msdf-hello-machina.png", report, StringComparison.Ordinal);
    }
}
