namespace Machina.Core.Nodes;

public enum UiStackItemKind
{
    Auto,
    Fixed,
    Fill,
}

public sealed record UiStackItem(
    UiNode Child,
    UiStackItemKind Kind,
    double MainSize = 0,
    double Weight = 1);
