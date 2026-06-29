using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Unit.Tests;

public sealed class FontDiagnosticOutputCleanerTests
{
    [Fact]
    public void ExportOptions_CleanRejectsRepoRoot()
    {
        string repoRoot = ToolingUnitTestEnvironment.FindRepoRoot();
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions(repoRoot) with
        {
            CleanOutputDirectory = true,
            RepositoryRootDirectory = repoRoot,
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FontDiagnosticOutputCleaner.PrepareOutputDirectory(options, repoRoot));

        Assert.Contains("repository root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportOptions_CleanDeletesExistingOutputDirectory()
    {
        string directory = ToolingUnitTestEnvironment.CreateDirectory();
        Directory.CreateDirectory(directory);
        string stalePath = Path.Combine(directory, "stale.txt");
        File.WriteAllText(stalePath, "stale");

        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions(directory) with
        {
            CleanOutputDirectory = true,
        };

        _ = FontDiagnosticOutputCleaner.PrepareOutputDirectory(options, directory);

        Assert.True(Directory.Exists(directory));
        Assert.False(File.Exists(stalePath));
    }

    [Fact]
    public void ExportOptions_CleanCreatesOutputDirectory()
    {
        string directory = ToolingUnitTestEnvironment.CreateDirectory();
        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions(directory) with
        {
            CleanOutputDirectory = true,
        };

        _ = FontDiagnosticOutputCleaner.PrepareOutputDirectory(options, directory);

        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void ExportOptions_WithoutCleanPreservesUnrelatedFiles()
    {
        string directory = ToolingUnitTestEnvironment.CreateDirectory();
        Directory.CreateDirectory(directory);
        string unrelatedFile = Path.Combine(directory, "keep-me.txt");
        File.WriteAllText(unrelatedFile, "keep");

        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions(directory);
        IReadOnlyList<string> warnings = FontDiagnosticOutputCleaner.PrepareOutputDirectory(options, directory);

        Assert.True(File.Exists(unrelatedFile));
        Assert.Contains(warnings, warning => warning.Contains("already contains files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportOptions_ReportsLockedFileOrFailsClearly()
    {
        string directory = ToolingUnitTestEnvironment.CreateDirectory();
        Directory.CreateDirectory(directory);
        string lockedPath = Path.Combine(directory, "locked.dfpage");
        File.WriteAllText(lockedPath, "locked");

        using FileStream stream = File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        FontDiagnosticExportOptions options = ToolingUnitTestEnvironment.CreateOptions(directory) with
        {
            CleanOutputDirectory = true,
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FontDiagnosticOutputCleaner.PrepareOutputDirectory(options, directory));

        Assert.Contains("locked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locked.dfpage", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
