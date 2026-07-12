namespace Aurelian.Shaders.Diagnostics;

public sealed record Diagnostic(string Code, string Message, int Line, int Column)
{
    public static Diagnostic Create(string code, string message, int line = 1, int column = 1) => new(code, message, line, column);
}
