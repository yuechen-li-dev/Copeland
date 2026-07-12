using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling.Tests;

internal static class ToolingIntegrationTestEnvironment
{
    public static string FindRepoRoot()
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

    public static string CreateDirectory(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
    }

    public static FontDiagnosticArtifactExporter CreateExporter()
    {
        return new FontDiagnosticArtifactExporter(
            CrimsonTextFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());
    }

    public static FontDiagnosticExportOptions CreateMinimalOptions(string outputDirectory)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "crimson-text-m11b-minimal",
            FontPath = CrimsonTextFixtureFont.FontPath,
            FontFamilyName = "Crimson Text",
            FontStyleName = "Regular",
            LicenseIdentifier = "OFL-1.1",
            Face = CrimsonTextFixtureFont.Face,
            PresetNames = ["direct-vs-msdf"],
            TextDefinitions =
            [
                new FontDiagnosticTextDefinition("hello-machina", "Hello Machina"),
            ],
            CanvasDefinitions =
            [
                new FontDiagnosticCanvasDefinition(32, 320, 64, 8d, 40d),
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

    public static FontDiagnosticExportOptions CreateDefaultIntegrationOptions(string outputDirectory)
    {
        return CreateMinimalOptions(outputDirectory) with
        {
            AtlasName = "crimson-text-m11b-default",
            PresetNames = ["direct-vs-msdf", "cad-debug"],
            TextDefinitions =
            [
                new FontDiagnosticTextDefinition("machina", "Machina"),
                new FontDiagnosticTextDefinition("hello-machina", "Hello Machina"),
            ],
            CanvasDefinitions =
            [
                new FontDiagnosticCanvasDefinition(32, 320, 64, 8d, 40d),
                new FontDiagnosticCanvasDefinition(64, 640, 128, 16d, 80d),
            ],
        };
    }

    public static FontDiagnosticExportOptions CreateScriptSmokeOptions(string outputDirectory)
    {
        FontDiagnosticExportOptions baseOptions = CreateDefaultIntegrationOptions(outputDirectory);
        FontDiagnosticGridOptions gridOptions = baseOptions.GridOptions with
        {
            ShowGrid = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_SHOW_GRID", baseOptions.GridOptions.ShowGrid),
            GridStep = ReadInt32("MACHINA_FONT_DIAGNOSTICS_GRID_STEP", baseOptions.GridOptions.GridStep),
            ShowUnitLabels = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_SHOW_UNIT_LABELS", baseOptions.GridOptions.ShowUnitLabels),
            ShowAxes = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_SHOW_AXES", baseOptions.GridOptions.ShowAxes),
            AxisStep = ReadInt32("MACHINA_FONT_DIAGNOSTICS_AXIS_STEP", baseOptions.GridOptions.AxisStep),
        };
        FontDiagnosticBoundsOverlayOptions boundsOptions = baseOptions.BoundsOptions with
        {
            ShowBounds = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_SHOW_BOUNDS", baseOptions.BoundsOptions.ShowBounds),
            ShowWireframes = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_SHOW_WIREFRAME", baseOptions.BoundsOptions.ShowWireframes),
        };

        string[] presets = ReadPresets(baseOptions.PresetNames);
        return baseOptions with
        {
            RepositoryRootDirectory = ReadString("MACHINA_FONT_DIAGNOSTICS_REPO_ROOT", baseOptions.RepositoryRootDirectory),
            PresetNames = presets,
            GridOptions = gridOptions,
            BoundsOptions = boundsOptions,
            StaticTextRenderStrategy = ReadTextBackend("MACHINA_FONT_DIAGNOSTICS_TEXT_BACKEND", baseOptions.StaticTextRenderStrategy),
            CleanOutputDirectory = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_CLEAN", baseOptions.CleanOutputDirectory),
            AllowPartial = ReadBoolean("MACHINA_FONT_DIAGNOSTICS_ALLOW_PARTIAL", baseOptions.AllowPartial),
        };
    }

    public static FontDiagnosticExportOptions CreateMsdfRegressionOptions(string outputDirectory, bool scaleExperimentalFieldWithEmSize)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "crimson-text-m11b-m9f-regression",
            FontPath = CrimsonTextFixtureFont.FontPath,
            FontFamilyName = "Crimson Text",
            FontStyleName = "Regular",
            LicenseIdentifier = "OFL-1.1",
            Face = CrimsonTextFixtureFont.Face,
            ScaleExperimentalFieldWithEmSize = scaleExperimentalFieldWithEmSize,
            PresetNames = ["direct-vs-msdf"],
            TextDefinitions =
            [
                new FontDiagnosticTextDefinition("hello-machina", "Hello Machina"),
                new FontDiagnosticTextDefinition("settings", "Settings"),
                new FontDiagnosticTextDefinition("machina-ui", "Machina UI"),
            ],
            CanvasDefinitions =
            [
                new FontDiagnosticCanvasDefinition(16, 320, 64, 8d, 24d),
                new FontDiagnosticCanvasDefinition(24, 320, 64, 8d, 32d),
                new FontDiagnosticCanvasDefinition(64, 720, 128, 16d, 80d),
            ],
        };
    }

    public static FontDiagnosticExportOptions CreateMsdfSmokeOptions(string outputDirectory, bool scaleExperimentalFieldWithEmSize)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "crimson-text-m9f",
            FontPath = CrimsonTextFixtureFont.FontPath,
            FontFamilyName = "Crimson Text",
            FontStyleName = "Regular",
            LicenseIdentifier = "OFL-1.1",
            Face = CrimsonTextFixtureFont.Face,
            ScaleExperimentalFieldWithEmSize = scaleExperimentalFieldWithEmSize,
            PresetNames = ["direct-vs-msdf", "cad-debug", "msdf-debug"],
            TextDefinitions =
            [
                new FontDiagnosticTextDefinition("hello-machina", "Hello Machina"),
                new FontDiagnosticTextDefinition("machina", "Machina"),
                new FontDiagnosticTextDefinition("machina-ui", "Machina UI"),
                new FontDiagnosticTextDefinition("settings", "Settings"),
                new FontDiagnosticTextDefinition("direct-outline-static-text", "Direct outline static text"),
                new FontDiagnosticTextDefinition("kerning", "AV To Ta Wa Yo"),
                new FontDiagnosticTextDefinition("aa0", "Aa0 1234567890"),
            ],
            CanvasDefinitions =
            [
                new FontDiagnosticCanvasDefinition(16, 360, 56, 8d, 24d),
                new FontDiagnosticCanvasDefinition(24, 520, 72, 8d, 32d),
                new FontDiagnosticCanvasDefinition(32, 720, 96, 8d, 44d),
                new FontDiagnosticCanvasDefinition(64, 1280, 168, 16d, 88d),
            ],
        };
    }

    public static string GetRequestedOutputDirectoryOrCreateTemp(string variableName, string tempPrefix)
    {
        string? requested = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return CreateDirectory(tempPrefix);
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
        string? value = Environment.GetEnvironmentVariable("MACHINA_FONT_DIAGNOSTICS_PRESET");
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultPresets.ToArray();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
