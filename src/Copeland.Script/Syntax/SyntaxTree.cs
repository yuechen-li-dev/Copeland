using Copeland.Script.Diagnostics;

namespace Copeland.Script.Syntax;

public sealed class SyntaxTree
{
    private SyntaxTree(string text, CompilationUnitSyntax root, IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Root = root;
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public string Text { get; }

    public CompilationUnitSyntax Root { get; }

    public IReadOnlyList<SyntaxToken> Tokens { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(string text)
    {
        var parser = new Parser(text);
        var root = parser.ParseCompilationUnit();
        var diagnostics = parser.Diagnostics.ToArray();
        var tokens = CollectTokens(root).ToArray();
        return new SyntaxTree(text, root, tokens, diagnostics);
    }

    public static SyntaxTree ParseTokens(string text)
    {
        var lexer = new Lexer(text);
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            var token = lexer.NextToken();
            tokens.Add(token);
            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        var root = new CompilationUnitSyntax([], tokens[^1]);
        return new SyntaxTree(text, root, tokens, lexer.Diagnostics.ToArray());
    }

    private static IEnumerable<SyntaxToken> CollectTokens(SyntaxNode node)
    {
        foreach (var child in node.GetChildren())
        {
            switch (child)
            {
                case SyntaxToken token:
                    yield return token;
                    break;
                case SyntaxNode childNode:
                    foreach (var descendant in CollectTokens(childNode))
                    {
                        yield return descendant;
                    }

                    break;
            }
        }
    }
}
