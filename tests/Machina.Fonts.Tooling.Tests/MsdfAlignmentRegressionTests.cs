using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class MsdfAlignmentRegressionTests
{
    private const string OutputDirectoryEnvironmentVariable = "MACHINA_M9F_OUTPUT_DIR";
    private const string ScaleFieldsEnvironmentVariable = "MACHINA_M9F_SCALE_FIELDS";

    [Fact]
    public async Task MsdfAlignment_HelloMachina_ImprovesAgainstDirectOutline()
    {
        ExportPair exports = await ExportBeforeAndAfterAsync();

        FontShapeDiff before = FindFixture(exports.Before, 64, "Hello Machina").DirectVsMsdf;
        FontShapeDiff after = FindFixture(exports.After, 64, "Hello Machina").DirectVsMsdf;

        Assert.True(after.IntersectionOverUnion > before.IntersectionOverUnion + 0.05d);
        Assert.True(after.P95EdgeDistance < before.P95EdgeDistance);
    }

    [Fact]
    public async Task MsdfAlignment_Settings_ImprovesAgainstDirectOutline()
    {
        ExportPair exports = await ExportBeforeAndAfterAsync();

        double beforeAverageIou = AverageIou(exports.Before, "Settings");
        double afterAverageIou = AverageIou(exports.After, "Settings");
        double beforeAverageP95 = AverageP95(exports.Before, "Settings");
        double afterAverageP95 = AverageP95(exports.After, "Settings");

        Assert.True(afterAverageIou >= beforeAverageIou - 0.01d);
        Assert.True(afterAverageP95 < beforeAverageP95);
    }

    [Fact]
    public async Task MsdfAlignment_DoesNotRegressSmallUiSizes()
    {
        ExportPair exports = await ExportBeforeAndAfterAsync();

        foreach (int size in new[] { 16, 24 })
        {
            foreach (string text in new[] { "Hello Machina", "Settings", "Machina UI" })
            {
                FontShapeDiff before = FindFixture(exports.Before, size, text).DirectVsMsdf;
                FontShapeDiff after = FindFixture(exports.After, size, text).DirectVsMsdf;

                Assert.True(
                    after.IntersectionOverUnion >= before.IntersectionOverUnion - 0.03d,
                    $"Expected no material regression for '{text}' at {size}px. Before={before.IntersectionOverUnion:0.000}, After={after.IntersectionOverUnion:0.000}.");
            }
        }
    }

    [Fact]
    public async Task MsdfAlignmentExport_M9fWorkflowExportsArtifacts()
    {
        string outputDirectory = GetRequestedOutputDirectoryOrCreateTemp();
        bool scaleExperimentalFieldWithEmSize = ReadBoolean(ScaleFieldsEnvironmentVariable, defaultValue: true);

        FontDiagnosticArtifactExporter exporter = new(
            CrimsonTextFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        FontDiagnosticExportResult result = await exporter.ExportAsync(CreateArtifactWorkflowOptions(outputDirectory, scaleExperimentalFieldWithEmSize));

        Assert.True(File.Exists(result.ShapeDiffReportJsonPath));
        Assert.True(File.Exists(result.ShapeDiffReportTextPath));
        Assert.True(File.Exists(result.ManifestJsonPath));
        Assert.True(File.Exists(result.ManifestTextPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-direct-vs-msdf-settings.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-msdf-debug-hello-machina.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "64", "m9d-cad-debug-hello-machina.png")));
    }

    private static async Task<ExportPair> ExportBeforeAndAfterAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "machina-m9f-alignment", Guid.NewGuid().ToString("N"));
        string beforeDirectory = Path.Combine(root, "before");
        string afterDirectory = Path.Combine(root, "after");

        try
        {
            FontDiagnosticArtifactExporter exporter = new(
                CrimsonTextFixtureFont.CreateSource(),
                new MsdfSharpDistanceFieldGenerator());

            FontDiagnosticExportResult before = await exporter.ExportAsync(CreateOptions(beforeDirectory, scaleExperimentalFieldWithEmSize: false));
            FontDiagnosticExportResult after = await exporter.ExportAsync(CreateOptions(afterDirectory, scaleExperimentalFieldWithEmSize: true));

            return new ExportPair(before.ShapeDiffReport, after.ShapeDiffReport);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static FontDiagnosticExportOptions CreateOptions(string outputDirectory, bool scaleExperimentalFieldWithEmSize)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "crimson-text-m9f-alignment",
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
                new FontDiagnosticCanvasDefinition(32, 400, 80, 8d, 40d),
                new FontDiagnosticCanvasDefinition(64, 720, 128, 16d, 80d),
            ],
        };
    }

    private static FontDiagnosticExportOptions CreateArtifactWorkflowOptions(string outputDirectory, bool scaleExperimentalFieldWithEmSize)
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

    private static FontShapeDiffFixtureReport FindFixture(FontShapeDiffReport report, int sizePx, string text)
    {
        FontShapeDiffSizeReport size = Assert.Single(report.Sizes, item => item.SizePx == sizePx);
        return Assert.Single(size.Fixtures, item => string.Equals(item.Text, text, StringComparison.Ordinal));
    }

    private static double AverageIou(FontShapeDiffReport report, string text)
    {
        return report.Sizes
            .SelectMany(static size => size.Fixtures)
            .Where(fixture => string.Equals(fixture.Text, text, StringComparison.Ordinal))
            .Average(static fixture => fixture.DirectVsMsdf.IntersectionOverUnion);
    }

    private static double AverageP95(FontShapeDiffReport report, string text)
    {
        return report.Sizes
            .SelectMany(static size => size.Fixtures)
            .Where(fixture => string.Equals(fixture.Text, text, StringComparison.Ordinal))
            .Average(static fixture => fixture.DirectVsMsdf.P95EdgeDistance);
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

    private static bool ReadBoolean(string variableName, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return bool.TryParse(value, out bool parsed)
            ? parsed
            : defaultValue;
    }

    private static string GetRequestedOutputDirectoryOrCreateTemp()
    {
        string? requested = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return Path.Combine(Path.GetTempPath(), "machina-m9f-artifacts", Guid.NewGuid().ToString("N"));
    }

    private sealed record ExportPair(
        FontShapeDiffReport Before,
        FontShapeDiffReport After);
}
