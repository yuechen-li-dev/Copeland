using System.Collections.ObjectModel;

namespace Copeland.TS.Tson;

public enum TsonTypeKind
{
    Boolean,
    Number,
    String,
    Object,
    Record,
    Enum,
    Array,
    Table,
}

public sealed class TsonTypeReference
{
    private TsonTypeReference(TsonTypeKind kind, string? nominalName, TsonTypeReference? elementType = null)
    {
        Kind = kind;
        NominalName = nominalName;
        ElementType = elementType;
    }

    public static TsonTypeReference Boolean { get; } = new(TsonTypeKind.Boolean, null);

    public static TsonTypeReference Number { get; } = new(TsonTypeKind.Number, null);

    public static TsonTypeReference String { get; } = new(TsonTypeKind.String, null);

    public static TsonTypeReference Object { get; } = new(TsonTypeKind.Object, null);

    public TsonTypeKind Kind { get; }

    public string? NominalName { get; }

    public TsonTypeReference? ElementType { get; }

    public static TsonTypeReference Record(string name)
    {
        return Nominal(TsonTypeKind.Record, name);
    }

    public static TsonTypeReference Enum(string name)
    {
        return Nominal(TsonTypeKind.Enum, name);
    }

    public static TsonTypeReference Array(TsonTypeReference elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        return new TsonTypeReference(TsonTypeKind.Array, null, elementType);
    }

    private static TsonTypeReference Nominal(TsonTypeKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A nominal TSON type name cannot be empty.", nameof(name));
        }

        return new TsonTypeReference(kind, name);
    }
}

/// <summary>
/// Structural evidence for a TSON array's homogeneous element schema.
/// Arrays themselves have no nominal identity; nominal element references retain theirs.
/// </summary>
public sealed class TsonArraySchema
{
    public TsonArraySchema(TsonTypeReference elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        ElementType = elementType;
    }

    public TsonTypeReference ElementType { get; }
}

public sealed class TsonTableIdentity : IEquatable<TsonTableIdentity>
{
    private TsonTableIdentity(string schemaIdentity, string tableName)
    {
        TableName = tableName;
        Value = $"{schemaIdentity}#{tableName}";
    }

    public string Value { get; }

    public string TableName { get; }

    public static TsonTableIdentity Create(string schemaIdentity, string tableName)
    {
        if (string.IsNullOrWhiteSpace(schemaIdentity)
            || !schemaIdentity.StartsWith("copeland://", StringComparison.Ordinal)
            || schemaIdentity.Length == "copeland://".Length
            || schemaIdentity.Contains('#', StringComparison.Ordinal)
            || schemaIdentity.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A TSON table schema identity must be an absolute 'copeland://' identity without '#'.", nameof(schemaIdentity));
        }

        if (!IsIdentifier(tableName))
        {
            throw new ArgumentException("A TSON table name must be an identifier.", nameof(tableName));
        }

        return new TsonTableIdentity(schemaIdentity, tableName);
    }

    internal static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)
            || !(char.IsLetter(value[0]) || value[0] is '_' or '$'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
    }

    public bool Equals(TsonTableIdentity? other)
    {
        return other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is TsonTableIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }
}

public sealed class TsonTableColumnIdentity : IEquatable<TsonTableColumnIdentity>
{
    private TsonTableColumnIdentity(TsonTableIdentity tableIdentity, string columnName)
    {
        TableIdentity = tableIdentity;
        ColumnName = columnName;
        Value = $"{tableIdentity.Value}.{columnName}";
    }

    public string Value { get; }

    public TsonTableIdentity TableIdentity { get; }

    public string ColumnName { get; }

    public static TsonTableColumnIdentity Create(TsonTableIdentity tableIdentity, string columnName)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);
        if (!TsonTableIdentity.IsIdentifier(columnName))
        {
            throw new ArgumentException("A TSON table column name must be an identifier.", nameof(columnName));
        }

        return new TsonTableColumnIdentity(tableIdentity, columnName);
    }

    public bool Equals(TsonTableColumnIdentity? other)
    {
        return other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is TsonTableColumnIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }
}

public sealed class TsonTableColumnSchema
{
    public TsonTableColumnSchema(
        string name,
        TsonTableColumnIdentity identity,
        TsonTypeReference elementType)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A TSON table column name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(elementType);
        if (identity.ColumnName != name)
        {
            throw new ArgumentException("A TSON table column identity must derive from its authored name.", nameof(identity));
        }
        if (!IsSupportedCellType(elementType))
        {
            throw new ArgumentException("A TSON table column type must belong to the supported cell algebra.", nameof(elementType));
        }

        Name = name;
        Identity = identity;
        ElementType = elementType;
    }

    public string Name { get; }

    public TsonTableColumnIdentity Identity { get; }

    public TsonTypeReference ElementType { get; }

    private static bool IsSupportedCellType(TsonTypeReference type)
    {
        var current = type;
        var depth = 0;
        while (current.Kind == TsonTypeKind.Array)
        {
            depth++;
            if (depth > TsonLimits.Default.MaximumNestingDepth || current.ElementType is null)
            {
                return false;
            }

            current = current.ElementType;
        }

        return current.Kind is TsonTypeKind.Boolean
            or TsonTypeKind.Number
            or TsonTypeKind.String
            or TsonTypeKind.Record
            or TsonTypeKind.Enum;
    }
}

