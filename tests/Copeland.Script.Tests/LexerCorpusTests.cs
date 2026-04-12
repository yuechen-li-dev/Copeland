using System.Text;
using Copeland.Script.Diagnostics;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class LexerCorpusTests
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public void Lexer_Corpus_Matches_Expected(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        var tree = SyntaxTree.ParseTokens(source);

        var tokenPath = Path.ChangeExtension(sourcePath, ".tokens.txt");
        if (File.Exists(tokenPath))
        {
            var actualTokens = DumpTokens(tree.Tokens);
            var expectedTokens = Normalize(File.ReadAllText(tokenPath));
            Assert.Equal(expectedTokens, Normalize(actualTokens));
        }

        var diagnosticsPath = Path.ChangeExtension(sourcePath, ".diagnostics.txt");
        if (File.Exists(diagnosticsPath))
        {
            var actualDiagnostics = DumpDiagnostics(tree.Diagnostics);
            var expectedDiagnostics = Normalize(File.ReadAllText(diagnosticsPath));
            Assert.Equal(expectedDiagnostics, Normalize(actualDiagnostics));
        }
    }

    public static IEnumerable<object[]> GetCases()
    {
        var root = GetRepoRoot();
        var corpusRoot = Path.Combine(root, "testdata");
        foreach (var dir in new[] { "m0-lex-valid", "m0-lex-invalid" })
        {
            var fullDir = Path.Combine(corpusRoot, dir);
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var sourcePath in Directory.EnumerateFiles(fullDir, "*.cope", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
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
