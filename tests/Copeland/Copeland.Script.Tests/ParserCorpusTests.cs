using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Syntax;
using Copeland.Script.Tests.Corpus;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class ParserCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Parser_Corpus_Matches_Expected(string sourcePath)
    {
        var source = CorpusFile.ReadSourceText(sourcePath);
        var tree = SyntaxTree.Parse(source);

        var treePath = Path.ChangeExtension(sourcePath, ".tree.txt");
        if (File.Exists(treePath))
        {
            var actualTree = SyntaxTreeDumper.Dump(tree.Root);
            var expectedTree = CorpusFile.Normalize(File.ReadAllText(treePath));
            Assert.Equal(expectedTree, CorpusFile.Normalize(actualTree));
        }

        var diagnosticsPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagnosticsPath))
        {
            var actualDiagnostics = DumpDiagnostics(tree.Diagnostics.Where(d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal)).ToArray());
            var expectedDiagnostics = CorpusFile.Normalize(File.ReadAllText(diagnosticsPath));
            Assert.Equal(expectedDiagnostics, CorpusFile.Normalize(actualDiagnostics));
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var root = CorpusFile.GetRepoRoot();
        var corpusRoot = Path.Combine(root, "testdata");
        foreach (var dir in new[] { "m0-parse-valid", "m0-parse-invalid", "m1-enum-parse-valid", "m1-enum-parse-invalid", "m1-match-parse-valid", "m1-match-parse-invalid" })
        {
            var fullDir = Path.Combine(corpusRoot, dir);
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var sourcePath in Directory.EnumerateFiles(fullDir, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(p => Path.GetExtension(p) is ".cope" or ".ts")
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                yield return new object[] { sourcePath };
            }
        }
    }

    private static string DumpDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var diagnostic in diagnostics)
        {
            sb.Append(diagnostic.Id);
            sb.Append('|');
            sb.Append(diagnostic.Position);
            sb.Append('|');
            sb.Append(diagnostic.Length);
            sb.Append('|');
            sb.Append(diagnostic.Message);
            sb.AppendLine();
        }

        return sb.ToString();
    }

}
