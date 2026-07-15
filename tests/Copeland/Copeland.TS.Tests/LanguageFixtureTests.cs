using Copeland.TS.Compiler;
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
    public void Valid_language_fixture_lowers_to_mir(string relativePath)
    {
        var compilation = CopelandCompiler.CompileToMir(LanguageFixtures.ReadSourceText(relativePath));

        Assert.True(compilation.Success, DescribeDiagnostics(relativePath, compilation.Diagnostics));
        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(compilation.BoundCompilation);
        Assert.NotNull(compilation.MirCompilation?.Program);
        Assert.NotNull(compilation.MirText);
    }

    [Theory]
    [MemberData(nameof(LanguageFixtures.Invalid), MemberType = typeof(LanguageFixtures))]
    public void Invalid_language_fixture_is_rejected_at_validation(string relativePath)
    {
        var compilation = CopelandCompiler.CompileToMir(LanguageFixtures.ReadSourceText(relativePath));

        Assert.NotEmpty(compilation.Diagnostics);
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-LEX", StringComparison.Ordinal)
            || diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        bool hasFamilyOwnedSyntaxDiagnostic = compilation.Diagnostics.Any(diagnostic =>
            diagnostic.Id.StartsWith("COPE-UNION", StringComparison.Ordinal)
            || diagnostic.Id.StartsWith("COPE-MATCH", StringComparison.Ordinal)
            || diagnostic.Id.StartsWith("COPE-TRY", StringComparison.Ordinal)
            || diagnostic.Id is "COPE-ALIAS-0001" or "COPE-ALIAS-0002");
        if (!hasFamilyOwnedSyntaxDiagnostic)
        {
            Assert.NotNull(compilation.BoundCompilation);
        }
        Assert.False(compilation.Success);
        Assert.Null(compilation.MirCompilation);
        Assert.Null(compilation.MirText);
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
