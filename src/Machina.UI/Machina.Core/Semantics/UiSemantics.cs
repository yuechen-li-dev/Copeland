namespace Machina.Core.Semantics;

public sealed record UiSemantics(
    UiRole Role,
    string? Label = null,
    bool Disabled = false,
    bool Focusable = false)
{
    public UiCursor Cursor { get; init; } = UiCursor.Auto;
    public bool? Checked { get; init; }
    public bool Indeterminate { get; init; }
    public bool? Selected { get; init; }
    public bool? Expanded { get; init; }
    public bool ReadOnly { get; init; }
    public bool Required { get; init; }
    public bool Invalid { get; init; }
    public bool Modal { get; init; }
    public string? Value { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public string? LabelledBy { get; init; }
    public string? DescribedBy { get; init; }
    public string? Controls { get; init; }
}
