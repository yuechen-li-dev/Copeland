namespace Copeland.TS.Syntax;

public sealed record FlowTapeState(
    int Position,
    string State,
    string Fragment,
    string Path,
    string? ParentPath,
    SyntaxToken AuthoredState,
    SyntaxToken? CompositionSite);

public sealed record FlowTapePlan(string FrameType, string StateType, IReadOnlyList<FlowTapeState> States);
