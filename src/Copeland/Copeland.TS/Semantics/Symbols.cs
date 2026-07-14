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
    string? authoredReturnAliasName = null) : Symbol(name)
{
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public string? AuthoredReturnAliasName { get; } = authoredReturnAliasName;
    public bool IsFallible => ReturnType is ResultTypeSymbol;
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
