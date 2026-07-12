namespace Machina.Fonts;

public readonly record struct FontFaceId
{
    public FontFaceId(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Value = value.Trim();

        if (Value.Length == 0)
        {
            throw new ArgumentException("Font face id must not be empty.", nameof(value));
        }
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
