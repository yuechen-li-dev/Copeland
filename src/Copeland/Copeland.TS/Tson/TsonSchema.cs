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
