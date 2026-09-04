namespace Machina.Core.Assets;

/// <summary>
/// Content-bearing semantic identity for a compiled monochrome vector icon.
/// </summary>
public readonly record struct MachinaVectorIconId
{
    public MachinaVectorIconId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Vector icon identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
