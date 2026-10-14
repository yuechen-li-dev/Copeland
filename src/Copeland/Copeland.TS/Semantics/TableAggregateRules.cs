namespace Copeland.TS.Semantics;

/// <summary>Shared admission rules for authored summaries and ad-hoc relation queries.</summary>
internal static class TableAggregateRules
{
    public static bool IsGroupable(TypeSymbol type)
    {
        return IsOrderable(type) || type is EnumTypeSymbol || type == PrimitiveTypeSymbol.Boolean;
    }

    public static bool IsOrderable(TypeSymbol type)
    {
        return type == PrimitiveTypeSymbol.Int || type == PrimitiveTypeSymbol.Float
            || type == PrimitiveTypeSymbol.Number || type == PrimitiveTypeSymbol.String;
    }

    public static bool SupportsSum(TypeSymbol type)
    {
        return TypeFacts.IsNumeric(type);
    }
}
