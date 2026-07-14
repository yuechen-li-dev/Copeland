using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonFixtureTests
{
    [Fact]
    public void Fixture_topology_is_complete_and_owned()
    {
        var root = GetFixtureRoot();
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "Valid"), "*.obj.ts", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "Valid"), "*.tson", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "Invalid"), "*.obj.ts", SearchOption.AllDirectories));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void Valid_filesystem_fixture_round_trips(string path)
    {
        var source = File.ReadAllText(path);
        var profile = path.EndsWith(".tson", StringComparison.Ordinal)
            ? TsonDocumentProfile.CanonicalTson
            : TsonDocumentProfile.ObjectTypeScript;

        var result = TsonDocumentReader.ReadSelfDescribed(source, profile);

        Assert.True(result.Success, Describe(path, result));
        var canonical = TsonCanonicalPrinter.Print(result.Document!);
        var reparsed = TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson);
        Assert.True(reparsed.Success, Describe(path, reparsed));
        Assert.Equal(canonical, TsonCanonicalPrinter.Print(reparsed.Document!));
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void Invalid_filesystem_fixture_has_expected_diagnostic(string path, string expectedCode)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            File.ReadAllText(path),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        if (expectedCode.StartsWith("COPE-PARSE", StringComparison.Ordinal)
            || expectedCode.StartsWith("COPE-LEX", StringComparison.Ordinal))
        {
            Assert.Contains(result.SyntaxDiagnostics, item => item.Id == expectedCode);
        }
        else
        {
            Assert.Contains(result.Diagnostics, item => item.Code == expectedCode && item.Length > 0);
        }
    }

    public static IEnumerable<object[]> ValidFixtures()
    {
        return Directory.GetFiles(Path.Combine(GetFixtureRoot(), "Valid"), "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".obj.ts", StringComparison.Ordinal)
                || path.EndsWith(".tson", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> InvalidFixtures()
    {
        return Directory.GetFiles(Path.Combine(GetFixtureRoot(), "Invalid"), "*.obj.ts", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { path, ReadExpectedCode(path) });
    }

    private static string GetFixtureRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "Tson");
    }

    private static string ReadExpectedCode(string path)
    {
        var firstLine = File.ReadLines(path).First();
        const string prefix = "// expected: ";
        if (!firstLine.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid TSON fixture lacks an expected diagnostic header: {path}");
        }

        return firstLine[prefix.Length..];
    }

    private static string Describe(string path, TsonReadResult result)
    {
        var diagnostics = result.SyntaxDiagnostics.Select(item => $"{item.Id}: {item.Message}")
            .Concat(result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return $"{path}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}";
    }
}
