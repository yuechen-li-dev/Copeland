using System.Security.Cryptography;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

[Collection("EnvironmentVariable")]
public sealed class FontDiagnosticExportTests
{
    private const string OutputDirectoryEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_OUTPUT_DIR";
    private const string PresetEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_PRESET";
    private const string ShowGridEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_SHOW_GRID";
    private const string GridStepEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_GRID_STEP";
    private const string ShowUnitLabelsEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_SHOW_UNIT_LABELS";
    private const string ShowAxesEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_SHOW_AXES";
    private const string AxisStepEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_AXIS_STEP";
    private const string ShowBoundsEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_SHOW_BOUNDS";
    private const string ShowWireframeEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_SHOW_WIREFRAME";
    private const string CleanEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_CLEAN";
    private const string AllowPartialEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_ALLOW_PARTIAL";
    private const string TextBackendEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_TEXT_BACKEND";
    private const string RepositoryRootEnvironmentVariable = "MACHINA_FONT_DIAGNOSTICS_REPO_ROOT";

    [Fact]
    public async Task FontDiagnosticsExport_UsesLayerPreset()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportOptions options = CreateOptions(directory) with
        {
            PresetNames = ["cad-debug"],
        };

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(options);

        Assert.Equal(8, result.LayerCompositionReport.Artifacts.Count);
        Assert.All(
            result.LayerCompositionReport.Artifacts,
            artifact =>
            {
                Assert.Equal("cad-debug", artifact.PresetName);
                Assert.True(artifact.PresetAvailability.Complete);
                Assert.Equal("DirectOutlineStatic", artifact.ReferenceRenderStrategy);
                Assert.Contains("DirectOutlineStatic", artifact.RenderStrategies);
                Assert.Contains(artifact.Layers, layer => layer.Id == "grid");
                Assert.Contains(artifact.Layers, layer => layer.Id == "axes");
                Assert.Contains(artifact.Layers, layer => layer.Id == "baseline");
            });
    }

    [Fact]
    public async Task FontDiagnosticsExport_WritesPresetArtifacts()
    {
        string directory = CreateDirectory();

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.LayerCompositionReportJsonPath));
        Assert.True(File.Exists(result.LayerCompositionReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-direct-vs-msdf-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-cad-debug-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9d-direct-vs-msdf-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9d-cad-debug-kerning.png")));
    }

    [Fact]
    public async Task FontDiagnosticsExport_IsDeterministic()
    {
        string firstDirectory = CreateDirectory();
        string secondDirectory = CreateDirectory();

        FontDiagnosticExportResult first = await CreateExporter().ExportAsync(CreateOptions(firstDirectory));
        FontDiagnosticExportResult second = await CreateExporter().ExportAsync(CreateOptions(secondDirectory));

        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ShapeDiffReportJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ShapeDiffReportJsonPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ShapeDiffReportTextPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ShapeDiffReportTextPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.LayerCompositionReportJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.LayerCompositionReportJsonPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.LayerCompositionReportTextPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.LayerCompositionReportTextPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ManifestJsonPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ManifestJsonPath), secondDirectory));
        Assert.Equal(
            NormalizeReport(File.ReadAllText(first.ManifestTextPath), firstDirectory),
            NormalizeReport(File.ReadAllText(second.ManifestTextPath), secondDirectory));

        foreach (string relativePath in EnumerateRelativeArtifacts(firstDirectory))
        {
            string firstPath = Path.Combine(firstDirectory, relativePath);
            string secondPath = Path.Combine(secondDirectory, relativePath);
            Assert.Equal(HashFile(firstPath), HashFile(secondPath));
        }
    }

    [Fact]
    public async Task FontDiagnosticsExport_ScriptWorkflowExportsArtifacts()
    {
        string directory = GetRequestedOutputDirectoryOrCreateTemp();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptionsFromEnvironment(directory));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.LayerCompositionReportJsonPath));
        Assert.True(File.Exists(result.LayerCompositionReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));

        foreach (string presetName in result.LayerCompositionReport.PresetsGenerated)
        {
            Assert.True(File.Exists(Path.Combine(directory, "32", $"m9d-{presetName}-machina.png")));
        }
    }

    [Fact]
    public async Task ExportOptions_CleanRejectsRepoRoot()
    {
        string repoRoot = FindRepoRoot();
        FontDiagnosticExportOptions options = CreateOptions(repoRoot) with
        {
            CleanOutputDirectory = true,
            RepositoryRootDirectory = repoRoot,
        };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateExporter().ExportAsync(options));

        Assert.Contains("repository root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportOptions_CleanDeletesExistingOutputDirectory()
    {
        string directory = CreateDirectory();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "stale.txt"), "stale");

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(
            CreateOptions(directory) with
            {
                CleanOutputDirectory = true,
                RepositoryRootDirectory = FindRepoRoot(),
            });

        Assert.False(File.Exists(Path.Combine(directory, "stale.txt")));
        Assert.True(File.Exists(result.ManifestJsonPath));
    }

    [Fact]
    public async Task ExportOptions_CleanCreatesOutputDirectory()
    {
        string directory = CreateDirectory();

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(
            CreateOptions(directory) with
            {
                CleanOutputDirectory = true,
                RepositoryRootDirectory = FindRepoRoot(),
            });

        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(result.ManifestTextPath));
    }

    [Fact]
    public async Task ExportOptions_WithoutCleanPreservesUnrelatedFiles()
    {
        string directory = CreateDirectory();
        Directory.CreateDirectory(directory);
        string unrelatedFile = Path.Combine(directory, "keep-me.txt");
        File.WriteAllText(unrelatedFile, "keep");

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.True(File.Exists(unrelatedFile));
        Assert.Contains(result.Manifest.Warnings, warning => warning.Contains("already contains files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportOptions_ReportsLockedFileOrFailsClearly()
    {
        string directory = CreateDirectory();
        Directory.CreateDirectory(directory);
        string lockedPath = Path.Combine(directory, "locked.dfpage");
        File.WriteAllText(lockedPath, "locked");

        using FileStream stream = File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        FontDiagnosticExportOptions options = CreateOptions(directory) with
        {
            CleanOutputDirectory = true,
            RepositoryRootDirectory = FindRepoRoot(),
        };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateExporter().ExportAsync(options));

        Assert.Contains("locked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locked.dfpage", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SourceAvailability_ReportsBrowserUnavailable()
    {
        string directory = CreateDirectory();

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.False(result.Manifest.Sources.BrowserReferenceAvailable);
        Assert.False(result.Manifest.Sources.BrowserMaskAvailable);
    }

    [Fact]
    public async Task SourceAvailability_ReportsDirectAndMsdfAvailable()
    {
        string directory = CreateDirectory();

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.True(result.Manifest.Sources.DirectOutlineAvailable);
        Assert.True(result.Manifest.Sources.MsdfAvailable);
        Assert.True(result.Manifest.Sources.DirectMaskAvailable);
        Assert.True(result.Manifest.Sources.MsdfMaskAvailable);
        Assert.True(result.Manifest.Sources.PlacementReportAvailable);
        Assert.True(result.Manifest.Sources.ShapeDiffReportAvailable);
    }

    [Fact]
    public async Task Export_StrictPresetFailsWhenBrowserMissing()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportOptions options = CreateOptions(directory) with
        {
            PresetNames = ["browser-vs-direct"],
        };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateExporter().ExportAsync(options));

        Assert.Contains("requires sources that are unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(directory, "font-diagnostic-export-manifest.json")));
        Assert.Contains(
            "browser-reference",
            File.ReadAllText(Path.Combine(directory, "font-diagnostic-export-manifest.txt")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_AllowPartialWritesWarningWhenBrowserMissing()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(
            CreateOptions(directory) with
            {
                PresetNames = ["browser-vs-direct"],
                AllowPartial = true,
            });

        Assert.False(result.Manifest.Complete);
        Assert.Contains(result.Manifest.Warnings, warning => warning.Contains("degraded", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Manifest.Errors);
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9d-browser-vs-direct-machina.png")));
    }

    [Fact]
    public async Task Export_DirectVsMsdfSucceedsWithoutBrowser()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(
            CreateOptions(directory) with
            {
                PresetNames = ["direct-vs-msdf"],
            });

        Assert.True(result.Manifest.Complete);
        Assert.Empty(result.Manifest.Errors);
        Assert.Contains(result.Manifest.PresetReports, report => report.PresetName == "direct-vs-msdf" && report.Complete);
    }

    [Fact]
    public async Task Export_WritesManifestJson()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.Contains("\"Kind\": \"machina-font-diagnostic-export\"", File.ReadAllText(result.ManifestJsonPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_WritesManifestText()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.Contains("Machina Font Toolkit M9d export manifest", File.ReadAllText(result.ManifestTextPath), StringComparison.Ordinal);
        Assert.Equal("DirectOutlineStatic", result.Manifest.TextBackend.StaticDefault);
        Assert.Equal("MsdfScalableExperimental", result.Manifest.TextBackend.ScalableExperimental);
    }

    [Fact]
    public async Task Manifest_UsesStableOrdering()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.Equal(
            result.Manifest.Presets.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            result.Manifest.Presets.ToArray());
        Assert.Equal(
            result.Manifest.Artifacts.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.Manifest.Artifacts.ToArray());
    }

    [Fact]
    public async Task Manifest_RecordsWarningsAndErrors()
    {
        string partialDirectory = CreateDirectory();
        FontDiagnosticExportResult partialResult = await CreateExporter().ExportAsync(
            CreateOptions(partialDirectory) with
            {
                PresetNames = ["browser-vs-direct"],
                AllowPartial = true,
            });

        Assert.NotEmpty(partialResult.Manifest.Warnings);
        Assert.Empty(partialResult.Manifest.Errors);

        string strictDirectory = CreateDirectory();
        FontDiagnosticExportOptions strictOptions = CreateOptions(strictDirectory) with
        {
            PresetNames = ["browser-vs-direct"],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExporter().ExportAsync(strictOptions));
        string strictManifestJson = File.ReadAllText(Path.Combine(strictDirectory, "font-diagnostic-export-manifest.json"));
        Assert.Contains("\"Errors\": [", strictManifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_DoesNotIncludeTimestampByDefault()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(CreateOptions(directory));

        Assert.Null(result.Manifest.GeneratedAtUtc);
        Assert.DoesNotContain("GeneratedAtUtc", File.ReadAllText(result.ManifestJsonPath), StringComparison.Ordinal);
    }

    private static FontDiagnosticArtifactExporter CreateExporter()
    {
        return new FontDiagnosticArtifactExporter(
            CrimsonTextFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());
    }

    private static FontDiagnosticExportOptions CreateOptions(string outputDirectory)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "crimson-text-m9d-diagnostics",
            FontPath = CrimsonTextFixtureFont.FontPath,
            FontFamilyName = "Crimson Text",
            FontStyleName = "Regular",
            LicenseIdentifier = "OFL-1.1",
            Face = CrimsonTextFixtureFont.Face,
            PresetNames = ["direct-vs-msdf", "cad-debug"],
            TextDefinitions =
            [
                new FontDiagnosticTextDefinition("machina", "Machina"),
                new FontDiagnosticTextDefinition("hello-machina", "Hello Machina"),
                new FontDiagnosticTextDefinition("kerning", "AV To Ta Wa Yo"),
                new FontDiagnosticTextDefinition("aa0", "Aa0"),
            ],
            CanvasDefinitions =
            [
                new FontDiagnosticCanvasDefinition(32, 320, 64, 8d, 40d),
                new FontDiagnosticCanvasDefinition(64, 640, 128, 16d, 80d),
            ],
            GridOptions = new FontDiagnosticGridOptions
            {
                ShowGrid = true,
                GridStep = 8,
                ShowUnitLabels = true,
                ShowAxes = true,
                AxisStep = 32,
                ShowBaseline = true,
            },
            BoundsOptions = new FontDiagnosticBoundsOverlayOptions
            {
                ShowBounds = true,
                ShowWireframes = true,
            },
        };
    }

    private static FontDiagnosticExportOptions CreateOptionsFromEnvironment(string outputDirectory)
    {
        FontDiagnosticExportOptions baseOptions = CreateOptions(outputDirectory);
        FontDiagnosticGridOptions gridOptions = baseOptions.GridOptions with
        {
            ShowGrid = ReadBoolean(ShowGridEnvironmentVariable, baseOptions.GridOptions.ShowGrid),
            GridStep = ReadInt32(GridStepEnvironmentVariable, baseOptions.GridOptions.GridStep),
            ShowUnitLabels = ReadBoolean(ShowUnitLabelsEnvironmentVariable, baseOptions.GridOptions.ShowUnitLabels),
            ShowAxes = ReadBoolean(ShowAxesEnvironmentVariable, baseOptions.GridOptions.ShowAxes),
            AxisStep = ReadInt32(AxisStepEnvironmentVariable, baseOptions.GridOptions.AxisStep),
        };
        FontDiagnosticBoundsOverlayOptions boundsOptions = baseOptions.BoundsOptions with
        {
            ShowBounds = ReadBoolean(ShowBoundsEnvironmentVariable, baseOptions.BoundsOptions.ShowBounds),
            ShowWireframes = ReadBoolean(ShowWireframeEnvironmentVariable, baseOptions.BoundsOptions.ShowWireframes),
        };

        string[] presets = ReadPresets(baseOptions.PresetNames);
        return baseOptions with
        {
            RepositoryRootDirectory = ReadString(RepositoryRootEnvironmentVariable, baseOptions.RepositoryRootDirectory),
            PresetNames = presets,
            GridOptions = gridOptions,
            BoundsOptions = boundsOptions,
            StaticTextRenderStrategy = ReadTextBackend(TextBackendEnvironmentVariable, baseOptions.StaticTextRenderStrategy),
            CleanOutputDirectory = ReadBoolean(CleanEnvironmentVariable, baseOptions.CleanOutputDirectory),
            AllowPartial = ReadBoolean(AllowPartialEnvironmentVariable, baseOptions.AllowPartial),
        };
    }

    private static IReadOnlyList<string> EnumerateRelativeArtifacts(string outputDirectory)
    {
        return Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(static path =>
                !string.Equals(Path.GetFileName(path), "shape-diff-report.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "shape-diff-report.txt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "layer-composition-report.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "layer-composition-report.txt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "font-diagnostic-export-manifest.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "font-diagnostic-export-manifest.txt", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(outputDirectory, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeReport(string content, string outputDirectory)
    {
        string fullPath = Path.GetFullPath(outputDirectory);
        string escapedPath = fullPath.Replace("\\", "\\\\", StringComparison.Ordinal);

        return content
            .Replace(fullPath, "<out>", StringComparison.OrdinalIgnoreCase)
            .Replace(escapedPath, "<out>", StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool ReadBoolean(string variableName, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return bool.TryParse(value, out bool parsed)
            ? parsed
            : defaultValue;
    }

    private static int ReadInt32(string variableName, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(value, out int parsed)
            ? parsed
            : defaultValue;
    }

    private static string? ReadString(string variableName, string? defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }

    private static MachinaTextRenderStrategy ReadTextBackend(string variableName, MachinaTextRenderStrategy defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : MachinaTextRenderStrategyCatalog.ParseStableName(value);
    }

    private static string[] ReadPresets(IReadOnlyList<string> defaultPresets)
    {
        string? value = Environment.GetEnvironmentVariable(PresetEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultPresets.ToArray();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetRequestedOutputDirectoryOrCreateTemp()
    {
        string? requested = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return CreateDirectory();
    }

    private static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-font-tooling-m9d-tests", Guid.NewGuid().ToString("N"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for tooling tests.");
    }
}
