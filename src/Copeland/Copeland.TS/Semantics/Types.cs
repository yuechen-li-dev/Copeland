namespace Copeland.TS.Semantics;

public abstract class TypeSymbol
{
    public abstract string Name { get; }

    public sealed override string ToString() => Name;
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    private PrimitiveTypeSymbol(string name) => Name = name;

    /// <summary>Copeland's signed 32-bit whole-number type.</summary>
    public static readonly PrimitiveTypeSymbol Int = new("int");
    /// <summary>Copeland's IEEE-754 binary64 floating-point type.</summary>
    public static readonly PrimitiveTypeSymbol Float = new("float");
    /// <summary>
    /// TypeScript-compatible spelling for <see cref="Float"/>. It deliberately
    /// remains a distinct display symbol so diagnostics preserve authored source,
    /// while semantic equivalence treats it as float.
    /// </summary>
    public static readonly PrimitiveTypeSymbol Number = new("number");
    public static readonly PrimitiveTypeSymbol String = new("string");
    public static readonly PrimitiveTypeSymbol Boolean = new("boolean");
    public static readonly PrimitiveTypeSymbol Void = new("void");
    public static readonly PrimitiveTypeSymbol Error = new("error");

    public override string Name { get; }
}

public sealed class ArrayTypeSymbol(TypeSymbol elementType) : TypeSymbol
{
    public TypeSymbol ElementType { get; } = elementType;

    public override string Name => TypeText.FormatArrayElement(ElementType) + "[]";
}

/// <summary>
/// A finite, non-nominal compiler type. It describes data shape only and never
/// causes a CLR or JavaScript runtime carrier to be emitted.
/// </summary>
public class StructuralObjectTypeSymbol(IReadOnlyList<StructuralFieldSymbol> fields) : TypeSymbol
{
    public IReadOnlyList<StructuralFieldSymbol> Fields { get; } = fields;
    public override string Name => "{ " + string.Join("; ", Fields.Select(member => member.Name + (member.IsOptional ? "?" : "") + ": " + member.Type.Name)) + " }";
}

/// <summary>A finite compiler projection retaining its source and operation for tooling.</summary>
public sealed class StructuralProjectionTypeSymbol(
    string operation,
    TypeSymbol source,
    IReadOnlyList<StructuralFieldSymbol> fields) : StructuralObjectTypeSymbol(fields)
{
    public string Operation { get; } = operation;
    public TypeSymbol Source { get; } = source;
}

public sealed class StructuralFieldSymbol(string name, TypeSymbol type, int ordinal, bool isOptional, bool isReadOnly)
{
    public string Name { get; } = name;
    public TypeSymbol Type { get; } = type;
    public int Ordinal { get; } = ordinal;
    public bool IsOptional { get; } = isOptional;
    public bool IsReadOnly { get; } = isReadOnly;
}

public sealed class UnionTypeSymbol(IReadOnlyList<TypeSymbol> alternatives) : TypeSymbol
{
    public IReadOnlyList<TypeSymbol> Alternatives { get; } = alternatives;
    public override string Name => string.Join(" | ", Alternatives.Select(alternative => alternative.Name));
}

public sealed class IntersectionTypeSymbol(IReadOnlyList<TypeSymbol> parts) : TypeSymbol
{
    public IReadOnlyList<TypeSymbol> Parts { get; } = parts;
    public override string Name => string.Join(" & ", Parts.Select(part => part.Name));
}

