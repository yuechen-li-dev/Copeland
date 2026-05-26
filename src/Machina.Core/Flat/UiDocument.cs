namespace Machina.Core.Flat;

public sealed record UiDocument(
    IReadOnlyList<UiRow> Rows)
{
    public static UiDocument Create(IReadOnlyList<UiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is null)
            {
                throw new ArgumentException($"Row at index {i} must not be null.", nameof(rows));
            }
        }

        return new UiDocument(rows);
    }
}
