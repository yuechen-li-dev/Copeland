namespace Aurelian.Shaders.Lexing;

public readonly record struct SourceSpan(int Start, int Length, int Line, int Column);
