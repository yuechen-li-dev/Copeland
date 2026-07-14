using System.Collections.Frozen;

namespace Copeland.TS.Syntax;

public static class SyntaxFacts
{
    private static readonly FrozenDictionary<string, SyntaxKind> KeywordKinds =
        new Dictionary<string, SyntaxKind>(StringComparer.Ordinal)
        {
            ["const"] = SyntaxKind.ConstKeyword,
            ["let"] = SyntaxKind.LetKeyword,
            ["function"] = SyntaxKind.FunctionKeyword,
            ["enum"] = SyntaxKind.EnumKeyword,
            ["record"] = SyntaxKind.RecordKeyword,
            ["match"] = SyntaxKind.MatchKeyword,
            ["return"] = SyntaxKind.ReturnKeyword,
            ["if"] = SyntaxKind.IfKeyword,
            ["else"] = SyntaxKind.ElseKeyword,
            ["while"] = SyntaxKind.WhileKeyword,
            ["for"] = SyntaxKind.ForKeyword,
            ["true"] = SyntaxKind.TrueKeyword,
            ["false"] = SyntaxKind.FalseKeyword,
            ["null"] = SyntaxKind.NullKeyword,
            ["number"] = SyntaxKind.NumberKeyword,
            ["string"] = SyntaxKind.StringKeyword,
            ["boolean"] = SyntaxKind.BooleanKeyword,
            ["void"] = SyntaxKind.VoidKeyword,
            ["var"] = SyntaxKind.VarKeyword,
            ["with"] = SyntaxKind.WithKeyword,
            ["try"] = SyntaxKind.TryKeyword,
            ["except"] = SyntaxKind.ExceptKeyword,
            ["break"] = SyntaxKind.BreakKeyword,
            ["continue"] = SyntaxKind.ContinueKeyword,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static SyntaxKind GetKeywordKind(string text)
        => KeywordKinds.GetValueOrDefault(text, SyntaxKind.IdentifierToken);

    public static int GetUnaryOperatorPrecedence(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.BangToken or SyntaxKind.MinusToken => 8,
            _ => 0,
        };

    public static int GetBinaryOperatorPrecedence(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken => 7,
            SyntaxKind.PlusToken or SyntaxKind.MinusToken => 6,
            SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken => 5,
            SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken => 4,
            SyntaxKind.AmpersandAmpersandToken => 3,
            SyntaxKind.PipePipeToken => 2,
            _ => 0,
        };

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
            SyntaxKind.QuestionToken => "?",
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
            SyntaxKind.EnumKeyword => "enum",
            SyntaxKind.RecordKeyword => "record",
            SyntaxKind.MatchKeyword => "match",
            SyntaxKind.ReturnKeyword => "return",
            SyntaxKind.IfKeyword => "if",
            SyntaxKind.ElseKeyword => "else",
            SyntaxKind.WhileKeyword => "while",
            SyntaxKind.ForKeyword => "for",
            SyntaxKind.TrueKeyword => "true",
            SyntaxKind.FalseKeyword => "false",
            SyntaxKind.NullKeyword => "null",
            SyntaxKind.NumberKeyword => "number",
            SyntaxKind.StringKeyword => "string",
            SyntaxKind.BooleanKeyword => "boolean",
            SyntaxKind.VoidKeyword => "void",
            SyntaxKind.VarKeyword => "var",
            SyntaxKind.WithKeyword => "with",
            SyntaxKind.TryKeyword => "try",
            SyntaxKind.ExceptKeyword => "except",
            SyntaxKind.BreakKeyword => "break",
            SyntaxKind.ContinueKeyword => "continue",
            _ => null,
        };
}
