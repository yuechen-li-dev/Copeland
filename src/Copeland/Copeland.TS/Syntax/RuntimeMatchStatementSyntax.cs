namespace Copeland.TS.Syntax;

public sealed record RuntimeMatchStatementSyntax(
    SyntaxToken MatchKeyword,
    ExpressionSyntax Expression,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<StaticMatchArmSyntax> Arms,
    SyntaxToken CloseBraceToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.RuntimeMatchStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return MatchKeyword;
        yield return Expression;
        yield return OpenBraceToken;
        foreach (var arm in Arms)
        {
            yield return arm;
        }
        yield return CloseBraceToken;
    }
}
