using Copeland.TS.Backend.CSharp;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using System.Security.Cryptography;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

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
        var corpusRoot = CorpusFile.GetCorpusRoot();
        foreach (var corpus in new[] { "m0-csharp-valid", "m1-enum-match-csharp-valid", "m1-record-csharp-valid", "m1-table-csharp-valid" })
        {
            var dir = Path.Combine(corpusRoot, corpus);
            foreach (var sourcePath in Directory.EnumerateFiles(dir, "*.ts", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
                yield return new object[] { sourcePath };
        }
    }

    [Fact]
    public void Table_csharp_artifact_has_a_stable_hash()
    {
        string path = Path.Combine(GetCorpusRoot(), "m1-table-csharp-valid", "empty-table.g.cs");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Equal("B83CAA6470B05E46947F8F66591E9C0428377C642C0555BE1E1F62526FDE955A", hash);
    }

    private static string GetCorpusRoot() => CorpusFile.GetCorpusRoot();

}
