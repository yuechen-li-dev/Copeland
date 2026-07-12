namespace Machina.Fonts.Tooling;

public static class FontDiagnosticOutputCleaner
{
    public static IReadOnlyList<string> PrepareOutputDirectory(FontDiagnosticExportOptions options, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (options.CleanOutputDirectory)
        {
            ValidateCleanOutputDirectory(options, outputDirectory);
        }

        if (Directory.Exists(outputDirectory))
        {
            if (options.CleanOutputDirectory)
            {
                try
                {
                    Directory.Delete(outputDirectory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    string? lockedPath = TryFindLockedPath(outputDirectory);
                    string target = lockedPath ?? outputDirectory;
                    throw new InvalidOperationException(
                        $"Unable to clean diagnostic output directory '{outputDirectory}'. Locked or inaccessible path: '{target}'.",
                        ex);
                }
            }
            else if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                return
                [
                    $"Output directory '{outputDirectory}' already contains files. Existing artifacts may be overwritten and stale files may remain."
                ];
            }
        }

        Directory.CreateDirectory(outputDirectory);
        return [];
    }

    public static void ValidateCleanOutputDirectory(FontDiagnosticExportOptions options, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string normalizedOutputDirectory = NormalizeDirectoryPath(outputDirectory);
        if (string.IsNullOrWhiteSpace(normalizedOutputDirectory))
        {
            throw new InvalidOperationException("Clean export requires a non-empty output directory.");
        }

        string root = NormalizeDirectoryPath(Path.GetPathRoot(normalizedOutputDirectory)!);
        if (string.Equals(normalizedOutputDirectory, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean root directory '{normalizedOutputDirectory}'.");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile)
            && string.Equals(normalizedOutputDirectory, NormalizeDirectoryPath(userProfile), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean user profile root '{normalizedOutputDirectory}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.RepositoryRootDirectory)
            && string.Equals(
                normalizedOutputDirectory,
                NormalizeDirectoryPath(options.RepositoryRootDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean repository root '{normalizedOutputDirectory}'.");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string? TryFindLockedPath(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return path;
            }
        }

        return null;
    }
}
