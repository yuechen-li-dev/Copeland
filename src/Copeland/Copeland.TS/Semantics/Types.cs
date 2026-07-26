namespace Copeland.TS.Semantics;

public abstract class TypeSymbol
{
    public abstract string Name { get; }

    public sealed override string ToString() => Name;
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    private PrimitiveTypeSymbol(string name) => Name = name;

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
    public NominalUnionProvenance? UnionProvenance { get; internal set; }

    public void AddCase(EnumCaseSymbol @case) => _cases.Add(@case);
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
    public static bool AreEquivalent(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (RecordTypeSymbol, RecordTypeSymbol) => false,
            (TableTypeSymbol, TableTypeSymbol) => false,
            (TableRowTypeSymbol, TableRowTypeSymbol) => false,
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
