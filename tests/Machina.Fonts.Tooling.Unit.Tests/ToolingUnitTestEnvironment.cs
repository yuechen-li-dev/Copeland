using Machina.Fonts;

namespace Machina.Fonts.Tooling.Unit.Tests;

internal static class ToolingUnitTestEnvironment
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

        throw new InvalidOperationException("Could not locate repository root for tooling unit tests.");
    }

    public static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-font-tooling-unit-tests", Guid.NewGuid().ToString("N"));
    }

    public static FontDiagnosticExportOptions CreateOptions(string outputDirectory)
    {
        return new FontDiagnosticExportOptions
        {
            OutputDirectory = outputDirectory,
            RepositoryRootDirectory = FindRepoRoot(),
            AtlasName = "tooling-unit-tests",
            FontPath = "fixture-font-not-required.ttf",
            FontFamilyName = "Fixture Font",
            FontStyleName = "Regular",
            LicenseIdentifier = "TEST",
            Face = new FontFaceId("fixture-face"),
            PresetNames = ["direct-vs-msdf", "cad-debug"],
            TextDefinitions = [new FontDiagnosticTextDefinition("hello-machina", "Hello Machina")],
            CanvasDefinitions = [new FontDiagnosticCanvasDefinition(32, 320, 64, 8d, 40d)],
        };
    }
}
