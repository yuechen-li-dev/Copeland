namespace Aurelian.Shaders.Lexing;

public enum TokenKind
{
    Identifier,
    Keyword,
    NumericLiteral,
    StringLiteral,
    Punctuation,
    Operator,
    Comment,
    PreprocessorDirective,
    EndOfFile,
}
