using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class ParserCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Parser_Corpus_Matches_Expected(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        var tree = SyntaxTree.Parse(source);

        var treePath = Path.ChangeExtension(sourcePath, ".tree.txt");
        if (File.Exists(treePath))
        {
            var actualTree = SyntaxTreeDumper.Dump(tree.Root);
            var expectedTree = Normalize(File.ReadAllText(treePath));
            Assert.Equal(expectedTree, Normalize(actualTree));
        }

        var diagnosticsPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagnosticsPath))
        {
            var actualDiagnostics = DumpDiagnostics(tree.Diagnostics.Where(d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal)).ToArray());
            var expectedDiagnostics = Normalize(File.ReadAllText(diagnosticsPath));
            Assert.Equal(expectedDiagnostics, Normalize(actualDiagnostics));
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var root = GetRepoRoot();
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

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