/// <summary>Compiler-known structural artifact value types. They have no runtime representation.</summary>
public sealed class ArtifactTypeSymbol : TypeSymbol
{
    private ArtifactTypeSymbol(string name) => Name = name;
    public static readonly ArtifactTypeSymbol ProjectTree = new("ProjectTree");
    public static readonly ArtifactTypeSymbol FileArtifact = new("FileArtifact");
    public static readonly ArtifactTypeSymbol DirectoryArtifact = new("DirectoryArtifact");
    public static readonly ArtifactTypeSymbol TextFileArtifact = new("TextFileArtifact");
    public static readonly ArtifactTypeSymbol SourceFileArtifact = new("SourceFileArtifact");
    public static readonly ArtifactTypeSymbol ProjectFile = new("ProjectFile");
    public static readonly ArtifactTypeSymbol TestFile = new("TestFile");
    public static readonly ArtifactTypeSymbol DotNetSolution = new("DotNetSolution");
    public static readonly ArtifactTypeSymbol DotNetProject = new("DotNetProject");
    public static readonly ArtifactTypeSymbol TypeScriptWorkspace = new("TypeScriptWorkspace");
    public static readonly ArtifactTypeSymbol NpmPackageManifest = new("NpmPackageManifest");
    public static readonly ArtifactTypeSymbol NpmDependency = new("NpmDependency");
    public static readonly ArtifactTypeSymbol CopelandSourceSet = new("CopelandSourceSet");
    public static readonly ArtifactTypeSymbol CopelandProjectTypeSet = new("CopelandProjectTypeSet");
    public static readonly ArtifactTypeSymbol XmlElement = new("XmlElement");
    public override string Name { get; }
}

/// <summary>Compiler-bound CLR type identity retained independently of C# text emission.</summary>
public sealed class ClrTypeSymbol(Type runtimeType) : TypeSymbol
{
    public Type RuntimeType { get; } = runtimeType;
    public string AssemblyIdentity { get; } = runtimeType.Assembly.FullName ?? runtimeType.Assembly.GetName().Name ?? "<unknown>";
    public string Namespace { get; } = runtimeType.Namespace ?? string.Empty;
    public string MetadataName { get; } = runtimeType.FullName?.Replace('+', '.') ?? runtimeType.Name;
    public override string Name => MetadataName;
}

public sealed class ResultTypeSymbol(TypeSymbol successType, TypeSymbol errorType) : TypeSymbol
{
    public TypeSymbol SuccessType { get; } = successType;
    public TypeSymbol ErrorType { get; } = errorType;

    public override string Name => $"{TypeText.FormatResultComponent(SuccessType)} ! {ErrorType.Name}";
}

public sealed class AsyncTypeSymbol(TypeSymbol eventualType) : TypeSymbol
{
    public TypeSymbol EventualType { get; } = eventualType;

    public override string Name => $"Async<{EventualType.Name}>";
}

/// <summary>A compiler-owned synchronous pull sequence.</summary>
public sealed class IterableTypeSymbol(TypeSymbol elementType) : TypeSymbol
{
    public TypeSymbol ElementType { get; } = elementType;

    public override string Name => $"Iterable<{ElementType.Name}>";
}

public sealed class CallableTypeSymbol(IReadOnlyList<CallableParameterTypeSymbol> parameters, TypeSymbol returnType) : TypeSymbol
{
    public IReadOnlyList<CallableParameterTypeSymbol> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;
    public override string Name => "(" + string.Join(", ", Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + ") => " + ReturnType.Name;
}

public sealed class CallableParameterTypeSymbol(string name, TypeSymbol type)
{
    public string Name { get; } = name;
    public TypeSymbol Type { get; } = type;
}

public sealed class ErrorNominalTypeSymbol(string name) : TypeSymbol
{
    public override string Name { get; } = name;
}

public sealed record NominalUnionProvenance(string SourceName, IReadOnlyList<string> Alternatives);

public sealed class EnumTypeSymbol(string name, string? stableIdentity = null) : TypeSymbol
{
    private readonly List<EnumCaseSymbol> _cases = [];
    public override string Name { get; } = name;
    public IReadOnlyList<EnumCaseSymbol> Cases => _cases;
    public string? StableIdentity { get; } = stableIdentity;
    /// <summary>Backend-neutral enum spelling selected after module binding.</summary>
    public string EmissionName { get; internal set; } = name;
    public NominalUnionProvenance? UnionProvenance { get; internal set; }

