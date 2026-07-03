namespace Machina.Core.Nodes;

public sealed record UiGridCell(
    int Row,
    int Column,
    UiNode Child);
