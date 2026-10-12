namespace Copeland.TS.Syntax;

public sealed record LayoutConditionalSyntax(
    SyntaxToken StaticKeyword,
    ExpressionSyntax Condition,
    IReadOnlyList<LayoutNodeSyntax> ThenNodes,
    IReadOnlyList<LayoutNodeSyntax> ElseNodes) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.LayoutNode;

    public override IEnumerable<object> GetChildren()
    {
        yield return StaticKeyword;
        yield return Condition;
        foreach (LayoutNodeSyntax node in ThenNodes) yield return node;
        foreach (LayoutNodeSyntax node in ElseNodes) yield return node;
    }
}
