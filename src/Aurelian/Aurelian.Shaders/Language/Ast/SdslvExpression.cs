namespace Aurelian.Shaders.Language.Ast;

public abstract record SdslvExpression;

public sealed record SdslvIdentifierExpression(string Name) : SdslvExpression;
public sealed record SdslvIntegerLiteralExpression(string Value) : SdslvExpression;
public sealed record SdslvFloatLiteralExpression(string Value) : SdslvExpression;
public sealed record SdslvStringLiteralExpression(string Value) : SdslvExpression;
public sealed record SdslvBoolLiteralExpression(bool Value) : SdslvExpression;

public sealed record SdslvArrayLiteralExpression(
    IReadOnlyList<SdslvExpression> Elements,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvFieldAccessExpression(
    SdslvExpression Base,
    string Field) : SdslvExpression;

public sealed record SdslvIndexExpression(
    SdslvExpression Base,
    SdslvExpression Index,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvCallExpression(
    SdslvExpression Callee,
    IReadOnlyList<SdslvExpression> Arguments) : SdslvExpression;

public sealed record SdslvBinaryExpression(
    SdslvExpression Left,
    SdslvBinaryOperator Operator,
    SdslvExpression Right) : SdslvExpression;

public sealed record SdslvUnaryExpression(
    SdslvUnaryOperator Operator,
    SdslvExpression Operand) : SdslvExpression;

public sealed record SdslvWithExpression(
    SdslvExpression Base,
    IReadOnlyList<SdslvWithUpdate> Updates) : SdslvExpression;

public sealed record SdslvSwitchExpression(
    SdslvExpression? Subject,
    IReadOnlyList<SdslvSwitchCase> Cases,
    SdslvExpression ElseValue,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvMatchExpression(
    SdslvExpression Subject,
    IReadOnlyList<SdslvMatchArm> Arms,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvWhenUtilityExpression(
    SdslvUtilityOptions? Options,
    IReadOnlyList<SdslvUtilityCase> Cases,
    SdslvExpression ElseValue,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvTryPropagateExpression(
    SdslvExpression Expression,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvUnwrapExpression(
    SdslvExpression Expression,
    SdslvSpan Span = default) : SdslvExpression;

public sealed record SdslvSwitchCase(
    SdslvExpression Condition,
    SdslvExpression Value,
    SdslvSpan Span = default);

public sealed record SdslvMatchArm(
    SdslvMatchArmKind Kind,
    SdslvExpression Value,
    SdslvSpan Span = default);

public abstract record SdslvMatchArmKind;

public sealed record SdslvEnumVariantMatchArmKind(SdslvPath VariantPath) : SdslvMatchArmKind;
public sealed record SdslvElseMatchArmKind : SdslvMatchArmKind;
public sealed record SdslvFallibleOkMatchArmKind(string Binding) : SdslvMatchArmKind;
public sealed record SdslvFallibleErrMatchArmKind(string Binding) : SdslvMatchArmKind;

public sealed record SdslvUtilityOptions(
    SdslvExpression? Hysteresis,
    SdslvExpression? MinCommit,
    SdslvSpan Span = default);

public sealed record SdslvUtilityCase(
    SdslvExpression Value,
    SdslvExpression Guard,
    SdslvExpression Score,
    SdslvSpan Span = default);

public sealed record SdslvWithUpdate(string Field, SdslvExpression Value);

public enum SdslvBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    LogicalOr,
    LogicalAnd,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}

public enum SdslvUnaryOperator
{
    Negate,
    Not,
}
