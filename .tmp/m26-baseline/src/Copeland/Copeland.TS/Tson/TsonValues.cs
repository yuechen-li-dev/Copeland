using System.Collections.ObjectModel;

namespace Copeland.TS.Tson;

public abstract class TsonValue
{
    private protected TsonValue()
    {
    }
}

public sealed class TsonBoolean : TsonValue
{
    private TsonBoolean(bool value)
    {
        Value = value;
    }

    public static TsonBoolean False { get; } = new(false);

    public static TsonBoolean True { get; } = new(true);

    public bool Value { get; }

    public static TsonBoolean FromBoolean(bool value)
    {
        return value ? True : False;
    }
}

public sealed class TsonNumber : TsonValue
{
    private const ulong CanonicalNaNBits = 0x7FF8000000000000;

    private TsonNumber(ulong bits)
    {
        Bits = NormalizeNaN(bits);
    }

    public ulong Bits { get; }

    public double Value => BitConverter.UInt64BitsToDouble(Bits);

    public bool IsNegativeZero => Bits == 0x8000000000000000;

    public bool IsNaN => double.IsNaN(Value);

    public static TsonNumber FromDouble(double value)
    {
        return FromBits(BitConverter.DoubleToUInt64Bits(value));
    }

    public static TsonNumber FromBits(ulong bits)
    {
        return new TsonNumber(bits);
    }

    private static ulong NormalizeNaN(ulong bits)
    {
        var exponent = bits & 0x7FF0000000000000;
        var fraction = bits & 0x000FFFFFFFFFFFFF;
        return exponent == 0x7FF0000000000000 && fraction != 0
            ? CanonicalNaNBits
            : bits;
    }
}

public sealed class TsonString : TsonValue
{
    public TsonString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(value[index])
                || index + 1 >= value.Length
                || !char.IsLowSurrogate(value[index + 1]))
            {
                throw new ArgumentException(
                    "A TSON string cannot contain an isolated UTF-16 surrogate.",
                    nameof(value));
            }

            index++;
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed class TsonArray : TsonValue
{
    public TsonArray(TsonArraySchema schema, IEnumerable<TsonValue> elements)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Schema = schema;
        Elements = TsonCollection.Copy(elements, nameof(elements));
        for (var index = 0; index < Elements.Count; index++)
        {
            if (!MatchesSchemaFamily(Elements[index], Schema.ElementType))
            {
                throw new ArgumentException(
                    $"TSON array element {index} does not match its schema family.",
                    nameof(elements));
            }
        }
    }

    public TsonArraySchema Schema { get; }

    public IReadOnlyList<TsonValue> Elements { get; }

    private static bool MatchesSchemaFamily(TsonValue value, TsonTypeReference type)
    {
        return type.Kind switch
        {
            TsonTypeKind.Boolean => value is TsonBoolean,
            TsonTypeKind.Number => value is TsonNumber,
            TsonTypeKind.String => value is TsonString,
            TsonTypeKind.Record => value is TsonRecord,
            TsonTypeKind.Enum => value is TsonEnum,
            TsonTypeKind.Array => value is TsonArray array
                && type.ElementType is not null
                && SameType(array.Schema.ElementType, type.ElementType),
            _ => false,
        };
    }

    private static bool SameType(TsonTypeReference left, TsonTypeReference right)
    {
        var pending = new Stack<(TsonTypeReference Left, TsonTypeReference Right)>();
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            var pair = pending.Pop();
            if (pair.Left.Kind != pair.Right.Kind
                || !string.Equals(pair.Left.NominalName, pair.Right.NominalName, StringComparison.Ordinal))
            {
                return false;
            }

            if (pair.Left.Kind == TsonTypeKind.Array)
            {
                if (pair.Left.ElementType is null || pair.Right.ElementType is null)
                {
                    return false;
                }

                pending.Push((pair.Left.ElementType, pair.Right.ElementType));
            }
        }

        return true;
    }
}

public sealed class TsonTableColumn
{
    public TsonTableColumn(TsonTableColumnSchema schema, IEnumerable<TsonValue> cells)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Schema = schema;
        Cells = TsonCollection.Copy(cells, nameof(cells));
        if (Cells.Count > TsonLimits.Default.MaximumTableRowCount)
        {
            throw new ArgumentException("A TSON table column exceeds the row limit.", nameof(cells));
        }

