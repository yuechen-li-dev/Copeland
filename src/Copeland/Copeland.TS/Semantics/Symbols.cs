namespace Copeland.TS.Semantics;

public abstract class Symbol(string name)
{
    public string Name { get; } = name;
}

public sealed class VariableSymbol(
    string name,
    TypeSymbol type,
    bool isReadOnly,
    string? authoredAliasName = null) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
    public bool IsReadOnly { get; } = isReadOnly;
    public string? AuthoredAliasName { get; } = authoredAliasName;
}

public sealed class ParameterSymbol(string name, TypeSymbol type, string? authoredAliasName = null) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
    public string? AuthoredAliasName { get; } = authoredAliasName;
}

public sealed class FunctionSymbol(
    string name,
    IReadOnlyList<ParameterSymbol> parameters,
    TypeSymbol returnType,
    string? authoredReturnAliasName = null,
    string? stableIdentity = null) : Symbol(name)
{
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public string? AuthoredReturnAliasName { get; } = authoredReturnAliasName;
    public string StableIdentity { get; } = stableIdentity ?? name;
    public bool IsFallible => ReturnType is ResultTypeSymbol;
    public IReadOnlyList<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    public bool IsGeneric => TypeParameters.Count > 0;
}

public sealed class RequirementFieldSymbol(string name, TypeSymbol type, int ordinal) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
    public int Ordinal { get; } = ordinal;
}

public sealed class InterfaceSymbol(string name, int declarationIdentity) : Symbol(name)
{
    private readonly List<RequirementFieldSymbol> _fields = [];
    public int DeclarationIdentity { get; } = declarationIdentity;
    public IReadOnlyList<RequirementFieldSymbol> Fields => _fields;
    public void AddField(RequirementFieldSymbol field) => _fields.Add(field);
}

public sealed class RequirementSet(IReadOnlyList<InterfaceSymbol> interfaces, IReadOnlyList<RequirementFieldSymbol> fields)
{
    public IReadOnlyList<InterfaceSymbol> Interfaces { get; } = interfaces;
    public IReadOnlyList<RequirementFieldSymbol> Fields { get; } = fields;
}

public sealed class TypeParameterSymbol(string name, TypeParameterTypeSymbol type, RequirementSet requirements) : Symbol(name)
{
    public TypeParameterTypeSymbol Type { get; } = type;
    public RequirementSet Requirements { get; } = requirements;
}

public sealed class TypeAliasSymbol(string name) : Symbol(name)
{
    public TypeSymbol CanonicalType { get; internal set; } = PrimitiveTypeSymbol.Error;
    public bool IsResolved { get; internal set; }
}

public sealed class EnumCaseSymbol(string name, EnumTypeSymbol enumType, IReadOnlyList<EnumPayloadFieldSymbol> payloadFields) : Symbol(name)
{
    public EnumTypeSymbol EnumType { get; } = enumType;
    public IReadOnlyList<EnumPayloadFieldSymbol> PayloadFields { get; } = payloadFields;
    public bool HasPayload => PayloadFields.Count > 0;
}

public sealed class EnumPayloadFieldSymbol(string name, TypeSymbol type) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
}

public sealed class RecordFieldSymbol(string name, RecordFieldId id, TypeSymbol type) : Symbol(name)
{
    public RecordFieldId Id { get; } = id;
    public TypeSymbol Type { get; } = type;
}

public sealed class TableColumnSymbol(string name, TableColumnId id, TypeSymbol type) : Symbol(name)
{
    public TableColumnId Id { get; } = id;
    public TypeSymbol Type { get; } = type;
}

public sealed class TableRowFieldSymbol(string name, TableRowFieldId id, TypeSymbol type) : Symbol(name)
{
    public TableRowFieldId Id { get; } = id;
    public TypeSymbol Type { get; } = type;
}
