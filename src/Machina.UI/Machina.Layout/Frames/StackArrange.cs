namespace Machina.Layout.Frames;

public enum StackAxis
{
    Horizontal,
    Vertical,
}

public enum StackJustify
{
    Start,
    Center,
    End,
    SpaceBetween,
}

public enum StackAlign
{
    Start,
    Center,
    End,
}

public sealed record StackArrange(
    StackAxis Axis,
    double Gap = 0,
    EdgeInsets? Padding = null,
    StackJustify Justify = StackJustify.Start,
    StackAlign Align = StackAlign.Start) : ArrangeSpec;