        for (var index = 0; index < Cells.Count; index++)
        {
            if (!TsonValueSchema.Matches(Cells[index], Schema.ElementType))
            {
                throw new ArgumentException(
                    $"TSON table cell {index} does not match column '{Schema.Name}'.",
                    nameof(cells));
            }
        }
    }

    public TsonTableColumnSchema Schema { get; }

    public IReadOnlyList<TsonValue> Cells { get; }
}

public sealed class TsonTable : TsonValue
{
    public TsonTable(
        TsonTableSchema schema,
        IEnumerable<TsonTableColumn> columns,
        TsonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Schema = schema;
        Columns = TsonCollection.Copy(columns, nameof(columns));
        if (Columns.Count != Schema.Columns.Count)
        {
            throw new ArgumentException("TSON table columns do not match the table schema.", nameof(columns));
        }

        var rowCount = Columns.Count == 0 ? 0 : Columns[0].Cells.Count;
        for (var index = 0; index < Columns.Count; index++)
        {
            if (!ReferenceEquals(Columns[index].Schema, Schema.Columns[index])
                && (!Columns[index].Schema.Identity.Equals(Schema.Columns[index].Identity)
                    || Columns[index].Schema.Name != Schema.Columns[index].Name
                    || !TsonValueSchema.SameType(
                        Columns[index].Schema.ElementType,
                        Schema.Columns[index].ElementType)))
            {
                throw new ArgumentException("TSON table columns do not match schema order and identity.", nameof(columns));
            }

            if (Columns[index].Cells.Count != rowCount)
            {
                throw new ArgumentException("TSON table columns must be rectangular.", nameof(columns));
            }
        }

        RowCount = rowCount;
        TsonTableValidation.Validate(this, limits ?? TsonLimits.Default, nameof(columns));
    }

    public TsonTableSchema Schema { get; }

    public IReadOnlyList<TsonTableColumn> Columns { get; }

    public int RowCount { get; }
}

internal static class TsonTableValidation
{
    public static void Validate(TsonTable table, TsonLimits limits, string parameterName)
    {
        long totalCells = (long)table.Columns.Count * table.RowCount;
        if (totalCells > limits.MaximumTableCellCount)
        {
            throw new ArgumentException("A TSON table exceeds the total-cell limit.", parameterName);
        }

        var nodeCount = 1 + table.Columns.Count;
        var containers = new HashSet<TsonValue>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(TsonValue Value, int Depth)>();
        for (var columnIndex = table.Columns.Count - 1; columnIndex >= 0; columnIndex--)
        {
            var cells = table.Columns[columnIndex].Cells;
            for (var cellIndex = cells.Count - 1; cellIndex >= 0; cellIndex--)
            {
                pending.Push((cells[cellIndex], 2));
            }
        }

        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item.Depth > limits.MaximumNestingDepth)
            {
                throw new ArgumentException("A TSON table exceeds the nested-value depth limit.", parameterName);
            }

            nodeCount++;
            if (nodeCount > limits.MaximumValueNodeCount)
            {
                throw new ArgumentException("A TSON table exceeds the value-node limit.", parameterName);
            }

            switch (item.Value)
            {
                case TsonString text when text.Value.Length > limits.MaximumStringLength:
                    throw new ArgumentException("A TSON table string exceeds the string limit.", parameterName);
                case TsonArray array:
                    AddContainer(containers, array, parameterName);
                    if (array.Elements.Count > limits.MaximumArrayLength)
                    {
                        throw new ArgumentException("A TSON table array exceeds the array-length limit.", parameterName);
                    }

                    PushValues(pending, array.Elements, item.Depth + 1);
                    break;
                case TsonRecord record:
                    AddContainer(containers, record, parameterName);
                    PushFields(pending, record.Fields, item.Depth + 1);
                    break;
                case TsonEnum @enum:
                    AddContainer(containers, @enum, parameterName);
                    PushFields(pending, @enum.Payloads, item.Depth + 1);
                    break;
                case TsonObject or TsonTable:
                    throw new ArgumentException("A TSON table contains an unsupported nested value.", parameterName);
            }
        }
    }

    private static void AddContainer(
        HashSet<TsonValue> containers,
        TsonValue value,
        string parameterName)
    {
        if (!containers.Add(value))
        {
            throw new ArgumentException("A TSON table cannot contain aliased container values.", parameterName);
        }
    }

    private static void PushValues(
        Stack<(TsonValue Value, int Depth)> pending,
        IReadOnlyList<TsonValue> values,
        int depth)
    {
        for (var index = values.Count - 1; index >= 0; index--)
        {
            pending.Push((values[index], depth));
        }
    }

    private static void PushFields(
        Stack<(TsonValue Value, int Depth)> pending,
        IReadOnlyList<TsonField> fields,
        int depth)
    {
        for (var index = fields.Count - 1; index >= 0; index--)
        {
            pending.Push((fields[index].Value, depth));
        }
    }
}

