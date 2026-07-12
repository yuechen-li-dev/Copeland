namespace Machina.Core.Nodes;

public enum Align
{
    Start,
    Center,
    End,
}

public sealed record ContainerNode(
    UiNode Child,
    Align AlignX = Align.Start,
    Align AlignY = Align.Start) : UiNode;
