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

public sealed class ResultTypeSymbol(TypeSymbol successType, TypeSymbol errorType) : TypeSymbol
{
    public TypeSymbol SuccessType { get; } = successType;
    public TypeSymbol ErrorType { get; } = errorType;

    public override string Name => $"{TypeText.FormatResultComponent(SuccessType)} ! {ErrorType.Name}";
}

public sealed class ErrorNominalTypeSymbol(string name) : TypeSymbol
{
    public override string Name { get; } = name;
}

public sealed class EnumTypeSymbol(string name, string? stableIdentity = null) : TypeSymbol
{
    private readonly List<EnumCaseSymbol> _cases = [];
    public override string Name { get; } = name;
    public IReadOnlyList<EnumCaseSymbol> Cases => _cases;
    public string? StableIdentity { get; } = stableIdentity;

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

public sealed class RecordTypeSymbol(string name, RecordTypeId id, string? stableIdentity = null) : TypeSymbol
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
            _ => left.GetType() == right.GetType() && left.Name == right.Name
        };
    }
}

internal static class TypeText
{
    public static string FormatArrayElement(TypeSymbol type)
        => type is ResultTypeSymbol ? $"({type.Name})" : type.Name;

    public static string FormatResultComponent(TypeSymbol type)
        => type is ResultTypeSymbol ? $"({type.Name})" : type.Name;
}
