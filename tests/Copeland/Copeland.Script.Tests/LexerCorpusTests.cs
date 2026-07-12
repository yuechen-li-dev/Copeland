using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Syntax;
using Copeland.Script.Tests.Corpus;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class LexerCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Lexer_Corpus_Matches_Expected(string sourcePath)
    {
        var source = CorpusFile.ReadSourceText(sourcePath);
        var tree = SyntaxTree.ParseTokens(source);

        var tokenPath = Path.ChangeExtension(sourcePath, ".tokens.txt");
        if (File.Exists(tokenPath))
        {
            var actualTokens = DumpTokens(tree.Tokens);
            var expectedTokens = CorpusFile.Normalize(File.ReadAllText(tokenPath));
            Assert.Equal(expectedTokens, CorpusFile.Normalize(actualTokens));
        }

        var diagnosticsPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagnosticsPath))
        {
            var actualDiagnostics = DumpDiagnostics(tree.Diagnostics);
            var expectedDiagnostics = CorpusFile.Normalize(File.ReadAllText(diagnosticsPath));
            Assert.Equal(expectedDiagnostics, CorpusFile.Normalize(actualDiagnostics));
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var root = CorpusFile.GetRepoRoot();
        var corpusRoot = Path.Combine(root, "testdata");
        foreach (var dir in new[] { "m0-lex-valid", "m0-lex-invalid" })
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

    private static string DumpTokens(IReadOnlyList<SyntaxToken> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            sb.Append(token.Kind);
            sb.Append('|');
            sb.Append(Escape(token.Text));
            sb.Append('|');
            sb.Append(token.Value is null ? "null" : Escape(token.Value.ToString()!));
            sb.AppendLine();
        }

        return sb.ToString();
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

    private static string Escape(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

}