internal static class TsonValueSchema
{
    public static bool Matches(TsonValue value, TsonTypeReference type)
    {
        return type.Kind switch
        {
            TsonTypeKind.Boolean => value is TsonBoolean,
            TsonTypeKind.Number => value is TsonNumber,
            TsonTypeKind.String => value is TsonString,
            TsonTypeKind.Record => value is TsonRecord,
            TsonTypeKind.Enum => value is TsonEnum,
            TsonTypeKind.Array => value is TsonArray array
                && type.ElementType is not null
                && SameType(array.Schema.ElementType, type.ElementType),
            _ => false,
        };
    }

    internal static bool SameType(TsonTypeReference left, TsonTypeReference right)
    {
        var pending = new Stack<(TsonTypeReference Left, TsonTypeReference Right)>();
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            var pair = pending.Pop();
            if (pair.Left.Kind != pair.Right.Kind
                || !string.Equals(pair.Left.NominalName, pair.Right.NominalName, StringComparison.Ordinal))
            {
                return false;
            }

            if (pair.Left.Kind == TsonTypeKind.Array)
            {
                if (pair.Left.ElementType is null || pair.Right.ElementType is null)
                {
                    return false;
                }

                pending.Push((pair.Left.ElementType, pair.Right.ElementType));
            }
        }

        return true;
    }
}

public sealed class TsonField
{
    public TsonField(string name, TsonValue value, string? identity = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A TSON field name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
        Identity = identity;
    }

    public string Name { get; }

    public string? Identity { get; }

    public TsonValue Value { get; }
}

public sealed class TsonObject : TsonValue
{
    public TsonObject(IEnumerable<TsonField> fields)
    {
        Fields = TsonCollection.CopyUniqueFields(fields, requireIdentity: false);
    }

    public IReadOnlyList<TsonField> Fields { get; }
}

public sealed class TsonRecord : TsonValue
{
    public TsonRecord(string identity, IEnumerable<TsonField> fields)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A TSON record identity cannot be empty.", nameof(identity));
        }

        Identity = identity;
        Fields = TsonCollection.CopyUniqueFields(fields, requireIdentity: true);
    }

    public string Identity { get; }

    public IReadOnlyList<TsonField> Fields { get; }
}

public sealed class TsonEnum : TsonValue
{
    public TsonEnum(
        string enumIdentity,
        string caseIdentity,
        string caseName,
        IEnumerable<TsonField> payloads)
    {
        if (string.IsNullOrWhiteSpace(enumIdentity))
        {
            throw new ArgumentException("A TSON enum identity cannot be empty.", nameof(enumIdentity));
        }

        if (string.IsNullOrWhiteSpace(caseIdentity))
        {
            throw new ArgumentException("A TSON enum case identity cannot be empty.", nameof(caseIdentity));
        }

        if (string.IsNullOrEmpty(caseName))
        {
            throw new ArgumentException("A TSON enum case name cannot be empty.", nameof(caseName));
        }

        EnumIdentity = enumIdentity;
        CaseIdentity = caseIdentity;
        CaseName = caseName;
        Payloads = TsonCollection.CopyUniqueFields(payloads, requireIdentity: true);
    }

    public string EnumIdentity { get; }

    public string CaseIdentity { get; }

    public string CaseName { get; }

    public IReadOnlyList<TsonField> Payloads { get; }
}

internal static class TsonCollection
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException("TSON collections cannot contain null elements.", parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    public static IReadOnlyList<TsonField> CopyUniqueFields(
        IEnumerable<TsonField> fields,
        bool requireIdentity)
    {
        var copy = Copy(fields, nameof(fields));
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in copy)
        {
            if (!names.Add(field.Name))
            {
                throw new ArgumentException($"Duplicate TSON field '{field.Name}'.", nameof(fields));
            }

            if (requireIdentity && string.IsNullOrWhiteSpace(field.Identity))
            {
                throw new ArgumentException(
                    $"Nominal TSON field '{field.Name}' requires a stable identity.",
                    nameof(fields));
            }

            if (!requireIdentity && field.Identity is not null)
            {
                throw new ArgumentException(
                    $"Structural TSON field '{field.Name}' cannot carry a nominal identity.",
                    nameof(fields));
            }
        }

        return copy;
    }
}
