using System.Collections.Frozen;

namespace Copeland.Script.Syntax;

public static class SyntaxFacts
{
    private static readonly FrozenDictionary<string, SyntaxKind> KeywordKinds =
        new Dictionary<string, SyntaxKind>(StringComparer.Ordinal)
        {
            ["const"] = SyntaxKind.ConstKeyword,
            ["let"] = SyntaxKind.LetKeyword,
            ["function"] = SyntaxKind.FunctionKeyword,
            ["return"] = SyntaxKind.ReturnKeyword,
            ["if"] = SyntaxKind.IfKeyword,
            ["else"] = SyntaxKind.ElseKeyword,
            ["while"] = SyntaxKind.WhileKeyword,
            ["for"] = SyntaxKind.ForKeyword,
            ["true"] = SyntaxKind.TrueKeyword,
            ["false"] = SyntaxKind.FalseKeyword,
            ["null"] = SyntaxKind.NullKeyword,
            ["var"] = SyntaxKind.VarKeyword,
            ["with"] = SyntaxKind.WithKeyword,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static SyntaxKind GetKeywordKind(string text)
        => KeywordKinds.GetValueOrDefault(text, SyntaxKind.IdentifierToken);

    public static string? GetText(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.OpenParenToken => "(",
            SyntaxKind.CloseParenToken => ")",
            SyntaxKind.OpenBraceToken => "{",
            SyntaxKind.CloseBraceToken => "}",
            SyntaxKind.OpenBracketToken => "[",
            SyntaxKind.CloseBracketToken => "]",
            SyntaxKind.CommaToken => ",",
            SyntaxKind.DotToken => ".",
            SyntaxKind.ColonToken => ":",
            SyntaxKind.SemicolonToken => ";",
            SyntaxKind.PlusToken => "+",
            SyntaxKind.MinusToken => "-",
            SyntaxKind.StarToken => "*",
            SyntaxKind.SlashToken => "/",
            SyntaxKind.PercentToken => "%",
            SyntaxKind.BangToken => "!",
            SyntaxKind.EqualsToken => "=",
            SyntaxKind.LessToken => "<",
            SyntaxKind.LessOrEqualsToken => "<=",
            SyntaxKind.GreaterToken => ">",
            SyntaxKind.GreaterOrEqualsToken => ">=",
            SyntaxKind.EqualsEqualsToken => "==",
            SyntaxKind.BangEqualsToken => "!=",
            SyntaxKind.EqualsEqualsEqualsToken => "===",
            SyntaxKind.BangEqualsEqualsToken => "!==",
            SyntaxKind.AmpersandAmpersandToken => "&&",
            SyntaxKind.PipePipeToken => "||",
            SyntaxKind.ArrowToken => "=>",
            SyntaxKind.ConstKeyword => "const",
            SyntaxKind.LetKeyword => "let",
            SyntaxKind.FunctionKeyword => "function",
            SyntaxKind.ReturnKeyword => "return",
            SyntaxKind.IfKeyword => "if",
            SyntaxKind.ElseKeyword => "else",
            SyntaxKind.WhileKeyword => "while",
            SyntaxKind.ForKeyword => "for",
            SyntaxKind.TrueKeyword => "true",
            SyntaxKind.FalseKeyword => "false",
            SyntaxKind.NullKeyword => "null",
            SyntaxKind.VarKeyword => "var",
            SyntaxKind.WithKeyword => "with",
            _ => null,
        };
}