public sealed class TsonFieldDefinition
{
    public TsonFieldDefinition(string name, string identity, TsonTypeReference type)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A TSON field definition name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A TSON field definition identity cannot be empty.", nameof(identity));
        }

        ArgumentNullException.ThrowIfNull(type);
        Name = name;
        Identity = identity;
        Type = type;
    }

    public string Name { get; }

    public string Identity { get; }

    public TsonTypeReference Type { get; }
}

public abstract class TsonNominalDefinition
{
    private protected TsonNominalDefinition(string name, string identity)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A TSON nominal definition name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A TSON nominal definition identity cannot be empty.", nameof(identity));
        }

        Name = name;
        Identity = identity;
    }

    public string Name { get; }

    public string Identity { get; }
}

public sealed class TsonRecordDefinition : TsonNominalDefinition
{
    public TsonRecordDefinition(
        string name,
        string identity,
        IEnumerable<TsonFieldDefinition> fields)
        : base(name, identity)
    {
        Fields = CopyUnique(fields, "record field");
    }

    public IReadOnlyList<TsonFieldDefinition> Fields { get; }

    private static IReadOnlyList<TsonFieldDefinition> CopyUnique(
        IEnumerable<TsonFieldDefinition> fields,
        string description)
    {
        var copy = TsonCollection.Copy(fields, nameof(fields));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in copy)
        {
            if (!names.Add(field.Name))
            {
                throw new ArgumentException($"Duplicate {description} '{field.Name}'.", nameof(fields));
            }
        }

        return copy;
    }
}

public sealed class TsonEnumCaseDefinition
{
    public TsonEnumCaseDefinition(
        string name,
        string identity,
        IEnumerable<TsonFieldDefinition> payloads)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A TSON enum case name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A TSON enum case identity cannot be empty.", nameof(identity));
        }

        Name = name;
        Identity = identity;
        Payloads = TsonCollection.Copy(payloads, nameof(payloads));

        if (Payloads.Select(payload => payload.Name).Distinct(StringComparer.Ordinal).Count() != Payloads.Count)
        {
            throw new ArgumentException("A TSON enum case cannot contain duplicate payload names.", nameof(payloads));
        }
    }

    public string Name { get; }

    public string Identity { get; }

    public IReadOnlyList<TsonFieldDefinition> Payloads { get; }
}

public sealed class TsonEnumDefinition : TsonNominalDefinition
{
    public TsonEnumDefinition(
        string name,
        string identity,
        IEnumerable<TsonEnumCaseDefinition> cases)
        : base(name, identity)
    {
        Cases = TsonCollection.Copy(cases, nameof(cases));
        if (Cases.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
        {
            throw new ArgumentException("A TSON enum cannot contain duplicate case names.", nameof(cases));
        }
    }

    public IReadOnlyList<TsonEnumCaseDefinition> Cases { get; }
}

public sealed class TsonTableSchema : TsonNominalDefinition
{
    public TsonTableSchema(
        string name,
        TsonTableIdentity identity,
        IEnumerable<TsonTableColumnSchema> columns)
        : base(name, identity?.Value ?? throw new ArgumentNullException(nameof(identity)))
    {
        if (identity.TableName != name)
        {
            throw new ArgumentException("A TSON table identity must derive from its authored name.", nameof(identity));
        }

        IdentityValue = identity;
        Columns = TsonCollection.Copy(columns, nameof(columns));
        if (Columns.Count == 0)
        {
            throw new ArgumentException("A TSON table schema requires at least one typed column.", nameof(columns));
        }

        if (Columns.Count > TsonLimits.Default.MaximumTableColumnCount)
        {
            throw new ArgumentException("A TSON table schema exceeds the column limit.", nameof(columns));
        }

        if (Columns.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != Columns.Count)
        {
            throw new ArgumentException("A TSON table schema cannot contain duplicate column names.", nameof(columns));
        }

        if (Columns.Select(column => column.Identity.Value).Distinct(StringComparer.Ordinal).Count() != Columns.Count)
        {
            throw new ArgumentException("A TSON table schema cannot contain duplicate column identities.", nameof(columns));
        }
    }

    public TsonTableIdentity IdentityValue { get; }

    public IReadOnlyList<TsonTableColumnSchema> Columns { get; }
}

public sealed class TsonCatalog
{
    private readonly IReadOnlyDictionary<string, TsonNominalDefinition> _definitionsByName;

    public TsonCatalog(string schemaIdentity, IEnumerable<TsonNominalDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(schemaIdentity))
        {
            throw new ArgumentException("A TSON schema identity cannot be empty.", nameof(schemaIdentity));
        }

        SchemaIdentity = schemaIdentity;
        Definitions = TsonCollection.Copy(definitions, nameof(definitions));

        var byName = new Dictionary<string, TsonNominalDefinition>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            if (!byName.TryAdd(definition.Name, definition))
            {
                throw new ArgumentException(
                    $"Duplicate TSON nominal declaration '{definition.Name}'.",
                    nameof(definitions));
            }

            if (!identities.Add(definition.Identity))
            {
                throw new ArgumentException(
                    $"Duplicate TSON nominal identity '{definition.Identity}'.",
                    nameof(definitions));
            }
        }

        _definitionsByName = new ReadOnlyDictionary<string, TsonNominalDefinition>(byName);
    }

    public string SchemaIdentity { get; }

    public IReadOnlyList<TsonNominalDefinition> Definitions { get; }

    public bool TryGetDefinition(string name, out TsonNominalDefinition? definition)
    {
        return _definitionsByName.TryGetValue(name, out definition);
    }
}
