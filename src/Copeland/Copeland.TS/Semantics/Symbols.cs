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

public sealed class ParameterSymbol(string name, TypeSymbol type, string? authoredAliasName = null, bool isCaptured = false, bool isStatic = false) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
    public string? AuthoredAliasName { get; } = authoredAliasName;
    public bool IsCaptured { get; } = isCaptured;
    public bool IsStatic { get; } = isStatic;
}

public sealed class FunctionSymbol(
    string name,
    IReadOnlyList<ParameterSymbol> parameters,
    TypeSymbol returnType,
    string? authoredReturnAliasName = null,
    string? stableIdentity = null,
    bool isAsync = false,
    bool isGenerator = false,
    bool isRemote = false) : Symbol(name)
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
    public bool IsRemote { get; } = isRemote;
    public IReadOnlyList<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    public bool IsGeneric => TypeParameters.Count > 0;
    public ClassTypeSymbol? ClassOwner { get; internal set; }
    public string? MemberName { get; internal set; }
    public bool IsClassConstructor { get; internal set; }
    public bool IsPublic { get; internal set; } = true;
    public CallableTypeSymbol CallableType => new(Parameters.Select(parameter => new CallableParameterTypeSymbol(parameter.Name, parameter.Type)).ToArray(), InvocationReturnType);
    public TypeSymbol InvocationReturnType => IsAsync || IsRemote ? new AsyncTypeSymbol(ReturnType) : ReturnType;
}

/// <summary>Resolved template identity used by static evaluation, never runtime emission.</summary>
public sealed class TemplateSymbol(
    string name,
    IReadOnlyList<ParameterSymbol> parameters,
    TypeSymbol returnType,
    string stableIdentity) : Symbol(name)
{
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public string StableIdentity { get; } = stableIdentity;
    public IReadOnlyList<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
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
    public TypeSymbol InvocationReturnType => RemoteErrorType is null && !IsPromise
        ? ResultType
        : RemoteErrorType is null
            ? new AsyncTypeSymbol(ResultType)
            : new AsyncTypeSymbol(new ResultTypeSymbol(ResultType, RemoteErrorType));
}

public sealed class NpmComponentSymbol : Symbol
{
    public NpmComponentSymbol(
        string name,
        string packageName,
        string packageVersion,
        string exportName,
        IReadOnlyList<NpmComponentPropertySymbol> properties,
        IReadOnlyList<NpmComponentMemberSymbol> members,
        bool isAvailableToJavaScript)
        : base(name)
    {
        PackageName = packageName;
        PackageVersion = packageVersion;
        ExportName = exportName;
        Properties = properties;
        Members = members;
        IsAvailableToJavaScript = isAvailableToJavaScript;
    }

    public string PackageName { get; }
    public string PackageVersion { get; }
    public string ExportName { get; }
    public IReadOnlyList<NpmComponentPropertySymbol> Properties { get; }
    public IReadOnlyList<NpmComponentMemberSymbol> Members { get; }
    public bool IsAvailableToJavaScript { get; }
}

public sealed class NpmComponentMemberSymbol(
    string name,
    string localBinding,
    string packageName,
    string packageVersion,
    string exportName,
    IReadOnlyList<NpmComponentPropertySymbol> properties,
    bool isAvailableToJavaScript) : Symbol(name)
{
    public string LocalBinding { get; } = localBinding;
    public string PackageName { get; } = packageName;
    public string PackageVersion { get; } = packageVersion;
    public string ExportName { get; } = exportName;
    public IReadOnlyList<NpmComponentPropertySymbol> Properties { get; } = properties;
    public bool IsAvailableToJavaScript { get; } = isAvailableToJavaScript;
}

public sealed record NpmComponentPropertySymbol(string Name, TypeSymbol Type, bool IsRequired);

public sealed class NpmComponentNamespaceTypeSymbol : TypeSymbol
{
    public NpmComponentNamespaceTypeSymbol(NpmComponentSymbol component) => Component = component;
    public NpmComponentSymbol Component { get; }
    public override string Name => "ReactComponentNamespace<" + Component.ExportName + ">";
}

public sealed class ReactComponentTypeSymbol : TypeSymbol
{
    public ReactComponentTypeSymbol(NpmComponentMemberSymbol component) => Component = component;
    public NpmComponentMemberSymbol Component { get; }
    public override string Name => "ReactComponent<" + Component.Name + ">";
}

/// <summary>A statically bound function exported by a Copeland NuGet package module.</summary>
public sealed class CopelandPackageFunctionSymbol(
    string name,
    string packageId,
    string moduleSpecifier,
    string nominalScope,
    string exportName,
    IReadOnlyList<ParameterSymbol> parameters,
    TypeSymbol returnType,
    System.Reflection.MethodInfo method) : Symbol(name)
{
    public string PackageId { get; } = packageId;
    public string ModuleSpecifier { get; } = moduleSpecifier;
    public string NominalScope { get; } = nominalScope;
    public string ExportName { get; } = exportName;
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public System.Reflection.MethodInfo Method { get; } = method;
    public string StableIdentity => PackageId + "/" + Method.DeclaringType!.Assembly.GetName().Name + "/" + ModuleSpecifier + "/" + NominalScope + "/" + ExportName;
}

public sealed class JavaScriptHostFunctionSymbol : Symbol
{
    public JavaScriptHostFunctionSymbol(
        string name,
        string moduleSpecifier,
        string exportName,
        IReadOnlyList<ParameterSymbol> parameters,
        TypeSymbol returnType,
        IReadOnlyList<TypeParameterSymbol>? typeParameters = null)
        : base(name)
    {
        ModuleSpecifier = moduleSpecifier;
        ExportName = exportName;
        Parameters = parameters;
        ReturnType = returnType;
        TypeParameters = typeParameters ?? [];
    }

    public string ModuleSpecifier { get; }
    public string ExportName { get; }
    public IReadOnlyList<ParameterSymbol> Parameters { get; }
    public TypeSymbol ReturnType { get; }
    public IReadOnlyList<TypeParameterSymbol> TypeParameters { get; }
    public bool IsGeneric => TypeParameters.Count > 0;
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