    public void AddCase(EnumCaseSymbol @case) => _cases.Add(@case);
}

/// <summary>Opaque React values owned by the bounded React M0 profile.</summary>
public sealed class ReactNodeTypeSymbol : TypeSymbol
{
    public static ReactNodeTypeSymbol Instance { get; } = new();
    private ReactNodeTypeSymbol() { }
    public override string Name => "ReactNode";
}

/// <summary>
/// Immutable compiler-owned document data. Unlike ReactNode this is a normal
/// language value and has no renderer or package dependency.
/// </summary>
public sealed class DocumentTypeSymbol : TypeSymbol
{
    public static DocumentTypeSymbol Instance { get; } = new();
    private DocumentTypeSymbol() { }
    public override string Name => "Document";
}

public sealed class ReactRootTypeSymbol : TypeSymbol
{
    public static ReactRootTypeSymbol Instance { get; } = new();
    private ReactRootTypeSymbol() { }
    public override string Name => "ReactRoot";
}

public sealed class ReactMountElementTypeSymbol : TypeSymbol
{
    public static ReactMountElementTypeSymbol Instance { get; } = new();
    private ReactMountElementTypeSymbol() { }
    public override string Name => "ReactMountElement";
}

public readonly record struct RecordTypeId(int Value)
{
    public override string ToString() => $"r{Value}";
}

public readonly record struct RecordFieldId(RecordTypeId RecordTypeId, int Ordinal)
{
    public override string ToString() => $"{RecordTypeId}.f{Ordinal}";
}

public class RecordTypeSymbol(string name, RecordTypeId id, string? stableIdentity = null) : TypeSymbol
{
    private readonly List<RecordFieldSymbol> _fields = [];

    public override string Name { get; } = name;
    public RecordTypeId Id { get; } = id;
    public IReadOnlyList<RecordFieldSymbol> Fields => _fields;
    public string? StableIdentity { get; } = stableIdentity;
    /// <summary>Backend-neutral carrier spelling selected after module binding.</summary>
    public string EmissionName { get; internal set; } = name;

    public void AddField(RecordFieldSymbol field)
    {
        _fields.Add(field);
    }
}

/// <summary>
/// A class deliberately reuses the nominal record type algebra. The provenance is
/// retained only for source access control and backend carrier shape; it is not an
/// object-oriented runtime type.
/// </summary>
public sealed class ClassTypeSymbol(string name, RecordTypeId id, string? stableIdentity = null)
    : RecordTypeSymbol(name, id, stableIdentity)
{
    private readonly List<FunctionSymbol> _associatedFunctions = [];

    public FunctionSymbol? Constructor { get; private set; }
    public IReadOnlyList<FunctionSymbol> AssociatedFunctions => _associatedFunctions;

    public void SetConstructor(FunctionSymbol constructor) => Constructor = constructor;

    public void AddAssociatedFunction(FunctionSymbol function) => _associatedFunctions.Add(function);

    public FunctionSymbol? FindAssociatedFunction(string name)
        => _associatedFunctions.FirstOrDefault(function => function.MemberName == name);
}

public readonly record struct TableTypeId(int Value) { public override string ToString() => $"t{Value}"; }
public readonly record struct TableColumnId(TableTypeId TableTypeId, int Ordinal) { public override string ToString() => $"{TableTypeId}.c{Ordinal}"; }
public readonly record struct TableRowFieldId(TableColumnId ColumnId) { public override string ToString() => $"{ColumnId}.f"; }

public sealed class TableTypeSymbol(string name, TableTypeId id, string? stableIdentity = null) : TypeSymbol
{
    private readonly List<TableColumnSymbol> _columns = [];
    public override string Name { get; } = name;
    public TableTypeId Id { get; } = id;
    public string StableIdentity { get; } = stableIdentity ?? name;
    public int RowCount { get; set; } = -1;
    public TableRowTypeSymbol RowType { get; } = new(name + ".Row", id, (stableIdentity ?? name) + ".Row");
    public IReadOnlyList<TableColumnSymbol> Columns => _columns;
    public void AddColumn(TableColumnSymbol column) { _columns.Add(column); RowType.AddField(new TableRowFieldSymbol(column.Name, new TableRowFieldId(column.Id), column.Type)); }
}

public sealed class TableRowTypeSymbol(string name, TableTypeId tableId, string? stableIdentity = null) : TypeSymbol
{
    private readonly List<TableRowFieldSymbol> _fields = [];
    public override string Name { get; } = name;
    public TableTypeId TableId { get; } = tableId;
    public string StableIdentity { get; } = stableIdentity ?? name;
    public IReadOnlyList<TableRowFieldSymbol> Fields => _fields;
    public void AddField(TableRowFieldSymbol field) => _fields.Add(field);
}

/// <summary>
/// A non-owning, typed selection of logical positions in one table. This is a
/// compiler-only type: values lower to loops over the table's columnar storage.
/// </summary>
public sealed class TableRowsTypeSymbol : TypeSymbol
{
    public TableRowsTypeSymbol(TableTypeSymbol tableType)
    {
        TableType = tableType;
    }

