using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LanguageFixtureTests
{
    [Fact]
    public void Language_fixture_topology_is_valid()
    {
        LanguageFixtures.AssertTopology();
    }

    [Theory]
    [MemberData(nameof(LanguageFixtures.Valid), MemberType = typeof(LanguageFixtures))]
    public void Valid_language_fixture_reaches_its_language_boundary(LanguageFixture fixture)
    {
        string source = LanguageFixtures.ReadSourceText(fixture);
        if (fixture.IsTsXml)
        {
            SyntaxTree tree = SyntaxTree.Parse(source, fixture.RelativePath);
            Assert.Empty(tree.Diagnostics);
            return;
        }

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, DescribeDiagnostics(fixture.RelativePath, compilation.Diagnostics));
        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(compilation.BoundCompilation);
        Assert.NotNull(compilation.MirCompilation?.Program);
        Assert.NotNull(compilation.MirText);
    }

    [Theory]
    [MemberData(nameof(LanguageFixtures.Invalid), MemberType = typeof(LanguageFixtures))]
    public void Invalid_language_fixture_is_rejected_for_its_declared_reason(LanguageFixture fixture)
    {
        string source = LanguageFixtures.ReadSourceText(fixture);
        if (fixture.IsTsXml)
        {
            SyntaxTree tree = SyntaxTree.Parse(source, fixture.RelativePath);
            Assert.NotEmpty(tree.Diagnostics);
            AssertExpectedDiagnostics(fixture, tree.Diagnostics.Select(diagnostic => diagnostic.Id));
            return;
        }

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.NotEmpty(compilation.Diagnostics);
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-LEX", StringComparison.Ordinal)
            || diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.False(compilation.Success);
        Assert.Null(compilation.MirCompilation);
        Assert.Null(compilation.MirText);
        AssertExpectedDiagnostics(fixture, compilation.Diagnostics.Select(diagnostic => diagnostic.Id));
    }

    private static void AssertExpectedDiagnostics(LanguageFixture fixture, IEnumerable<string> actualIds)
    {
        foreach (string expectedId in fixture.ExpectedDiagnosticIds)
        {
            Assert.Contains(expectedId, actualIds);
        }
    }

    private static string DescribeDiagnostics(
        string relativePath,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
    {
        var details = string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));

        return $"Language fixture failed: {relativePath}{Environment.NewLine}{details}";
    }
}
