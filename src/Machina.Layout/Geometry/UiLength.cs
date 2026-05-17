namespace Machina.Layout.Geometry;

public enum UiLengthUnit
{
    Px,
    Ui,
}

public readonly record struct UiLength
{
    public UiLengthUnit Unit { get; }
    public double Value { get; }

    private UiLength(UiLengthUnit unit, double value)
    {
        Unit = unit;
        Value = value;
    }

    public static UiLength Px(double value) => new(UiLengthUnit.Px, value);

    public static UiLength Ui(double value) => new(UiLengthUnit.Ui, value);

    public static implicit operator UiLength(double value) => Px(value);
}
