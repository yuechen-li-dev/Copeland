using Copeland.TS.Diagnostics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Compiler;

/// <summary>
/// Narrow expression-only parse surface for compiler clients such as table query
/// frontends. It deliberately exposes neither module parsing nor parser state.
/// </summary>
public static class CopelandExpressionParser
{
    public static CopelandExpressionParseResult Parse(string text, string? sourceIdentity = null)
    {
        var parser = new Parser(text);
        ExpressionSyntax expression = parser.ParseStandaloneExpression();
        IReadOnlyList<Diagnostic> diagnostics = parser.Diagnostics
            .Select(diagnostic => diagnostic with { SourcePath = sourceIdentity })
            .ToArray();
        return new CopelandExpressionParseResult(expression, diagnostics, sourceIdentity);
    }
}

public sealed class CopelandExpressionParseResult(
    ExpressionSyntax expression,
    IReadOnlyList<Diagnostic> diagnostics,
    string? sourceIdentity)
{
    public ExpressionSyntax Expression { get; } = expression;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public string? SourceIdentity { get; } = sourceIdentity;
    public bool Success => Diagnostics.Count == 0;
}
