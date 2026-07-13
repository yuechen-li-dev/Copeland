namespace Machina.Presentation.Screens;

/// <summary>
/// Stable identity for a presenter screen within a screen stack.
/// </summary>
public readonly record struct PresenterScreenId
{
    public PresenterScreenId(string value)
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
            throw new ArgumentException("Presenter screen identities must not be empty or whitespace.", nameof(value));
        }

        return normalized.ToLowerInvariant();
    }
}
