using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Mir;
using Copeland.Script.Syntax;
using Copeland.Script.Tests.Corpus;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class MirCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Mir_Corpus_Matches_Expected(string sourcePath)
    {
        var source = CorpusFile.ReadSourceText(sourcePath);
        var tree = SyntaxTree.Parse(source);
        var mir = MirLowerer.Lower(tree);

        var copePath = Path.ChangeExtension(sourcePath, ".cope");
        if (File.Exists(copePath))
        {
            Assert.NotNull(mir.Program);
            var actual = MirTextWriter.Write(mir.Program!);
            Assert.Equal(CorpusFile.Normalize(File.ReadAllText(copePath)), CorpusFile.Normalize(actual));
        }

        var diagPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagPath))
        {
            var actual = DumpDiagnostics(mir.Diagnostics.Where(d => d.Id.StartsWith("COPE-BIND") || d.Id.StartsWith("COPE-TYPE") || d.Id.StartsWith("COPE-PROFILE")).ToArray());
            Assert.Equal(CorpusFile.Normalize(File.ReadAllText(diagPath)), CorpusFile.Normalize(actual));
            Assert.Null(mir.Program);
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var repoRoot = CorpusFile.GetRepoRoot();
        foreach (var dir in new[] { "m0-mir-valid", "m0-mir-invalid", "m1-enum-match-mir-valid" })
        {
            var fullDir = Path.Combine(repoRoot, "testdata", dir);
            if (!Directory.Exists(fullDir)) continue;
            foreach (var sourcePath in Directory.EnumerateFiles(fullDir, "*.ts", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
                yield return new object[] { sourcePath };
        }
    }

    private static string DumpDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var d in diagnostics) sb.AppendLine($"{d.Id}|{d.Position}|{d.Length}|{d.Message}");
        return sb.ToString();
    }

}
