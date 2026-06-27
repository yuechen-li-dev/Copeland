using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Semantics;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;
using Copeland.Script.Tests.Corpus;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class BinderCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Binder_Corpus_Matches_Expected(string sourcePath)
    {
        var source = CorpusFile.ReadSourceText(sourcePath);
        var tree = SyntaxTree.Parse(source);
        var compilation = Binder.Bind(tree);

        var boundPath = Path.ChangeExtension(sourcePath, ".bound.txt");
        if (File.Exists(boundPath))
        {
            var actual = BoundTreeDumper.Dump(compilation.Program);
            Assert.Equal(CorpusFile.Normalize(File.ReadAllText(boundPath)), CorpusFile.Normalize(actual));
        }

        var diagPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagPath))
        {
            var actual = DumpDiagnostics(compilation.Diagnostics.Where(d => d.Id.StartsWith("COPE-BIND") || d.Id.StartsWith("COPE-TYPE") || d.Id.StartsWith("COPE-PROFILE") || d.Id.StartsWith("COPE-ENUM")).ToArray());
            Assert.Equal(CorpusFile.Normalize(File.ReadAllText(diagPath)), CorpusFile.Normalize(actual));
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var repoRoot = CorpusFile.GetRepoRoot();
        foreach (var dir in new[] { "m0-bind-valid", "m0-bind-invalid", "m1-enum-bind-valid", "m1-enum-bind-invalid", "m1-match-bind-valid", "m1-match-bind-invalid" })
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
