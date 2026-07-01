namespace Aurelian.Shaders.Lexing;

public readonly record struct Token(TokenKind Kind, string Text, SourceSpan Span);
