namespace Copeland.TS.Semantics;

/// <summary>A unit-bearing value using the ordinary immutable record representation.</summary>
public sealed class LengthTypeSymbol : RecordTypeSymbol
{
    public static LengthTypeSymbol Instance { get; } = new();

    private LengthTypeSymbol() : base("Length", new RecordTypeId(-9101), "copeland::spatial::Length")
    {
        AddField(new RecordFieldSymbol("value", new RecordFieldId(Id, 0), PrimitiveTypeSymbol.Number));
        AddField(new RecordFieldSymbol("unit", new RecordFieldId(Id, 1), PrimitiveTypeSymbol.String));
    }
}

/// <summary>Closed spatial data consumed before runtime MIR.</summary>
public sealed class LayoutValueTypeSymbol : TypeSymbol
{
    public static LayoutValueTypeSymbol Instance { get; } = new();
    public override string Name => "Layout";
}
