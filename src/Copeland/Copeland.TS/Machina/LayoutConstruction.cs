using Copeland.TS.Syntax;

namespace Copeland.TS.MachinaSource;

public sealed record LayoutKeywordValue(string Name);

/// <summary>Closed spatial input shared by source binding and template evaluation.</summary>
public sealed record LayoutValue(SyntaxToken Anchor, object? Scalar = null,
    string? Keyword = null, IReadOnlyDictionary<string, LayoutValue>? Fields = null);

public sealed record LayoutPropertyData(SyntaxToken Identifier, LayoutValue Value);

public sealed record LayoutNodeData(
    SyntaxToken KindToken,
    SyntaxToken Identifier,
    IReadOnlyList<LayoutPropertyData> Properties,
    IReadOnlyList<LayoutNodeData> Children,
    IReadOnlyList<BoundRelativeDerivationSpec>? RelativeDerivations = null);

public sealed record LayoutConstruction(
    SyntaxToken Identifier,
    string? Profile,
    LayoutValue X,
    LayoutValue Y,
    IReadOnlyList<LayoutPropertyData> Properties,
    IReadOnlyList<LayoutNodeData> Nodes,
    SyntaxToken? Contract = null);
