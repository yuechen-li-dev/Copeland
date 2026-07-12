namespace Aurelian.Core.Presentation.Screens;

public readonly record struct ScreenLayerKey
{
    public ScreenLayerKey(string value)
    {
        Value = Normalize(value);
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Screen layer names must not be empty or whitespace.", nameof(value));
        }

        return normalized.ToLowerInvariant();
    }
}
