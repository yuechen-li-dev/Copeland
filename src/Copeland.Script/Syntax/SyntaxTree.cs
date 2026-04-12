using Copeland.Script.Diagnostics;

namespace Copeland.Script.Syntax;

public sealed class SyntaxTree
{
    private SyntaxTree(string text, IReadOnlyList<SyntaxToken> tokens, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public string Text { get; }

    public IReadOnlyList<SyntaxToken> Tokens { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

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

        return new SyntaxTree(text, tokens, lexer.Diagnostics.ToArray());
    }
}
