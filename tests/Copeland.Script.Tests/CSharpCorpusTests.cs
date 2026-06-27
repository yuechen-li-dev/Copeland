using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Mir;
using Copeland.Script.Syntax;
using Copeland.Script.Tests.Corpus;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class CSharpCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void CSharp_Corpus_Matches_Expected(string sourcePath)
    {
        var source = CorpusFile.ReadSourceText(sourcePath);
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.NotNull(mir.Program);
        Assert.DoesNotContain(mir.Diagnostics, d => d.Id.StartsWith("COPE-", StringComparison.Ordinal));

        var expectedPath = Path.ChangeExtension(sourcePath, ".g.cs");
        var actual = CSharpBackend.Emit(mir.Program!).SourceText;
        Assert.Equal(CorpusFile.Normalize(File.ReadAllText(expectedPath)), CorpusFile.Normalize(actual));
    }

    public static IEnumerable<object[]> GetCases()
    {
        var root = CorpusFile.GetRepoRoot();
        foreach (var corpus in new[] { "m0-csharp-valid", "m1-enum-match-csharp-valid" })
        {
            var dir = Path.Combine(root, "testdata", corpus);
            foreach (var sourcePath in Directory.EnumerateFiles(dir, "*.ts", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
                yield return new object[] { sourcePath };
        }
    }

}
