namespace Copeland.Script.Syntax;

public enum SyntaxKind
{
    // Special
    BadToken,
    EndOfFileToken,

    // Trivia
    WhitespaceTrivia,
    LineBreakTrivia,
    SingleLineCommentTrivia,
    MultiLineCommentTrivia,

    // Literals / identifiers
    IdentifierToken,
    NumberToken,
    StringToken,

    // Punctuation
    OpenParenToken,
    CloseParenToken,
    OpenBraceToken,
    CloseBraceToken,
    OpenBracketToken,
    CloseBracketToken,
    CommaToken,
    DotToken,
    ColonToken,
    SemicolonToken,

    // Operators
    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,
    PercentToken,
    BangToken,
    EqualsToken,
    LessToken,
    LessOrEqualsToken,
    GreaterToken,
    GreaterOrEqualsToken,
    EqualsEqualsToken,
    BangEqualsToken,
    EqualsEqualsEqualsToken,
    BangEqualsEqualsToken,
    AmpersandAmpersandToken,
    PipePipeToken,
    ArrowToken,

    // Keywords
    ConstKeyword,
    LetKeyword,
    FunctionKeyword,
    ReturnKeyword,
    IfKeyword,
    ElseKeyword,
    WhileKeyword,
    ForKeyword,
    TrueKeyword,
    FalseKeyword,
    NullKeyword,
    VarKeyword,
    WithKeyword,
}
