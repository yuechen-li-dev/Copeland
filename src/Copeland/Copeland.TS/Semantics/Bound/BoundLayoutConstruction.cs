using Copeland.TS.MachinaSource;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics.Bound;

public sealed record BoundLayoutPropertyPlan(SyntaxToken Identifier, BoundTemplateValue Value);

public sealed record BoundLayoutNodePlan(
    SyntaxToken KindToken,
    SyntaxToken Identifier,
    IReadOnlyList<BoundLayoutPropertyPlan> Properties,
    IReadOnlyList<BoundLayoutNodePlan> Children,
    BoundTemplateValue? Condition = null,
    IReadOnlyList<BoundLayoutNodePlan>? ElseChildren = null);

/// <summary>Typed spatial construction; the evaluator never receives layout syntax.</summary>
public sealed class BoundTemplateLayoutConstruction(
    SyntaxToken anchor,
    string? profile,
    BoundTemplateValue x,
    BoundTemplateValue y,
    IReadOnlyList<BoundLayoutPropertyPlan> properties,
    IReadOnlyList<BoundLayoutNodePlan> nodes,
    BoundLayoutTypeDeclaration? contract,
    string sourcePath) : BoundTemplateValue(anchor, LayoutValueTypeSymbol.Instance)
{
    public string? Profile { get; } = profile;
    public BoundTemplateValue X { get; } = x;
    public BoundTemplateValue Y { get; } = y;
    public IReadOnlyList<BoundLayoutPropertyPlan> Properties { get; } = properties;
    public IReadOnlyList<BoundLayoutNodePlan> Nodes { get; } = nodes;
    public BoundLayoutTypeDeclaration? Contract { get; } = contract;
    public string SourcePath { get; } = sourcePath;
}

public sealed class BoundTemplateDefaultArgument(BoundTemplateValue value) : BoundTemplateValue(value.Anchor, value.Type)
{
    public BoundTemplateValue Value { get; } = value;
}
