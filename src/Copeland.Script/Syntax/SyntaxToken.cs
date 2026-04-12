namespace Copeland.Script.Syntax;

public sealed record SyntaxToken(
    SyntaxKind Kind,
    int Position,
    string Text,
    object? Value);
