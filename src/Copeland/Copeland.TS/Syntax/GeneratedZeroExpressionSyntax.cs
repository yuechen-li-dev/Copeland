namespace Copeland.TS.Syntax;

/// <summary>Compiler-owned initialization of inactive, finite GPU value storage.</summary>
internal sealed record GeneratedZeroExpressionSyntax(TypeSyntax Type) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GeneratedZeroExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Type;
    }
}

internal sealed record GeneratedEnumTagExpressionSyntax(ExpressionSyntax Subject) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GeneratedEnumTagExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Subject;
    }
}
