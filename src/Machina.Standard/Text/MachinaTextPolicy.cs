namespace Machina.Standard.Text;

public sealed record MachinaTextPolicy
{
    public MachinaTextPolicy(
        MachinaTextVariant variant = MachinaTextVariant.Body,
        MachinaTextWrap wrap = MachinaTextWrap.Word,
        MachinaTextOverflow overflow = MachinaTextOverflow.Clip,
        MachinaTextAlign align = MachinaTextAlign.Start,
        MachinaTextLeading leading = default,
        double blockGap = 8,
        double listGap = 2,
        MachinaTextVerticalAlign verticalAlign = MachinaTextVerticalAlign.Top)
    {
        if (!double.IsFinite(blockGap) || blockGap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockGap), blockGap, "Block gap must be finite and non-negative.");
        }

        if (!double.IsFinite(listGap) || listGap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listGap), listGap, "List gap must be finite and non-negative.");
        }

        Variant = variant;
        Wrap = wrap;
        Overflow = overflow;
        Align = align;
        Leading = leading;
        BlockGap = blockGap;
        ListGap = listGap;
        VerticalAlign = verticalAlign;
    }

    public MachinaTextVariant Variant { get; }

    public MachinaTextWrap Wrap { get; }

    public MachinaTextOverflow Overflow { get; }

    public MachinaTextAlign Align { get; }

    public MachinaTextLeading Leading { get; }

    public double BlockGap { get; }

    public double ListGap { get; }

    public MachinaTextVerticalAlign VerticalAlign { get; }

    public static MachinaTextPolicy FromSpec(MachinaTextSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return new MachinaTextPolicy(
            spec.Variant,
            spec.Wrap,
            spec.Overflow,
            spec.Align,
            spec.Leading,
            spec.BlockGap,
            spec.ListGap,
            spec.VerticalAlign);
    }
}
