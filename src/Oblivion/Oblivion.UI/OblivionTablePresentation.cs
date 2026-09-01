using System.Globalization;
using Copeland.TS.Tson;

namespace Oblivion.Product;

public sealed record OblivionTablePresentationSource(
    TsonTable Table,
    string SourceReference,
    string Profile,
    string SourceHash,
    long LoadMilliseconds,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public static class OblivionTableProjection
{
    public static TsonValue Cell(TsonTable table, int rowIndex, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        if (rowIndex >= table.RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        if (columnIndex >= table.Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        return table.Columns[columnIndex].Cells[rowIndex];
    }
}

public static class OblivionTableLayoutPolicy
{
    public const int ColumnWidthSampleSize = 32;
    public const double RowIndexWidth = 56;
    public const double MinimumColumnWidth = 180;
    public const double MaximumColumnWidth = 320;
    public const double RowHeight = 34;
    public const double HeaderHeight = 54;

    public static double PreferredTableWidth(TsonTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return RowIndexWidth + Enumerable.Range(0, table.Columns.Count)
            .Sum(columnIndex => PreferredColumnWidth(table, columnIndex));
    }

    public static double PreferredColumnWidth(TsonTable table, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        var column = table.Columns[columnIndex];
        int characters = Math.Max(
            column.Schema.Name.Length,
            OblivionTableCellDisplayFormatter.FormatType(column.Schema.ElementType).Length);
        for (int rowIndex = 0; rowIndex < Math.Min(table.RowCount, ColumnWidthSampleSize); rowIndex++)
        {
            characters = Math.Max(
                characters,
                OblivionTableCellDisplayFormatter.Format(column.Cells[rowIndex]).Length);
        }

        return Math.Clamp(28 + (characters * 7.2), MinimumColumnWidth, MaximumColumnWidth);
    }
}

public static class OblivionTableCellDisplayFormatter
{
    public const int VisibleArrayItemLimit = 3;
    public const int MaximumDisplayLength = 160;

    public static string Format(TsonValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string display = FormatCore(value);
        return display.Length <= MaximumDisplayLength
            ? display
            : display[..(MaximumDisplayLength - 1)] + "…";
    }

    public static string FormatType(TsonTypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Kind switch
        {
            TsonTypeKind.Boolean => "boolean",
            TsonTypeKind.Number => "number",
            TsonTypeKind.String => "string",
            TsonTypeKind.Record => type.NominalName ?? "record",
            TsonTypeKind.Enum => type.NominalName ?? "enum",
            TsonTypeKind.Array when type.ElementType is not null => FormatType(type.ElementType) + "[]",
            _ => type.Kind.ToString().ToLowerInvariant(),
        };
    }

    private static string FormatCore(TsonValue value)
    {
        return value switch
        {
            TsonBoolean boolean => boolean.Value ? "true" : "false",
            TsonNumber number => FormatNumber(number),
            TsonString text => FormatString(text.Value),
            TsonRecord record => "{" + string.Join(", ", record.Fields.Select(FormatField)) + "}",
            TsonEnum @enum => FormatEnum(@enum),
            TsonArray array => FormatArray(array),
            _ => value.GetType().Name,
        };
    }

    private static string FormatNumber(TsonNumber number)
    {
        if (number.IsNegativeZero)
        {
            return "-0";
        }

        if (number.IsNaN)
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(number.Value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(number.Value))
        {
            return "-Infinity";
        }

        return number.Value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string FormatField(TsonField field)
    {
        return $"{field.Name}: {FormatCore(field.Value)}";
    }

    private static string FormatEnum(TsonEnum value)
    {
        if (value.Payloads.Count == 0)
        {
            return value.CaseName;
        }

        string payload = value.Payloads.Count == 1
            ? FormatCore(value.Payloads[0].Value)
            : string.Join(", ", value.Payloads.Select(FormatField));
        return $"{value.CaseName}({payload})";
    }

    private static string FormatArray(TsonArray array)
    {
        IEnumerable<string> items = array.Elements
            .Take(VisibleArrayItemLimit)
            .Select(FormatCore);
        string suffix = array.Elements.Count > VisibleArrayItemLimit ? ", …" : string.Empty;
        return "[" + string.Join(", ", items) + suffix + "]";
    }
}
