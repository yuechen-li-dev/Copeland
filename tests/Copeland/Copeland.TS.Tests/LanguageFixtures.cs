namespace Copeland.TS.Tests;

public static class LanguageFixtures
{
    private const string FixtureRootName = "Language";
    private const string ValidRootName = "Valid";
    private const string InvalidRootName = "Invalid";
    private const string ValidSuffix = ".cl-valid.ts";
    private const string InvalidSuffix = ".cl-invalid.ts";

    public static IEnumerable<object[]> Valid => GetTheoryData(ValidRootName, ValidSuffix);

    public static IEnumerable<object[]> Invalid => GetTheoryData(InvalidRootName, InvalidSuffix);

    public static void AssertTopology()
    {
        var root = GetFixtureRoot();
        ValidateAllFixtureNames(root);
        EnsureFixtureDirectoryExists(root, ValidRootName);
        EnsureFixtureDirectoryExists(root, InvalidRootName);
        EnsureFixturesExist(root, ValidRootName, ValidSuffix);
        EnsureFixturesExist(root, InvalidRootName, InvalidSuffix);
    }

    public static string ReadSourceText(string relativePath)
    {
        var root = GetFixtureRoot();
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Language fixture path escapes the fixture root.", nameof(relativePath));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Language fixture does not exist: {normalizedRelativePath}", fullPath);
        }

        return File.ReadAllText(fullPath);
    }

    private static IEnumerable<object[]> GetTheoryData(string category, string suffix)
    {
        var root = GetFixtureRoot();
        AssertTopology();

        return EnumerateFixtures(root, category, suffix)
            .Select(relativePath => new object[] { relativePath })
            .ToArray();
    }

    private static string GetFixtureRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, FixtureRootName);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Language fixture root was not copied to test output: {root}. " +
                "Ensure Copeland.TS.Tests copies Language/**/*.");
        }

        return Path.GetFullPath(root);
    }

    private static void ValidateAllFixtureNames(string root)
    {
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, filePath));

            if (relativePath.EndsWith(".cope", StringComparison.Ordinal)
                || relativePath.EndsWith(".g.cs", StringComparison.Ordinal)
                || relativePath.EndsWith(".g.js", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Language fixtures cannot contain generated or MIR artifacts: {relativePath}");
            }

            var isValidFixture = relativePath.StartsWith(ValidRootName + "/", StringComparison.Ordinal)
                && relativePath.EndsWith(ValidSuffix, StringComparison.Ordinal);
            var isInvalidFixture = relativePath.StartsWith(InvalidRootName + "/", StringComparison.Ordinal)
                && relativePath.EndsWith(InvalidSuffix, StringComparison.Ordinal);

            if (!isValidFixture && !isInvalidFixture)
            {
                throw new InvalidOperationException(
                    $"Language fixture does not follow the required suffix convention: {relativePath}");
            }
        }
    }

    private static void EnsureFixtureDirectoryExists(string root, string category)
    {
        var categoryPath = Path.Combine(root, category);
        if (!Directory.Exists(categoryPath))
        {
            throw new DirectoryNotFoundException($"Language fixture directory is missing: {categoryPath}");
        }
    }

    private static void EnsureFixturesExist(string root, string category, string suffix)
    {
        if (!EnumerateFixtures(root, category, suffix).Any())
        {
            throw new InvalidOperationException($"Language/{category} must contain at least one {suffix} fixture.");
        }
    }

    private static IEnumerable<string> EnumerateFixtures(string root, string category, string suffix)
    {
        var categoryPath = Path.Combine(root, category);
        return Directory.EnumerateFiles(categoryPath, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(suffix, StringComparison.Ordinal))
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(root, path)))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
