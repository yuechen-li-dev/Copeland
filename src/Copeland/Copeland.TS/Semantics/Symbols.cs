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

public sealed class ParameterSymbol(string name, TypeSymbol type, string? authoredAliasName = null, bool isCaptured = false) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
    public string? AuthoredAliasName { get; } = authoredAliasName;
    public bool IsCaptured { get; } = isCaptured;
}

public sealed class FunctionSymbol(
    string name,
    IReadOnlyList<ParameterSymbol> parameters,
    TypeSymbol returnType,
    string? authoredReturnAliasName = null,
    string? stableIdentity = null,
    bool isAsync = false,
    bool isGenerator = false) : Symbol(name)
{
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public string? AuthoredReturnAliasName { get; } = authoredReturnAliasName;
    public string StableIdentity { get; } = stableIdentity ?? name;
    /// <summary>Backend-neutral callable spelling selected after project binding. It never changes source lookup identity.</summary>
    public string EmissionName { get; internal set; } = name;
    public bool IsFallible => ReturnType is ResultTypeSymbol;
    public bool IsAsync { get; } = isAsync;
    public bool IsGenerator { get; } = isGenerator;
    public IReadOnlyList<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    public bool IsGeneric => TypeParameters.Count > 0;
    public ClassTypeSymbol? ClassOwner { get; internal set; }
    public string? MemberName { get; internal set; }
    public bool IsClassConstructor { get; internal set; }
    public bool IsPublic { get; internal set; } = true;
    public CallableTypeSymbol CallableType => new(Parameters.Select(parameter => new CallableParameterTypeSymbol(parameter.Name, parameter.Type)).ToArray(), InvocationReturnType);
    public TypeSymbol InvocationReturnType => IsAsync ? new AsyncTypeSymbol(ReturnType) : ReturnType;
}

public sealed class NpmFunctionSymbol : Symbol
{
    public NpmFunctionSymbol(string name, string packageName, string packageVersion, string exportName, IReadOnlyList<ParameterSymbol> parameters, TypeSymbol resultType, TypeSymbol? remoteErrorType, bool isPromise, bool isAvailableToJavaScript, bool isAvailableToClrSidecar) : base(name) { PackageName = packageName; PackageVersion = packageVersion; ExportName = exportName; Parameters = parameters; ResultType = resultType; RemoteErrorType = remoteErrorType; IsPromise = isPromise; IsAvailableToJavaScript = isAvailableToJavaScript; IsAvailableToClrSidecar = isAvailableToClrSidecar; }
    public string PackageName { get; }
    public string PackageVersion { get; }
    public string ExportName { get; }
    public IReadOnlyList<ParameterSymbol> Parameters { get; }
    public TypeSymbol ResultType { get; }
    public TypeSymbol? RemoteErrorType { get; }
    public bool IsPromise { get; }
    public bool IsAvailableToJavaScript { get; }
    public bool IsAvailableToClrSidecar { get; }
    public TypeSymbol InvocationReturnType => RemoteErrorType is null
        ? new AsyncTypeSymbol(ResultType)
        : new AsyncTypeSymbol(new ResultTypeSymbol(ResultType, RemoteErrorType!));
}

public sealed class ClassValueSymbol(string name, ClassTypeSymbol classType) : Symbol(name)
{
    public ClassTypeSymbol ClassType { get; } = classType;
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

public sealed class RecordFieldSymbol(string name, RecordFieldId id, TypeSymbol type, bool isPublic = true) : Symbol(name)
{
    public RecordFieldId Id { get; } = id;
    public TypeSymbol Type { get; } = type;
    public bool IsPublic { get; } = isPublic;
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
