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
        foreach (var corpus in new[] { "m0-csharp-valid", "m1-enum-match-csharp-valid", "m1-record-csharp-valid", "m1-table-csharp-valid", "cts-union-m0b", "cts-call-m0b", "cts-class-m1" })
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

        Assert.Equal("18326C9663290677C9B581F2F7BA9C7BCDE4B5408B32DC2187FD8A96156D2D30", hash);
    }

    [Fact]
    public void Inferred_reuse_csharp_artifact_has_a_stable_hash()
    {
        string path = Path.Combine(GetCorpusRoot(), "m0-csharp-valid", "inferred-reuse.g.cs");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Equal("3E983E41DB6658CB9D9F5513A3958F871D18FAE4E4621ECBAA39EFF507A891DA", hash);
    }

    [Fact]
    public void Pure_class_csharp_artifact_has_a_stable_hash()
    {
        string path = Path.Combine(GetCorpusRoot(), "cts-class-m1", "main.g.cs");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Equal("2019048E4022B5A26DE666A9E93E14C23C0DEFF23DA36C7E29A9A50C25A45AE1", hash);
    }

    private static string GetCorpusRoot() => CorpusFile.GetCorpusRoot();

}
