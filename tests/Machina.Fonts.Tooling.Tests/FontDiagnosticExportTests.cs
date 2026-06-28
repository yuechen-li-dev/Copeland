using System.Security.Cryptography;
using Machina.Fonts.Generation.MsdfSharp;
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

    [Fact]
    public async Task FontDiagnosticsExport_UsesLayerPreset()
    {
        string directory = CreateDirectory();
        FontDiagnosticExportOptions options = CreateOptions(directory) with
        {
            PresetNames = ["cad-debug"],
        };

        FontDiagnosticExportResult result = await CreateExporter().ExportAsync(options);

        Assert.Equal(6, result.LayerCompositionReport.Artifacts.Count);
        Assert.All(
            result.LayerCompositionReport.Artifacts,
            artifact =>
            {
                Assert.Equal("cad-debug", artifact.PresetName);
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
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9b-direct-vs-msdf-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "32", "m9b-cad-debug-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9b-direct-vs-msdf-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(directory, "64", "m9b-cad-debug-kerning.png")));
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

        foreach (string presetName in result.LayerCompositionReport.PresetsGenerated)
        {
            Assert.True(File.Exists(Path.Combine(directory, "32", $"m9b-{presetName}-machina.png")));
        }
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
            AtlasName = "crimson-text-m9b-diagnostics",
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
            PresetNames = presets,
            GridOptions = gridOptions,
            BoundsOptions = boundsOptions,
        };
    }

    private static IReadOnlyList<string> EnumerateRelativeArtifacts(string outputDirectory)
    {
        return Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(static path =>
                !string.Equals(Path.GetFileName(path), "shape-diff-report.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "shape-diff-report.txt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "layer-composition-report.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "layer-composition-report.txt", StringComparison.OrdinalIgnoreCase))
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
        return Path.Combine(Path.GetTempPath(), "machina-font-tooling-m9b-tests", Guid.NewGuid().ToString("N"));
    }
}
