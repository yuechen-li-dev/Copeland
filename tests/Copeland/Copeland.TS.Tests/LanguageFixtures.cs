using System.Text.RegularExpressions;

namespace Copeland.TS.Tests;

public enum LanguageFixtureVerdict
{
    Valid,
    Invalid,
}

public sealed record LanguageFixture(
    string RelativePath,
    LanguageFixtureVerdict Verdict,
    bool IsTsXml,
    IReadOnlyList<string> ExpectedDiagnosticIds);

/// <summary>
/// Convention-based language specimen discovery. A fixture's complete suffix is
/// its verdict; folders are only topical organization and never test authority.
/// </summary>
public static class LanguageFixtures
{
    private const string FixtureRootName = "Language";
    private static readonly FixtureSuffix[] Suffixes =
    [
        new(".cl-valid.ts", LanguageFixtureVerdict.Valid, false),
        new(".cl-invalid.ts", LanguageFixtureVerdict.Invalid, false),
        new(".cl-valid.tsx", LanguageFixtureVerdict.Valid, true),
        new(".cl-invalid.tsx", LanguageFixtureVerdict.Invalid, true),
    ];

    private static readonly Regex ExpectedDiagnosticPattern = new(
        @"^\s*//\s*expect:\s*(?<id>COPE-[A-Z0-9-]+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IEnumerable<object[]> Valid => GetTheoryData(LanguageFixtureVerdict.Valid);

    public static IEnumerable<object[]> Invalid => GetTheoryData(LanguageFixtureVerdict.Invalid);

    public static void AssertTopology()
    {
        LanguageFixture[] fixtures = Discover();
        if (fixtures.Length == 0)
        {
            throw new InvalidOperationException("Language fixture root contains no convention-named fixtures.");
        }

        foreach (FixtureSuffix suffix in Suffixes)
        {
            if (!fixtures.Any(fixture => fixture.RelativePath.EndsWith(suffix.Text, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Language fixture corpus contains no '{suffix.Text}' specimen.");
            }
        }
    }

    public static LanguageFixture[] Discover()
    {
        string root = GetFixtureRoot();
        var fixtures = new List<LanguageFixture>();

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(root, path));
            FixtureSuffix? suffix = Suffixes.FirstOrDefault(candidate =>
                relativePath.EndsWith(candidate.Text, StringComparison.Ordinal));

            if (suffix is null)
            {
                throw new InvalidOperationException(
                    $"Language fixture does not follow a canonical full suffix: {relativePath}");
            }

            string source = File.ReadAllText(path);
            string[] expectedDiagnostics = ExpectedDiagnosticPattern.Matches(source)
                .Select(match => match.Groups["id"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            fixtures.Add(new LanguageFixture(
                relativePath,
                suffix.Verdict,
                suffix.IsTsXml,
                expectedDiagnostics));
        }

        return fixtures
            .OrderBy(fixture => fixture.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ReadSourceText(LanguageFixture fixture)
        => ReadSourceText(fixture.RelativePath);

    public static string ReadSourceText(string relativePath)
    {
        string root = GetFixtureRoot();
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Language fixture path escapes the fixture root.", nameof(relativePath));
        }

        return File.ReadAllText(fullPath);
    }

    private static IEnumerable<object[]> GetTheoryData(LanguageFixtureVerdict verdict)
    {
        AssertTopology();
        return Discover()
            .Where(fixture => fixture.Verdict == verdict)
            .Select(fixture => new object[] { fixture })
            .ToArray();
    }

    private static string GetFixtureRoot()
    {
        string root = Path.Combine(AppContext.BaseDirectory, FixtureRootName);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Language fixture root was not copied to test output: {root}. Ensure Copeland.TS.Tests copies Language/**/*.");
        }

        return Path.GetFullPath(root);
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private sealed record FixtureSuffix(string Text, LanguageFixtureVerdict Verdict, bool IsTsXml);
}
