using Copeland.TS.Syntax;
using Copeland.TS.Semantics.Bound;
using System.Globalization;

namespace Copeland.TS.Semantics;

/// <summary>A compile-time argument slot. It never becomes a runtime storage type.</summary>
public sealed class StaticArgumentTypeSymbol(TypeSymbol valueType, object value) : TypeSymbol
{
    public TypeSymbol ValueType { get; } = valueType;
    public object Value { get; } = value;
    public string CanonicalValue { get; } = Convert.ToString(value, CultureInfo.InvariantCulture)!;
    public override string Name => "static " + ValueType.Name + "=" + CanonicalValue;
}

public sealed class BoundGenericStaticExpression(TypeParameterTypeSymbol parameter, TypeSymbol valueType) : BoundExpression
{
    public TypeParameterTypeSymbol Parameter { get; } = parameter;
    public override TypeSymbol Type => valueType;
}

/// <summary>A typed scalar expression awaiting substitution of formal static arguments.</summary>
public sealed class StaticExpressionArgumentTypeSymbol(TypeSymbol valueType, BoundExpression expression, string identity) : TypeSymbol
{
    public TypeSymbol ValueType { get; } = valueType;
    public BoundExpression Expression { get; } = expression;
    public string Identity { get; } = identity;
    public override string Name => "static expression " + Identity;
}

public sealed class BoundOpenGenericCallExpression(
    FunctionSymbol function,
    IReadOnlyList<TypeSymbol> arguments,
    IReadOnlyList<BoundExpression> values,
    TypeSymbol returnType,
    SyntaxToken anchor
) : BoundExpression
{
    public FunctionSymbol Function { get; } = function;
    public IReadOnlyList<TypeSymbol> TypeArguments { get; } = arguments;
    public IReadOnlyList<BoundExpression> Arguments { get; } = values;
    public SyntaxToken Anchor { get; } = anchor;
    public override TypeSymbol Type => returnType;
}