    public TableTypeSymbol TableType { get; }
    public override string Name => TableType.Name + ".rows";
}

public sealed class ColumnTypeSymbol(TypeSymbol elementType) : TypeSymbol
{
    public TypeSymbol ElementType { get; } = elementType;
    public override string Name => "column " + TypeText.FormatResultComponent(ElementType);
}

public sealed class TypeParameterTypeSymbol(string name, int ordinal) : TypeSymbol
{
    public int Ordinal { get; } = ordinal;
    public override string Name { get; } = name;
}

public static class TypeFacts
{
    public static bool IsInt(TypeSymbol type) => type == PrimitiveTypeSymbol.Int;

    public static bool IsFloat(TypeSymbol type)
        => type == PrimitiveTypeSymbol.Float || type == PrimitiveTypeSymbol.Number;

    public static bool IsNumeric(TypeSymbol type) => IsInt(type) || IsFloat(type);

    public static bool AreEquivalent(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (IsFloat(left) && IsFloat(right))
        {
            return true;
        }

        return (left, right) switch
        {
            (RecordTypeSymbol, RecordTypeSymbol) => false,
            (TableTypeSymbol, TableTypeSymbol) => false,
            (TableRowTypeSymbol, TableRowTypeSymbol) => false,
            (TableRowsTypeSymbol leftRows, TableRowsTypeSymbol rightRows) => leftRows.TableType.Id == rightRows.TableType.Id,
            (ColumnTypeSymbol leftColumn, ColumnTypeSymbol rightColumn) => AreEquivalent(leftColumn.ElementType, rightColumn.ElementType),
            (ArrayTypeSymbol leftArray, ArrayTypeSymbol rightArray) => AreEquivalent(leftArray.ElementType, rightArray.ElementType),
            (ResultTypeSymbol leftResult, ResultTypeSymbol rightResult) =>
                AreEquivalent(leftResult.SuccessType, rightResult.SuccessType)
                && AreEquivalent(leftResult.ErrorType, rightResult.ErrorType),
            (AsyncTypeSymbol leftAsync, AsyncTypeSymbol rightAsync) =>
                AreEquivalent(leftAsync.EventualType, rightAsync.EventualType),
            (IterableTypeSymbol leftIterable, IterableTypeSymbol rightIterable) =>
                AreEquivalent(leftIterable.ElementType, rightIterable.ElementType),
            (CallableTypeSymbol leftCallable, CallableTypeSymbol rightCallable) =>
                leftCallable.Parameters.Count == rightCallable.Parameters.Count
                && leftCallable.Parameters.Zip(rightCallable.Parameters).All(pair => AreEquivalent(pair.First.Type, pair.Second.Type))
                && AreEquivalent(leftCallable.ReturnType, rightCallable.ReturnType),
            _ => left.GetType() == right.GetType() && left.Name == right.Name
        };
    }
}

internal static class TypeText
{
    public static string FormatArrayElement(TypeSymbol type)
        => type is ResultTypeSymbol or CallableTypeSymbol ? $"({type.Name})" : type.Name;

    public static string FormatResultComponent(TypeSymbol type)
        => type is ResultTypeSymbol or CallableTypeSymbol ? $"({type.Name})" : type.Name;
}
