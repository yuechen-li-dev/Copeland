namespace Machina.Standard.Text;

public enum MachinaTextVariant
{
    Body,
    Label,
    Caption,
    Title,
    Mono,
}

public enum MachinaTextWrap
{
    Word,
    None,
}

public enum MachinaTextOverflow
{
    Clip,
    Ellipsis,
    Scroll,
}

public enum MachinaTextAlign
{
    Start,
    Center,
    End,
}

public enum MachinaTextVerticalAlign
{
    Top,
    Center,
    Bottom,
}

public enum MachinaTextLeadingKind
{
    Normal,
    Tight,
    Loose,
    Numeric,
}

public readonly record struct MachinaTextLeading
{
    private MachinaTextLeading(MachinaTextLeadingKind kind, double value)
    {
        Kind = kind;
        Value = value;
    }

    public MachinaTextLeadingKind Kind { get; }

    public double Value { get; }

    public static MachinaTextLeading Normal { get; } = new(MachinaTextLeadingKind.Normal, 0);

    public static MachinaTextLeading Tight { get; } = new(MachinaTextLeadingKind.Tight, 0);

    public static MachinaTextLeading Loose { get; } = new(MachinaTextLeadingKind.Loose, 0);

    public static MachinaTextLeading Numeric(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Numeric leading must be finite and greater than zero.");
        }

        return new MachinaTextLeading(MachinaTextLeadingKind.Numeric, value);
    }
}
